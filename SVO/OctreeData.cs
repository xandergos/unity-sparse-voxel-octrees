using System;
using System.Collections.Generic;
using System.IO;
using UnityEngine;

namespace SVO
{
    public class OctreeData
    {
        public static OctreeData Load(string filePath)
        {
            var fstream = File.OpenRead(filePath);
            
            var structureData = new List<int>();
            var shadingData = new List<int>();
            var freeStructureMemory = new Queue<int>();
            var freeShadingMemory = new List<int>();
            
            var buffer = new byte[4];
            fstream.Read(buffer, 0, 4);
            var s = BitConverter.ToInt32(buffer, 0);
            structureData.Capacity = Mathf.NextPowerOfTwo(s);
            for (var j = 0; j < s; j++)
            {
                fstream.Read(buffer, 0, 4);
                var n = BitConverter.ToInt32(buffer, 0);
                structureData.Add(n);
            }
            
            fstream.Read(buffer, 0, 4);
            s = BitConverter.ToInt32(buffer, 0);
            shadingData.Capacity = Mathf.NextPowerOfTwo(s);
            for (var j = 0; j < s; j++)
            {
                fstream.Read(buffer, 0, 4);
                var n = BitConverter.ToInt32(buffer, 0);
                shadingData.Add(n);
            }
            
            fstream.Read(buffer, 0, 4);
            s = BitConverter.ToInt32(buffer, 0);
            for (var j = 0; j < s; j++)
            {
                fstream.Read(buffer, 0, 4);
                var n = BitConverter.ToInt32(buffer, 0);
                freeStructureMemory.Enqueue(n);
            }
            
            fstream.Read(buffer, 0, 4);
            s = BitConverter.ToInt32(buffer, 0);
            freeShadingMemory.Capacity = Mathf.NextPowerOfTwo(s);
            for (var j = 0; j < s; j++)
            {
                fstream.Read(buffer, 0, 4);
                var n = BitConverter.ToInt32(buffer, 0);
                freeShadingMemory.Add(n);
            }
            
            fstream.Dispose();
            
            return new OctreeData(structureData, shadingData, freeStructureMemory, freeShadingMemory);
        }
        
        private static readonly byte[] ShadingDataSizeTable = new byte[256];

        static OctreeData()
        {
            for (var i = 0; i < 256; i++)
            {
                // Count number of set bits. Naive algorithm.
                byte c = 0;
                for (var mask = 1; mask < 256; mask <<= 1)
                {
                    c += Convert.ToByte((i & mask) > 0);
                }

                ShadingDataSizeTable[i] = c;
            }
        }

        public static OctreeData FromMesh(Mesh mesh, int depth, bool autoTransform, Func<Bounds, Color> color, int submesh=0)
        {
            var data = new OctreeData();

            void Fill(int fillDepth, Bounds bounds, Vector3[] triangleVerts, Vector3 normal)
            {
                if (TriBoxOverlap.IsIntersecting(bounds, triangleVerts))
                {
                    if (depth == fillDepth)
                    {
                        data.SetSolidVoxel(bounds.min, depth, VoxelAttribute.Encode(color(bounds), normal));
                    }
                    else
                    {
                        for (var i = 0; i < 8; i++)
                        {
                            var nextCenter = 0.5f * bounds.extents + bounds.min;
                            if ((i & 4) > 0) nextCenter.x += bounds.extents.x;
                            if ((i & 2) > 0) nextCenter.y += bounds.extents.y;
                            if ((i & 1) > 0) nextCenter.z += bounds.extents.z;
                            Fill(fillDepth + 1, new Bounds(nextCenter, bounds.extents), triangleVerts, normal);
                        }
                    }
                }
            }
            
            var scaleVec = new Vector3(.5f, .5f, .5f);
            scaleVec.Scale(new Vector3(1/mesh.bounds.extents.x, 1/mesh.bounds.extents.y, 1/mesh.bounds.extents.z));
            var scale = Mathf.Min(Mathf.Min(scaleVec.x, scaleVec.y), scaleVec.z);
            
            var vertices = mesh.vertices;
            var indices = mesh.GetIndices(submesh);
            var normals = mesh.normals;

            for (var i = 0; i < indices.Length; i += 3)
            {
                var normal = normals[indices[i]];
                Vector3[] triVerts;
                if (autoTransform)
                    triVerts = new[]
                    {
                        vertices[indices[i]] - mesh.bounds.center,
                        vertices[indices[i + 1]] - mesh.bounds.center,
                        vertices[indices[i + 2]] - mesh.bounds.center
                    };
                else
                    triVerts = new[]
                    {
                        vertices[indices[i]],
                        vertices[indices[i + 1]],
                        vertices[indices[i + 2]]
                    };
                for (var j = 0; j < 3; j++)
                {
                    if(autoTransform) triVerts[j] *= scale;
                    triVerts[j] += new Vector3(1.5f, 1.5f, 1.5f);
                }
                
                Fill(0, new Bounds(new Vector3(1.5f, 1.5f, 1.5f), Vector3.one), triVerts, normal);
            }

            return data;
        }

        // These lists are effectively unmanaged memory blocks. This allows for a simple transition from CPU to GPU memory,
        // and is much faster for the C# gc to deal with.
        internal List<int> StructureData = new List<int>(new[] { 1 << 31 });
        internal List<int> ShadingData = new List<int>(new[] { 0 });

        internal Queue<int> FreeStructureMemory = new Queue<int>();
        internal List<int> FreeShadingMemory = new List<int>();
        
        private readonly int[] _ptrStack = new int[24];
        private Vector3 _ptrStackPos = Vector3.one;
        private int _ptrStackDepth;

        public OctreeData()
        {
            _ptrStack[0] = 0;
        }

        internal OctreeData(List<int> structureData, List<int> shadingData, 
            Queue<int> freeStructureMemory, List<int> freeShadingMemory)
        {
            StructureData = structureData;
            ShadingData = shadingData;
            FreeStructureMemory = freeStructureMemory;
            FreeShadingMemory = freeShadingMemory;
        }

        /**
         * position: Position of the voxel, with each component in the range [1, 2).
         * depth: Depth of the voxel. A depth of n means a voxel of editDepth pow(2f, -n)
         * shadingData: The shading data for the voxel. From VoxelAttributes.Encode().
         */
        public void SetSolidVoxel(Vector3 position, int depth, int[] shadingData)
        {
            static unsafe int AsInt(float f) => *(int*)&f;
            static int FirstSetHigh(int i) => (AsInt(i) >> 23) - 127;
            
            Debug.Assert(position.x < 2 && position.x >= 1);
            Debug.Assert(position.y < 2 && position.y >= 1);
            Debug.Assert(position.z < 2 && position.z >= 1);
            
            // This can skip a lot of tree iterations if the last voxel set was near this one.
            var differingBits = AsInt(_ptrStackPos.x) ^ AsInt(position.x);
            differingBits |= AsInt(_ptrStackPos.y) ^ AsInt(position.y);
            differingBits |= AsInt(_ptrStackPos.z) ^ AsInt(position.z);
            var firstSet = 23 - FirstSetHigh(differingBits);
            var stepDepth = Math.Min(Math.Min(firstSet - 1, _ptrStackDepth), depth);
            
            var ptr = _ptrStack[stepDepth];
            var type = (StructureData[ptr] >> 31) & 1; // Type of root node
            // Step down one depth until a non-ptr node is hit or the max depth is reached.
            while(type == 0 && stepDepth < depth)
            {
                // Descend to the next branch
                ptr = StructureData[ptr]; 
                
                // Step to next node
                stepDepth++;
                var xm = (AsInt(position.x) >> (23 - stepDepth)) & 1;
                var ym = (AsInt(position.y) >> (23 - stepDepth)) & 1;
                var zm = (AsInt(position.z) >> (23 - stepDepth)) & 1;
                var childIndex = (xm << 2) + (ym << 1) + zm;
                ptr += childIndex;
                _ptrStack[stepDepth] = ptr;
                
                // Get type of the node
                type = (StructureData[ptr] >> 31) & 1;
            }
            _ptrStackDepth = stepDepth;
            _ptrStackPos = position;

            // Pointer will be deleted, so all children must be as well.
            if (type == 0)
                FreeStructureMemory.Enqueue(StructureData[ptr]);

            var original = StructureData[ptr];
            int[] originalShadingData;
            if (type == 1 && original != 1 << 31)
            {
                // Get the shading data
                var shadingPtr = original & 0x7FFFFFFF;
                var size = ShadingDataSizeTable[Math.Abs(ShadingData[shadingPtr] >> 24)];
                originalShadingData = new int[size];
                for (var i = 0; i < size; i++)
                    originalShadingData[i] = ShadingData[shadingPtr + i];
                FreeShadingMemory.Add(shadingPtr);
            }
            else originalShadingData = new int[0];
            while (stepDepth < depth)
            {
                stepDepth++;
                // Create another branch to go down another depth
                // The last hit voxel MUST be type 1. Otherwise stepDepth == depth.
                var branchPtr = FreeStructureMemory.Count > 0 ? FreeStructureMemory.Dequeue() : StructureData.Count;
                if (branchPtr == StructureData.Count)
                {
                    for (var i = 0; i < 8; i++)
                        StructureData.Add((1 << 31) | AllocateShadingData(originalShadingData));
                }
                else {
                    for (var i = 0; i < 8; i++)
                        StructureData[branchPtr + i] = (1 << 31) | AllocateShadingData(originalShadingData);
                }
                _ptrStack[stepDepth] = branchPtr;
                StructureData[ptr] = branchPtr;
                ptr = branchPtr;
                
                // Move to the position of the right child node.
                var xm = (AsInt(position.x) >> (23 - stepDepth)) & 1;
                var ym = (AsInt(position.y) >> (23 - stepDepth)) & 1;
                var zm = (AsInt(position.z) >> (23 - stepDepth)) & 1;
                var childIndex = (xm << 2) + (ym << 1) + zm;
                ptr += childIndex;
            }
            StructureData[ptr] = (1 << 31) | AllocateShadingData(shadingData);
        }

        public RayHit? CastRay(Ray ray, Vector3 octreeScale, Vector3 octreePos)
        {
            static unsafe int AsInt(float f) => *(int*)&f;
            static unsafe float AsFloat(int i) => *(float*)&i;
            static int FirstSetHigh(int i) => (AsInt(i) >> 23) - 127;

            ray.direction.Scale(new Vector3(1 / octreeScale.x, 1 / octreeScale.y, 1 / octreeScale.z));
            ray.direction.Normalize();
            var rayOrigin = ray.origin;
            rayOrigin += octreeScale * 1.5f;
            rayOrigin -= octreePos;
            rayOrigin.Scale(new Vector3(1 / octreeScale.x, 1 / octreeScale.y, 1 / octreeScale.z));
            
            const int maxDepth = 23;
            const float epsilon = 0.00000011920929f;
            // Mirror coordinate system such that all ray direction components are negative.
            int signMask = 0;
            if (ray.direction.x > 0f)
            {
                signMask ^= 4;
                rayOrigin.x = 3f - rayOrigin.x;
            }

            if (ray.direction.y > 0f)
            {
                signMask ^= 2;
                rayOrigin.y = 3f - rayOrigin.y;
            }

            if (ray.direction.z > 0f)
            {
                signMask ^= 1;
                rayOrigin.z = 3f - rayOrigin.z;
            }

            ray.direction = -new Vector3(Mathf.Abs(ray.direction.x), Mathf.Abs(ray.direction.y), Mathf.Abs(ray.direction.z));
            
            // Get intersections of chunk (if hit)
            Vector3 rootMinDistances = Vector3.one * 2f - rayOrigin;
            rootMinDistances.Scale(new Vector3(1 / ray.direction.x, 1 / ray.direction.y, 1 / ray.direction.z));
            Vector3 rootMaxDistances = Vector3.one - rayOrigin;
            rootMaxDistances.Scale(new Vector3(1 / ray.direction.x, 1 / ray.direction.y, 1 / ray.direction.z));
            float rootTmin = Mathf.Max(Mathf.Max(Mathf.Max(rootMinDistances.x, rootMinDistances.y), rootMinDistances.z), 0);
            float rootTmax = Mathf.Min(Mathf.Min(rootMaxDistances.x, rootMaxDistances.y), rootMaxDistances.z);
            
            if(rootTmax < 0 || rootTmin >= rootTmax)
                return null;
            
            Vector3 nextPath = rayOrigin + ray.direction * rootTmin;
            nextPath.x = Mathf.Clamp(nextPath.x, 1f, AsFloat(0x3fffffff));
            nextPath.y = Mathf.Clamp(nextPath.y, 1f, AsFloat(0x3fffffff));
            nextPath.z = Mathf.Clamp(nextPath.z, 1f, AsFloat(0x3fffffff));
            
            int[] stack = new int[maxDepth + 1];
            stack[0] = 0;
            int stackDepth = 0;
            Vector3 stackPath = new Vector3(1, 1, 1);

            do
            {
                // GET voxel at targetPos
                int differingBits = AsInt(stackPath.x) ^ AsInt(nextPath.x);
                differingBits |= AsInt(stackPath.y) ^ AsInt(nextPath.y);
                differingBits |= AsInt(stackPath.z) ^ AsInt(nextPath.z);
                int firstSet = 23 - FirstSetHigh(differingBits);
                int depth = Mathf.Min(firstSet - 1, stackDepth);
                int ptr = stack[depth];
                int type = StructureData[ptr] >> 31 & 1;
                while(type == 0)
                {
                    ptr = StructureData[ptr];
                    depth++;
                    int xm = AsInt(nextPath.x) >> 23 - depth & 1;
                    int ym = AsInt(nextPath.y) >> 23 - depth & 1;
                    int zm = AsInt(nextPath.z) >> 23 - depth & 1;
                    int childIndex = (xm << 2) + (ym << 1) + zm;
                    childIndex ^= signMask;
                    ptr += childIndex;
                    stack[depth] = ptr;
                    type = StructureData[ptr] >> 31 & 1;
                }
                stackDepth = depth;
                // Remove unused bits
                stackPath.x = AsFloat(AsInt(nextPath.x) & ~((1 << 23 - depth) - 1));
                stackPath.y = AsFloat(AsInt(nextPath.y) & ~((1 << 23 - depth) - 1));
                stackPath.z = AsFloat(AsInt(nextPath.z) & ~((1 << 23 - depth) - 1));
                
                // Return hit if voxel is solid
                if(type == 1 && StructureData[ptr] != 1 << 31)
                {
                    RayHit hit = new RayHit();

                    int shadingPtr = StructureData[ptr] & 0x7FFFFFFF;
                    
                    int colorData = ShadingData[shadingPtr];
                    hit.Color = new Color((colorData >> 16 & 0xFF) / 255f, (colorData >> 8 & 0xFF) / 255f, (colorData & 0xFF) / 255f);
                    
                    // Normals transformed to [0, 1] range
                    int normalData = ShadingData[shadingPtr + 1];
                    int normalSignBit = normalData >> 22 & 1;
                    int axis = normalData >> 20 & 3;
                    int comp2 = normalData >> 10 & 0x3FF;
                    int comp1 = normalData & 0x3FF;

                    Vector3 normal = new Vector3(0, 0, 0);
                    switch(axis)
                    {
                        case 0:
                            normal.x = normalSignBit * 2f - 1;
                            normal.y = comp2 / 1023f * 2f - 1;
                            normal.z = comp1 / 1023f * 2f - 1;
                            break;
                        case 1:
                            normal.x = comp2 / 1023f * 2f - 1;
                            normal.y = normalSignBit * 2f - 1;
                            normal.z = comp1 / 1023f * 2f - 1;
                            break;
                        case 2:
                            normal.x = comp2 / 1023f * 2f - 1;
                            normal.y = comp1 / 1023f * 2f - 1;
                            normal.z = normalSignBit * 2f - 1;
                            break;
                    }
                    normal.Normalize();
                    hit.Normal = normal;

                    // Undo coordinate mirroring in next_path
                    Vector3 mirroredPath = nextPath;
                    //float editDepth = exp2(-depth);
                    if(signMask >> 2 != 0) mirroredPath.x = 3f - nextPath.x;
                    if((signMask >> 1 & 1) != 0) mirroredPath.y = 3f - nextPath.y;
                    if((signMask & 1) != 0) mirroredPath.z = 3f - nextPath.z;
                    hit.OctreePosition = mirroredPath;
                    hit.WorldPosition = mirroredPath - Vector3.one * 1.5f;
                    hit.WorldPosition.Scale(octreeScale);
                    hit.WorldPosition += octreePos;
                    
                    var xNear = AsFloat(AsInt(stackPath.x) + (1 << (23 - depth)));
                    var yNear = AsFloat(AsInt(stackPath.y) + (1 << (23 - depth)));
                    var zNear = AsFloat(AsInt(stackPath.z) + (1 << (23 - depth)));
                    var txMin = (xNear - rayOrigin.x) / ray.direction.x;
                    var tyMin = (yNear - rayOrigin.y) / ray.direction.y;
                    var tzMin = (zNear - rayOrigin.z) / ray.direction.z;
                    var tMin = Mathf.Max(Mathf.Max(txMin, tyMin), tzMin);

                    hit.FaceNormal = Vector3.zero;
                    if (txMin >= tMin)
                    {
                        hit.OctreePosition.x = xNear;
                        if ((signMask & 4) != 0)
                        {
                            hit.OctreePosition.x = 3f - hit.OctreePosition.x;
                        }
                        hit.FaceNormal.x = (signMask & 4) == 0 ? 1 : -1;
                    }
                    else if (tyMin >= tMin)
                    {
                        hit.OctreePosition.y = yNear;
                        if ((signMask & 2) != 0)
                        {
                            hit.OctreePosition.y = 3f - hit.OctreePosition.y;
                        }
                        hit.FaceNormal.y = (signMask & 2) == 0 ? 1 : -1;
                    }
                    else if (tzMin >= tMin)
                    {
                        hit.OctreePosition.z = zNear;
                        if ((signMask & 1) != 0)
                        {
                            hit.OctreePosition.z = 3f - hit.OctreePosition.z;
                        }
                        hit.FaceNormal.z = (signMask & 1) == 0 ? 1 : -1;
                    }
                    
                    return hit;
                }

                // Step to the next voxel by moving along the normal on the far side of the voxel that was hit.
                var xFar = stackPath.x;
                var yFar = stackPath.y;
                var zFar = stackPath.z;
                var txMax = (xFar - rayOrigin.x) / ray.direction.x;
                var tyMax = (yFar - rayOrigin.y) / ray.direction.y;
                var tzMax = (zFar - rayOrigin.z) / ray.direction.z;
                var tMax = Mathf.Min(Mathf.Min(txMax, tyMax), tzMax);
                nextPath.x = Mathf.Clamp(rayOrigin.x + ray.direction.x * tMax, stackPath.x, 
                    AsFloat(AsInt(stackPath.x) + (1 << (23 - depth)) - 1));
                nextPath.y = Mathf.Clamp(rayOrigin.y + ray.direction.y * tMax, stackPath.y, 
                    AsFloat(AsInt(stackPath.y) + (1 << (23 - depth)) - 1));
                nextPath.z = Mathf.Clamp(rayOrigin.z + ray.direction.z * tMax, stackPath.z, 
                    AsFloat(AsInt(stackPath.z) + (1 << (23 - depth)) - 1));

                if(txMax <= tMax) nextPath.x = xFar - epsilon;
                if(tyMax <= tMax) nextPath.y = yFar - epsilon;
                if(tzMax <= tMax) nextPath.z = zFar - epsilon;
            }
            while((AsInt(nextPath.x) & 0xFF800000) == 0x3f800000 && 
                  (AsInt(nextPath.y) & 0xFF800000) == 0x3f800000 && 
                  (AsInt(nextPath.z) & 0xFF800000) == 0x3f800000);

            return null;
        }

        private int AllocateShadingData(int[] shadingData)
        {
            if (shadingData.Length == 0) return 0;
            var index = 0;
            foreach (var ptr in FreeShadingMemory)
            {
                var size = ShadingDataSizeTable[(uint)ShadingData[ptr] >> 24];
                if (size < shadingData.Length)
                {
                    index++;
                    continue;
                }
                
                for (var i = 0; i < shadingData.Length; i++)
                {
                    ShadingData[ptr + i] = shadingData[i];
                }
                FreeShadingMemory.RemoveAt(index);
                return ptr;
            }

            var endPtr = ShadingData.Count;
            ShadingData.AddRange(shadingData);
            return endPtr;
        }
        
        public void Save(string filePath)
        {
            var fstream = File.OpenWrite(filePath);
            
            fstream.Write(BitConverter.GetBytes(StructureData.Count), 0, 4);
            foreach (var n in StructureData)
                fstream.Write(BitConverter.GetBytes(n), 0, 4);

            fstream.Write(BitConverter.GetBytes(ShadingData.Count), 0, 4);
            foreach (var n in ShadingData)
                fstream.Write(BitConverter.GetBytes(n), 0, 4);

            fstream.Write(BitConverter.GetBytes(FreeStructureMemory.Count), 0, 4);
            foreach (var n in FreeStructureMemory)
                fstream.Write(BitConverter.GetBytes(n), 0, 4);

            fstream.Write(BitConverter.GetBytes(FreeShadingMemory.Count), 0, 4);
            foreach (var n in FreeShadingMemory)
                fstream.Write(BitConverter.GetBytes(n), 0, 4);
            
            fstream.Flush();
            fstream.Dispose();
        }
    }
}