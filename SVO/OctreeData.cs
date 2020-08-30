using System;
using System.Collections.Generic;
using System.IO;
using UnityEngine;

namespace SVO
{
    public class OctreeData
    {
        public static void Load(FileStream inputStream)
        {
            throw new NotImplementedException("TODO");
        }

        private static readonly byte[] ShadingDataSizeTable = new byte[256];

        static OctreeData()
        {
            for (int i = 0; i < 256; i++)
            {
                // Count number of set bits. Naive algorithm.
                byte c = 0;
                for (int mask = 1; mask < 256; mask <<= 1)
                {
                    c += Convert.ToByte((i & mask) > 0);
                }

                ShadingDataSizeTable[i] = c;
            }
        }

        public static OctreeData FromMesh(Mesh mesh, int depth, int submesh=0)
        {
            var data = new OctreeData();

            void Fill(int fillDepth, Bounds bounds, Vector3[] triangleVerts, Vector3 normal)
            {
                if (TriBoxOverlap.IsIntersecting(bounds, triangleVerts))
                {
                    if (depth == fillDepth)
                    {
                        data.SetSolidVoxel(bounds.min, depth, VoxelAttribute.Encode(Color.white, normal));
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
            
            var scale = new Vector3(.5f, .5f, .5f);
            scale.Scale(new Vector3(1/mesh.bounds.extents.x, 1/mesh.bounds.extents.y, 1/mesh.bounds.extents.z));
            
            var vertices = mesh.vertices;
            var indices = mesh.GetIndices(submesh);
            var normals = mesh.normals;

            for (var i = 0; i < indices.Length; i += 3)
            {
                var normal = normals[i];
                Vector3[] triVerts = {
                    vertices[indices[i]] - mesh.bounds.center,
                    vertices[indices[i + 1]] - mesh.bounds.center,
                    vertices[indices[i + 2]] - mesh.bounds.center
                };
                for (var j = 0; j < 3; j++)
                {
                    triVerts[j].Scale(scale);
                    triVerts[j] += new Vector3(1.5f, 1.5f, 1.5f);
                }
                
                Fill(0, new Bounds(new Vector3(1.5f, 1.5f, 1.5f), Vector3.one), triVerts, normal);
            }

            return data;
        }

        // These lists are effectively unmanaged memory blocks. This allows for a simple transition from CPU to GPU memory,
        // and is much faster for the C# gc to deal with.
        internal readonly List<int> StructureData = new List<int>(new[] { 1 << 31 });
        internal readonly List<int> ShadingData = new List<int>(new[] { 0 });
        
        private readonly Queue<int> _freeStructureMemory = new Queue<int>();
        private readonly List<int> _freeShadingMemory = new List<int>();
        
        private readonly int[] _ptrStack = new int[24];
        private Vector3 _ptrStackPos = Vector3.one;
        private int _ptrStackDepth;

        public OctreeData()
        {
            _ptrStack[0] = 0;
        }

        internal OctreeData(List<int> structureData, List<int> shadingData)
        {
            StructureData = structureData;
            ShadingData = shadingData;
        }

        /**
         * position: Position of the voxel, with each component in the range [1, 2).
         * depth: Depth of the voxel. A depth of n means a voxel of size pow(2f, -n)
         * shadingData: The shading data for the voxel. From VoxelAttributes.Encode().
         */
        public void SetSolidVoxel(Vector3 position, int depth, IEnumerable<int> shadingData)
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
                _freeStructureMemory.Enqueue(StructureData[ptr]);

            var original = StructureData[ptr];
            int[] originalShadingData;
            if (type == 1 && original != 1 << 31)
            {
                // Get the shading data
                var shadingPtr = original & 0x7FFFFFFF;
                var size = ShadingDataSizeTable[ShadingData[shadingPtr] >> 24];
                originalShadingData = new int[size];
                for (var i = 0; i < size; i++)
                    originalShadingData[i] = ShadingData[shadingPtr + i];
                _freeShadingMemory.Add(shadingPtr);
            }
            else originalShadingData = new int[0];
            while (stepDepth < depth)
            {
                stepDepth++;
                // Create another branch to go down another depth
                // The last hit voxel MUST be type 1. Otherwise stepDepth == depth.
                var branchPtr = _freeStructureMemory.Count > 0 ? _freeStructureMemory.Dequeue() : StructureData.Count;
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
                int xm = (AsInt(position.x) >> (23 - stepDepth)) & 1;
                int ym = (AsInt(position.y) >> (23 - stepDepth)) & 1;
                int zm = (AsInt(position.z) >> (23 - stepDepth)) & 1;
                int childIndex = (xm << 2) + (ym << 1) + zm;
                ptr += childIndex;
            }
            StructureData[ptr] = (1 << 31) | ShadingData.Count;
            ShadingData.AddRange(shadingData);
        }

        private int AllocateShadingData(IReadOnlyList<int> shadingData)
        {
            if (shadingData.Count == 0) return 0;
            foreach (var ptr in _freeShadingMemory)
            {
                var size = ShadingDataSizeTable[ShadingData[ptr] >> 24];
                if (size < shadingData.Count) continue;
                
                for (var i = 0; i < shadingData.Count; i++)
                {
                    ShadingData[ptr + i] = shadingData[i];
                }
                return ptr;
            }

            var endPtr = ShadingData.Count;
            ShadingData.AddRange(shadingData);
            return endPtr;
        }

        public void Save(String filePath)
        {
            throw new NotImplementedException("TODO");
        }
    }
}