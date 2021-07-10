/*
 *  Unity Sparse Voxel Octrees
 *  Copyright (C) 2021  Alexander Goslin
 *
 *  This program is free software: you can redistribute it and/or modify
 *  it under the terms of the GNU General Public License as published by
 *  the Free Software Foundation, either version 3 of the License, or
 *  (at your option) any later version.
 *
 *  This program is distributed in the hope that it will be useful,
 *  but WITHOUT ANY WARRANTY; without even the implied warranty of
 *  MERCHANTABILITY or FITNESS FOR A PARTICULAR PURPOSE.  See the
 *  GNU General Public License for more details.
 *
 *  You should have received a copy of the GNU General Public License
 *  along with this program.  If not, see <https://www.gnu.org/licenses/>.
 */

using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using Object = UnityEngine.Object;

namespace SVO
{
    public class Octree : IDisposable
    {
        private static Texture3D tempTex = null;
        
        public Texture3D Data { get; private set; }

        // These lists are effectively unmanaged memory blocks. This allows for a simple transition from CPU to GPU memory,
        // and is much easier for the C# gc to deal with.
        private List<int> _data = new List<int>(new[] { 1 << 31 });
        private readonly HashSet<int> _freeStructureMemory = new HashSet<int>();
        private readonly HashSet<int> _freeAttributeMemory = new HashSet<int>();
        private ulong[] _updateCount = new ulong[2048];
        private ulong[] _lastApply = new ulong[2048];

        private int[] _ptrStack = new int[24];
        private Vector3 _ptrStackPos = Vector3.one;
        private int _ptrStackDepth;

        public Octree(Texture3D data)
        {
            for (int i = 0; i < _lastApply.Length; i++)
                _lastApply[i] = ulong.MaxValue;
            _ptrStack[0] = 0;
            Data = data;
        }

        public Octree()
        {
            _ptrStack[0] = 0;
            Data = null;
        }
        
        /// <summary>
        /// Edits the voxel at some position with depth and attributes data.
        /// </summary>
        /// <param name="position">Position of the voxel, with each component in the range [-.5, .5).</param>
        /// <param name="depth">Depth of the voxel. A depth of n means a voxel of editDepth pow(2f, -n)</param>
        /// <param name="color">Color of the voxel. Transparency is currently ignored, unless an alpha value of 0 is
        /// provided, in which case the voxel is simply deleted.</param>
        /// <param name="attributes">Shading data for the voxel. Best for properties like normals.</param>
        public void SetVoxel(Vector3 position, int depth, Color color, int[] attributes)
        {
            SetVoxelNormalized(position + new Vector3(1.5f, 1.5f, 1.5f), depth, color, attributes);
        }

        /// <summary>
        /// Edits the voxel at some position with depth and attributes data.
        /// </summary>
        /// <param name="position">Position of the voxel, with each component in the range [1, 2).</param>
        /// <param name="depth">Depth of the voxel. A depth of n means a voxel of size pow(2f, -n)</param>
        /// <param name="color">Color of the voxel</param>
        /// <param name="attributes">Shading data for the voxel. Best for properties like normals.</param>
        private void SetVoxelNormalized(Vector3 position, int depth, Color color, int[] attributes)
        {
            unsafe int AsInt(float f) => *(int*)&f;
            int FirstSetHigh(int i) => (AsInt(i) >> 23) - 127;

            position.x = Mathf.Clamp(position.x, 1f, 1.99999988079f);
            position.y = Mathf.Clamp(position.y, 1f, 1.99999988079f);
            position.z = Mathf.Clamp(position.z, 1f, 1.99999988079f);
            
            // Create 'internalAttributes' which is the same as attributes
            // but with one extra int at the beggining for color and metadata
            int[] internalAttributes = null;
            if(color.a != 0f)
            {
                internalAttributes = new int[attributes.Length + 1];
                internalAttributes[0] |= attributes.Length + 1 << 24;
                internalAttributes[0] |= (int)(color.r * 255) << 16;
                internalAttributes[0] |= (int)(color.g * 255) << 8;
                internalAttributes[0] |= (int)(color.b * 255) << 0;
                for (var i = 0; i < attributes.Length; i++) internalAttributes[i + 1] = attributes[i];
            }
            
            // Attempt to skip initial tree iterations by reusing the position of the last voxel
            var differingBits = AsInt(_ptrStackPos.x) ^ AsInt(position.x);
            differingBits |= AsInt(_ptrStackPos.y) ^ AsInt(position.y);
            differingBits |= AsInt(_ptrStackPos.z) ^ AsInt(position.z);
            var firstSet = 23 - FirstSetHigh(differingBits);
            var stepDepth = Math.Min(Math.Min(firstSet - 1, _ptrStackDepth), depth);
            
            var ptr = _ptrStack[stepDepth];
            var type = (_data[ptr] >> 31) & 1; // Type of root node
            // Step down one depth until a non-ptr node is hit or the desired depth is reached.
            while(type == 0 && stepDepth < depth)
            {
                // Descend to the next branch
                ptr = _data[ptr]; 
                
                // Step to next node
                stepDepth++;
                var xm = (AsInt(position.x) >> (23 - stepDepth)) & 1;
                var ym = (AsInt(position.y) >> (23 - stepDepth)) & 1;
                var zm = (AsInt(position.z) >> (23 - stepDepth)) & 1;
                var childIndex = (xm << 2) + (ym << 1) + zm;
                ptr += childIndex;
                _ptrStack[stepDepth] = ptr;
                
                // Get type of the node
                type = (_data[ptr] >> 31) & 1;
            }

            // The new voxels position or desired depth has now been reached
            // delete old voxel data
            var original = _data[ptr];
            int[] originalShadingData;
            if (type == 0)
            {
                FreeBranch(original);
            }
            if (type == 1 && original != 1 << 31)
            {
                // Get the attributes data
                var attribPtr = original & 0x7FFFFFFF;
                var size = (_data[attribPtr] >> 24) & 0xFF;
                originalShadingData = new int[size];
                for (var i = 0; i < size; i++)
                    originalShadingData[i] = _data[attribPtr + i];
                FreeAttributes(attribPtr);
            }
            else originalShadingData = null;

            while (stepDepth < depth)
            {
                stepDepth++;
                
                // Calculate index of next node
                var xm = (AsInt(position.x) >> (23 - stepDepth)) & 1;
                var ym = (AsInt(position.y) >> (23 - stepDepth)) & 1;
                var zm = (AsInt(position.z) >> (23 - stepDepth)) & 1;
                var childIndex = (xm << 2) + (ym << 1) + zm;
                
                // Create another branch to go down another depth
                // The last hit voxel MUST be type 1 (Voxel). Otherwise stepDepth == depth.
                var defaultData = new int[8];
                for (var i = 0; i < 8; i++)
                    if (i == childIndex)
                        defaultData[i] = 1 << 31; // Placeholder
                    else
                        defaultData[i] = (1 << 31) | AllocateAttributeData(originalShadingData);
                var branchPtr = AllocateBranch(defaultData);
                _data[ptr] = branchPtr;
                RecordUpdate(ptr);
                ptr = branchPtr + childIndex;
                _ptrStack[stepDepth] = ptr;
            }
            _data[ptr] = (1 << 31) | AllocateAttributeData(internalAttributes);
            RecordUpdate(ptr);
            
            _ptrStackDepth = stepDepth;
            _ptrStackPos = position;
        }

        public void FillTriangle(Vector3[] vertices, int depth, Func<Bounds, Tuple<Color, int[]>> attributeGenerator)
        {
            void FillRecursively(int currentDepth, Bounds bounds)
            {
                if (TriBoxOverlap.IsIntersecting(bounds, vertices))
                {
                    if (depth == currentDepth)
                    {
                        var (color, attributes) = attributeGenerator(bounds);
                        SetVoxel(bounds.min, depth, color, attributes);
                    }
                    else for (var i = 0; i < 8; i++) // Call recursively for children.
                    {
                        var nextCenter = 0.5f * bounds.extents + bounds.min;
                        if ((i & 4) > 0) nextCenter.x += bounds.extents.x;
                        if ((i & 2) > 0) nextCenter.y += bounds.extents.y;
                        if ((i & 1) > 0) nextCenter.z += bounds.extents.z;
                        FillRecursively(currentDepth + 1, new Bounds(nextCenter, bounds.extents));
                    }
                }
            }
            
            FillRecursively(0, new Bounds(Vector3.zero, Vector3.one));
        }

        public bool CastRay(
            Ray world_ray, 
            Transform octreeTransform, 
            out RayHit hit)
        {
            hit = new RayHit();
            
            unsafe int AsInt(float f) => *(int*)&f;
            unsafe float AsFloat(int value) => *(float*)&value;
            int FirstSetHigh(int value) => (AsInt(value) >> 23) - 127;
            int GetType(int value) => (value >> 31) & 1;
            Vector3 VecMul(Vector3 a, Vector3 b) => new Vector3(a.x * b.x, a.y * b.y, a.z * b.z);
            Vector3 VecClamp(Vector3 a, float min, float max) => 
                new Vector3(Mathf.Clamp(a.x, min, max), 
                    Mathf.Clamp(a.y, min, max), 
                    Mathf.Clamp(a.z, min, max));
            
            var ray_dir = (Vector3)(octreeTransform.worldToLocalMatrix * new Vector4(world_ray.direction.x, world_ray.direction.y, world_ray.direction.z, 0));
            var ray_origin = (Vector3)(octreeTransform.worldToLocalMatrix * new Vector4(world_ray.origin.x, world_ray.origin.y, world_ray.origin.z, 1));
            // Calculations assume octree voxels are in [1, 2) but object is in [-.5, .5]. This corrects that.
            ray_origin += new Vector3(1.5f, 1.5f, 1.5f);
             
            const int max_depth = 23;
            const float epsilon = 0.00000011920928955078125f;
            // Mirror coordinate system such that all ray direction components are negative.
            int sign_mask = 0;
            if(ray_dir.x > 0f)
            {
                sign_mask ^= 4; 
                ray_origin.x = 3f - ray_origin.x;
            }
            if(ray_dir.y > 0f)
            {
                sign_mask ^= 2; 
                ray_origin.y = 3f - ray_origin.y;
            }
            if(ray_dir.z > 0f)
            {
                sign_mask ^= 1; 
                ray_origin.z = 3f - ray_origin.z;
            }

            ray_dir = -new Vector3(Mathf.Abs(ray_dir.x), Mathf.Abs(ray_dir.y), Mathf.Abs(ray_dir.z));
            var ray_inv_dir = -new Vector3(Mathf.Abs(1 / ray_dir.x), Mathf.Abs(1 / ray_dir.y), Mathf.Abs(1 / ray_dir.z));
            
            // Get intersections of octree (if hit)
            var root_min_distances = VecMul(Vector3.one * 2f - ray_origin, ray_inv_dir);
            var root_max_distances = VecMul(Vector3.one - ray_origin, ray_inv_dir);
            var root_tmin = Mathf.Max(Mathf.Max(Mathf.Max(root_min_distances.x, root_min_distances.y), root_min_distances.z), 0);
            var root_tmax = Mathf.Min(Mathf.Min(root_max_distances.x, root_max_distances.y), root_max_distances.z);
            
            if(root_tmax < 0 || root_tmin >= root_tmax) return false;
            if(root_tmin == root_min_distances.x)
            {
                hit.faceNormal = new Vector3(1, 0, 0);
                if((sign_mask >> 2) != 0)
                    hit.faceNormal.x = -hit.faceNormal.x;
            }
            else if(root_tmin == root_min_distances.y)
            {
                hit.faceNormal = new Vector3(0, 1, 0);
                if((sign_mask >> 1 & 1) != 0)
                    hit.faceNormal.y = -hit.faceNormal.y;
            }
            else
            {
                hit.faceNormal = new Vector3(0, 0, 1);
                if((sign_mask & 1) != 0)
                    hit.faceNormal.z = -hit.faceNormal.z;
            }
            
            Vector3 next_path = VecClamp(ray_origin + ray_dir * root_tmin, 1f, AsFloat(0x3fffffff));
            
            var stack = new int[max_depth + 1];
            stack[0] = 0;
            var stack_depth = 0;
            Vector3 stack_path = new Vector3(1, 1, 1);

            int i = 0;
            do
            {
                i++;
                // Get voxel at targetPos
                var differing_bits = AsInt(stack_path.x) ^ AsInt(next_path.x);
                differing_bits |= AsInt(stack_path.y) ^ AsInt(next_path.y);
                differing_bits |= AsInt(stack_path.z) ^ AsInt(next_path.z);
                var first_set = 23 - FirstSetHigh(differing_bits);
                var depth = Mathf.Min(first_set - 1, stack_depth);
                var ptr = stack[depth];
                int data = _data[ptr];
                int type = GetType(data);
                while(type == 0)
                {
                    ptr = data;
                    depth++;
                    int xm = (AsInt(next_path.x) >> (23 - depth)) & 1; // 1 or 0 for sign of movement in x direction
                    int ym = (AsInt(next_path.y) >> (23 - depth)) & 1; // 1 or 0 for sign of movement in y direction
                    int zm = (AsInt(next_path.z) >> (23 - depth)) & 1; // 1 or 0 for sign of movement in z direction
                    int child_index = (xm << 2) + (ym << 1) + zm;
                    child_index ^= sign_mask;
                    ptr += child_index;
                    stack[depth] = ptr;
                    data = _data[ptr]; // Follow ptr
                    type = GetType(data);
                }
                stack_depth = depth;
                stack_path = new Vector3(
                    AsFloat(AsInt(next_path.x) & ~((1 << 23 - depth) - 1)),
                    AsFloat(AsInt(next_path.y) & ~((1 << 23 - depth) - 1)),
                    AsFloat(AsInt(next_path.z) & ~((1 << 23 - depth) - 1))
                ); // Remove unused bits
                
                // Return hit if voxel is solid
                if(type == 1 && data != (1 << 31))
                {
                    int attributes_head_ptr = data & ~(1 << 31);

                    int color_data = _data[attributes_head_ptr];
                    hit.attributesPtr = attributes_head_ptr + 1;
                    hit.color = new Vector4((color_data >> 16 & 0xFF) / 255f, (color_data >> 8 & 0xFF) / 255f, (color_data & 0xFF) / 255f, (color_data >> 24 & 0xFF) / 255f);

                    // Undo coordinate mirroring in next_path
                    Vector3 mirrored_path = next_path;
                    hit.voxelObjSize = AsFloat((0b01111111 - depth) << 23); // exp2(-depth)
                    if(sign_mask >> 2 != 0)
                    {
                        hit.faceNormal.x = -hit.faceNormal.x;
                        mirrored_path.x = 3f - next_path.x;
                    }
                    if((sign_mask >> 1 & 1) != 0)
                    {
                        hit.faceNormal.y = -hit.faceNormal.y;
                        mirrored_path.y = 3f - next_path.y;
                    }
                    if((sign_mask & 1) != 0)
                    {
                        hit.faceNormal.z = -hit.faceNormal.z;
                        mirrored_path.z = 3f - next_path.z;
                    }
                    hit.voxelObjPos -= Vector3.one * 1.5f;
                    hit.objPos = mirrored_path - Vector3.one * 1.5f;
                    hit.voxelObjPos = new Vector3(
                        AsFloat(AsInt(mirrored_path.x) & ~((1 << 23 - depth) - 1)) - 1.5f,
                        AsFloat(AsInt(mirrored_path.y) & ~((1 << 23 - depth) - 1)) - 1.5f,
                        AsFloat(AsInt(mirrored_path.z) & ~((1 << 23 - depth) - 1)) - 1.5f
                        );
                    hit.worldPos = octreeTransform.localToWorldMatrix * new Vector4(hit.objPos.x, hit.objPos.y, hit.objPos.z, 1f);
                    
                    return true;
                }

                // Step to the next voxel by moving along the normal on the far side of the voxel that was hit.
                var t_max = VecMul((stack_path - ray_origin), ray_inv_dir);
                var min_t_max = Mathf.Min(Mathf.Min(t_max.x, t_max.y), t_max.z);
                var cmax = new Vector3(
                    AsFloat(AsInt(stack_path.x) + (1 << 23 - depth) - 1),
                    AsFloat(AsInt(stack_path.y) + (1 << 23 - depth) - 1),
                    AsFloat(AsInt(stack_path.z) + (1 << 23 - depth) - 1)
                );
                next_path = new Vector3(
                    Mathf.Clamp(ray_origin.x + ray_dir.x * min_t_max, stack_path.x, cmax.x),
                    Mathf.Clamp(ray_origin.y + ray_dir.y * min_t_max, stack_path.y, cmax.y),
                    Mathf.Clamp(ray_origin.z + ray_dir.z * min_t_max, stack_path.z, cmax.z)
                );

                if(t_max.x == min_t_max)
                {
                    hit.faceNormal = new Vector3(1, 0, 0);
                    next_path.x = stack_path.x - epsilon;
                }
                else if(t_max.y == min_t_max)
                {
                    hit.faceNormal = new Vector3(0, 1, 0);
                    next_path.y = stack_path.y - epsilon;
                }
                else
                {
                    hit.faceNormal = new Vector3(0, 0, 1);
                    next_path.z = stack_path.z - epsilon;
                }
            }
            while((AsInt(next_path.x) & 0xFF800000) == 0x3f800000 && 
                  (AsInt(next_path.y) & 0xFF800000) == 0x3f800000 && 
                  (AsInt(next_path.z) & 0xFF800000) == 0x3f800000 && 
                  i <= 250); // Same as 1 <= next_path < 2 && i <= 250

            return false;
        }

        private void FreeBranch(int ptr)
        {
            _freeStructureMemory.Add(ptr);
            for (var i = 0; i < 8; i++)
            {
                var optr = ptr + i;
                var type = (_data[optr] >> 31) & 1;
                if(type == 0)
                    FreeBranch(_data[optr]);
                else if(_data[optr] != 1 << 31)
                    FreeAttributes(_data[optr] & 0x7FFFFFFF);
            }
        }

        private void FreeAttributes(int ptr)
        {
            _freeAttributeMemory.Add(ptr);
        }
        
        private int AllocateBranch(IReadOnlyList<int> ptrs)
        {
            int ptr;
            if (_freeStructureMemory.Count == 0)
            {
                ptr = _data.Count;
                _data.AddRange(ptrs);
            }
            else
            {
                ptr = _freeStructureMemory.Last();
                for (var i = 0; i < ptrs.Count; i++)
                    _data[i + ptr] = ptrs[i];
                _freeStructureMemory.Remove(ptr);
            }
            // Only need to record update twice because branch can only be in 2 slices at most
            RecordUpdate(ptr);
            RecordUpdate(ptr + 7);
            return ptr;
        }

        private int AllocateAttributeData(IReadOnlyList<int> attributes)
        {
            if (attributes == null) return 0;
            var index = 0;
            foreach (var ptr in _freeAttributeMemory)
            {
                var size = (uint)_data[ptr] >> 24;
                if (size != attributes.Count)
                {
                    index++;
                    continue;
                }
                
                for (var i = 0; i < attributes.Count; i++)
                {
                    _data[ptr + i] = attributes[i];
                }
                if (attributes.Count > 256 * 256) throw new ArgumentException("Too many attributes. Max number is 65536 per voxel.");
                // Assume attributes.Count is less than the size of one slice.
                RecordUpdate(ptr);
                RecordUpdate(ptr + attributes.Count - 1);
                _freeAttributeMemory.Remove(ptr);
                return ptr;
            }

            var endPtr = _data.Count;
            _data.AddRange(attributes);
            RecordUpdate(endPtr);
            RecordUpdate(endPtr + attributes.Count - 1);
            return endPtr;
        }

        /// <summary>
        /// Creates a texture to contain this octree.
        /// </summary>
        /// <param name="tryReuseOldTexture">Attempt to reuse the previous texture. This can be faster, but the old texture will no longer be usable.</param>
        /// <returns>A new texture containing the updated Octree.</returns>
        public Texture3D Apply(bool tryReuseOldTexture=true)
        {
            if (tempTex is null)
                tempTex = new Texture3D(256, 256, 1, TextureFormat.RFloat, false);
            
            var depth = Mathf.NextPowerOfTwo(Mathf.CeilToInt((float) _data.Count / 256 / 256));
            if (Data is null || depth != Data.depth || !tryReuseOldTexture)
            {
                Object.Destroy(Data);
                Data = new Texture3D(256, 256, depth, TextureFormat.RFloat, false);
                for (int i = 0; i < _lastApply.Length; i++)
                    _lastApply[i] = ulong.MaxValue;
            }

            uint updated = 0;
            for (var i = 0; i < depth; i++)
            {
                if (_lastApply[i] == _updateCount[i])
                    continue;

                updated++;
                _lastApply[i] = _updateCount[i];
                
                var minIndex = i * 256 * 256;
                var maxIndex = (i + 1) * 256 * 256;
                if (minIndex > _data.Count) minIndex = _data.Count;
                if (maxIndex > _data.Count) maxIndex = _data.Count;
                
                if (minIndex >= maxIndex) break;
                var block = new int[256 * 256];
                _data.CopyTo(minIndex, block, 0, maxIndex - minIndex);
                tempTex.SetPixelData(block, 0);
                tempTex.Apply();
                Graphics.CopyTexture(tempTex, 0, 0, 0, 0, 256, 256, Data, i, 0, 0, 0);
                
            }

            if (updated != 0)
            {
                Data.IncrementUpdateCount();
            }
            return Data;
        }

        /// <summary>
        /// Rebuilds the internal structure of the octree. This makes the octree memory continuous, lowering memory
        /// overhead and potentially increasing performance.
        /// </summary>
        public void Rebuild()
        {
            var capacity = Mathf.NextPowerOfTwo(_data.Count - _freeStructureMemory.Count * 8 - _freeAttributeMemory.Count);
            var optimizedData = new List<int>(capacity);

            void RebuildBranch(int referenceBranchPtr)
            {
                var start = optimizedData.Count;
                optimizedData.AddRange(new int[8]);
                for (var i = 0; i < 8; i++)
                {
                    if (_data[referenceBranchPtr + i] == 1 << 31)
                    {
                        optimizedData[start + i] = 1 << 31;
                    }
                    else if ((_data[referenceBranchPtr + i] >> 31 & 1) == 1)
                    {
                        optimizedData[start + i] = 1 << 31 | optimizedData.Count;
                        var attribPtr = _data[referenceBranchPtr + i] & 0x7FFFFFFF;
                        var c = (_data[attribPtr] >> 24) & 0xFF;
                        for(var j = 0; j < c; j++)
                            optimizedData.Add(_data[attribPtr + j]);
                    }
                    else
                    {
                        optimizedData[start + i] = optimizedData.Count;
                        RebuildBranch(_data[referenceBranchPtr + i]);
                    }
                }   
            }

            if (_data[0] == 1 << 31)
            {
                optimizedData.Add(1 << 31);
            }
            else if ((_data[0] >> 31 & 1) == 1)
            {
                optimizedData.Add(1 << 31 | (optimizedData.Count + 1));
                var attribPtr = _data[0] & 0x7FFFFFFF;
                var c = (_data[attribPtr] >> 24) & 0xFF;
                for(var j = 0; j < c; j++)
                    optimizedData.Add(_data[attribPtr + j]);
            }
            else
            {
                optimizedData.Add(optimizedData.Count + 1);
                RebuildBranch(_data[0]);
            }

            _data = optimizedData;
            _ptrStackDepth = 0;
            _ptrStackPos = Vector3.one;
            _ptrStack = new int[24];
            _freeAttributeMemory.Clear();
            _freeStructureMemory.Clear();
            _lastApply = new ulong[2048];
            for (var i = 0; i < _lastApply.Length; i++)
                _lastApply[i] = ulong.MaxValue;
        }

        private void RecordUpdate(int idx)
        {
            _updateCount[idx >> 16]++;
        }

        public void Dispose()
        {
            Object.Destroy(Data);
        }
    }
}