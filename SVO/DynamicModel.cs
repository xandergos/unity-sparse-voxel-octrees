using System;
using System.Collections.Generic;
using UnityEngine;

namespace SVO
{
    /**
     * Model that can be changed at runtime. Good for voxel, minecraft-like terrain.
     * Its recommended that these models generally stay at a depth of about 7 or less, although at times can go up as high as 10.
     * Dynamic models have to store a copy of their data on the CPU, and must be sent to the GPU when a change is
     * made.
     */
    public class DynamicModel: MonoBehaviour
    {
        private ComputeBuffer _primaryBuffer;
        private ComputeBuffer _attribBuffer;
        private readonly List<int> _primaryBufferData = new List<int>(new[] {2 << 30});
        private readonly List<int> _attribBufferData = new List<int>(new[] {2 << 30});
        private bool _shouldUpdateBuffer;
        private readonly Queue<int> _freeMemoryPointers = new Queue<int>();
        
        private readonly int[] _ptrStack = new int[24];
        private Vector3 _ptrStackPos = Vector3.zero;
        private int _ptrStackDepth;
        private static readonly int OctreePrimaryData = Shader.PropertyToID("octree_primary_data");
        private static readonly int OctreeAttribData = Shader.PropertyToID("octree_attrib_data");
        private static readonly int Initialized = Shader.PropertyToID("initialized");

        private void Awake()
        {
            _primaryBuffer = new ComputeBuffer(_primaryBufferData.Count, 4);
            _primaryBuffer.SetData(_primaryBufferData.ToArray());
            _attribBuffer = new ComputeBuffer(_attribBufferData.Count, 4);
            _attribBuffer.SetData(_attribBufferData.ToArray());
        }

        private int GetVoxel(Vector3 pos)
        {
            int ptr = 0; // Root ptr
            int type = (_primaryBufferData[ptr] >> 30) & 3; // Type of root node
            int stepDepth = 0;
            // Step down one depth until a non-ptr node is hit or the max depth is reached.
            while(type == 0)
            {
                // Descend to the next branch
                ptr = _primaryBufferData[ptr];
                
                // Step to next node
                stepDepth++;
                int xm = (BitConverter.ToInt32(BitConverter.GetBytes(pos.x), 0) >> (23 - stepDepth)) & 1;
                int ym = (BitConverter.ToInt32(BitConverter.GetBytes(pos.y), 0) >> (23 - stepDepth)) & 1;
                int zm = (BitConverter.ToInt32(BitConverter.GetBytes(pos.z), 0) >> (23 - stepDepth)) & 1;
                int childIndex = (xm << 2) + (ym << 1) + zm;
                ptr += childIndex;
                
                // Get type of the node
                type = (_primaryBufferData[ptr] >> 30) & 3;
            }
            return _primaryBufferData[ptr];
        }

		/**
		Sets the color (and normal) at the specified position and depth.
		position: The position of the voxel in the Octrees local space. This can be represented as a cube with center 1.5 and size 1. The locations are odd for performance purposes.
		depth: The depth of the voxel. A depth of 1 would mean the voxel's size is 1/2, a depth of 3 would mean a size of 1/8th. In general, the size of a voxel with depth n has size 2^(-n). Minimum depth is 1.
		color: The color of the voxel.
		normal: The voxels normal.
		*/
        public void SetVoxelColor(Vector3 position, int depth, Color color, Vector3 normal)
        {
            var primaryData = 1 << 30;
            primaryData |= ((int) (color.r * 255) << 16) | ((int) (color.g * 255) << 8) | (int) (color.b * 255);

            var attribData = 0;
            var maxAbsComp = Mathf.Max(Mathf.Max(Mathf.Abs(normal.x), Mathf.Abs(normal.y)), Mathf.Abs(normal.z));
            var cubicNormal = normal / maxAbsComp;
            var cubicNormalUnorm = cubicNormal / 2f + new Vector3(.5f, .5f, .5f);
            if (Mathf.Abs(normal.x) == maxAbsComp)
            {
                attribData |= ((Math.Sign(normal.x) + 1) / 2) << 22;
                attribData |= (int)(1023f * cubicNormalUnorm.y) << 10;
                attribData |= (int)(1023f * cubicNormalUnorm.z);
            }
            else if (Mathf.Abs(normal.y) == maxAbsComp)
            {
                attribData |= ((Math.Sign(normal.y) + 1) / 2) << 22;
                attribData |= 1 << 20;
                attribData |= (int)(1023f * cubicNormalUnorm.x) << 10;
                attribData |= (int)(1023f * cubicNormalUnorm.z);
            }
            else if (Mathf.Abs(normal.z) == maxAbsComp)
            {
                attribData |= ((Math.Sign(normal.z) + 1) / 2) << 22;
                attribData |= 2 << 20;
                attribData |= (int)(1023f * cubicNormalUnorm.x) << 10;
                attribData |= (int)(1023f * cubicNormalUnorm.y);
            }
            
            SetVoxel(position, depth, primaryData, attribData);
        }

        public void DeleteVoxels(Vector3 position, int depth) => SetVoxel(position, depth, 2 << 30, 0);

        private void SetVoxel(Vector3 position, int depth, int data, int attribData)
        {
            static int AsInt(float f) => BitConverter.ToInt32(BitConverter.GetBytes(f), 0);
            static int FirstSetHigh(int i) => (AsInt(i) >> 23) - 127;
            
            // This can skip a lot of tree iterations if the last voxel set was near this one.
            int differingBits = AsInt(_ptrStackPos.x) ^ AsInt(position.x);
            differingBits |= AsInt(_ptrStackPos.y) ^ AsInt(position.y);
            differingBits |= AsInt(_ptrStackPos.z) ^ AsInt(position.z);
            differingBits &= 0x007fffff;
            int firstSet = 23 - FirstSetHigh(differingBits);
            int stepDepth = Math.Min(Math.Min(firstSet - 1, _ptrStackDepth), depth);
            
            int ptr = _ptrStack[stepDepth];
            int type = (_primaryBufferData[ptr] >> 30) & 3; // Type of root node
            // Step down one depth until a non-ptr node is hit or the max depth is reached.
            while(type == 0 && stepDepth < depth)
            {
                // Descend to the next branch
                ptr = _primaryBufferData[ptr];
                
                // Step to next node
                stepDepth++;
                int xm = (BitConverter.ToInt32(BitConverter.GetBytes(position.x), 0) >> (23 - stepDepth)) & 1;
                int ym = (BitConverter.ToInt32(BitConverter.GetBytes(position.y), 0) >> (23 - stepDepth)) & 1;
                int zm = (BitConverter.ToInt32(BitConverter.GetBytes(position.z), 0) >> (23 - stepDepth)) & 1;
                int childIndex = (xm << 2) + (ym << 1) + zm;
                ptr += childIndex;
                _ptrStack[stepDepth] = ptr;
                
                // Get type of the node
                type = (_primaryBufferData[ptr] >> 30) & 3;
            }
            _ptrStackDepth = stepDepth;
            _ptrStackPos = position;

            // Data can be compressed
            if (type == 0)
                FreeMemory(_primaryBufferData[ptr]);

            var original = _primaryBufferData[ptr];
            var originalAttrib = _attribBufferData[ptr];
            while (stepDepth < depth)
            {
                stepDepth++;
                // Create another branch to go down another depth
                _primaryBufferData[ptr] = AllocateBranch(original, originalAttrib);
                ptr = _primaryBufferData[ptr];
                
                // Move to the position of the right child node.
                int xm = (BitConverter.ToInt32(BitConverter.GetBytes(position.x), 0) >> (23 - stepDepth)) & 1;
                int ym = (BitConverter.ToInt32(BitConverter.GetBytes(position.y), 0) >> (23 - stepDepth)) & 1;
                int zm = (BitConverter.ToInt32(BitConverter.GetBytes(position.z), 0) >> (23 - stepDepth)) & 1;
                int childIndex = (xm << 2) + (ym << 1) + zm;
                ptr += childIndex;
            }
            _primaryBufferData[ptr] = data;
            _attribBufferData[ptr] = attribData;
            
            _shouldUpdateBuffer = true;
        }

        public void Fill(Predicate<Bounds> branchCondition, Predicate<Bounds> voxelCondition, Color color)
        {
            var primaryData = 1 << 30;
            primaryData |= ((int) (color.r * 255) << 16) | ((int) (color.g * 255) << 8) | (int) (color.b * 255);
            
            Vector3 size = Vector3.one;
            Vector3 pos = Vector3.one;
            if (((_primaryBufferData[0] >> 30) & 3) == 0)
            {
                if (branchCondition(new Bounds(pos + size / 2, size)))
                {
                    Fill(pos, 1, _primaryBufferData[0], branchCondition, voxelCondition, primaryData);
                }
            }
            else if (((_primaryBufferData[0] >> 30) & 3) == 2 && voxelCondition.Invoke( new Bounds(pos + size / 2, size)))
            {
                _primaryBufferData[0] = primaryData;
            }
        }

        private void Fill(Vector3 startPos, int depth, int startPtr, Predicate<Bounds> branchCondition, Predicate<Bounds> voxelCondition, int data)
        {
            static int AsInt(float f) => BitConverter.ToInt32(BitConverter.GetBytes(f), 0);
            static float AsFloat(int i) => BitConverter.ToSingle(BitConverter.GetBytes(i), 0);
            
            Vector3 size = Vector3.one * (AsFloat(0x3f800000 + (1 << (23 - depth))) - 1f);
            for (int i = 0; i < 8; i++)
            {
                Vector3 pos = startPos;
                pos.x = AsFloat(AsInt(pos.x) | (((i >> 2) & 1) << (23 - depth)));
                pos.y = AsFloat(AsInt(pos.y) | (((i >> 1) & 1) << (23 - depth)));
                pos.z = AsFloat(AsInt(pos.z) | ((i & 1) << (23 - depth)));
                if (((_primaryBufferData[startPtr + i] >> 30) & 3) == 0)
                {
                    if (branchCondition( new Bounds(pos + size / 2, size)))
                    {
                        Fill(pos, depth + 1, _primaryBufferData[startPtr + i], branchCondition, voxelCondition, data);
                    }
                }
                else if (((_primaryBufferData[startPtr + i] >> 30) & 3) == 2 && voxelCondition.Invoke( new Bounds(pos + size / 2, size)))
                {
                    _primaryBufferData[startPtr + i] = data;
                }
            }
        }

        private void FreeMemory(int ptr)
        {
            _freeMemoryPointers.Enqueue(ptr);
        }

        private int AllocateBranch(int defaultValue, int defaultAttribValue)
        {
            if (_freeMemoryPointers.Count != 0)
            {
                var ptr = _freeMemoryPointers.Dequeue();
                for (int i = 0; i < 8; i++)
                {
                    _primaryBufferData[ptr + i] = defaultValue;
                    _attribBufferData[ptr + i] = defaultAttribValue;
                }
                // Free child pointers
                for (int i = 0; i < 8; i++)
                {
                    var n = ptr + i;
                    if(((n >> 30) & 3) == 0) FreeMemory(n);
                }
                return ptr;
            }
            else
            {
                var ptr = _primaryBufferData.Count;
                _primaryBufferData.AddRange(new[]
                {
                    defaultValue, defaultValue, defaultValue, defaultValue,
                    defaultValue, defaultValue, defaultValue, defaultValue
                });
                _attribBufferData.AddRange(new[]
                {
                    defaultAttribValue, defaultAttribValue, defaultAttribValue, defaultAttribValue,
                    defaultAttribValue, defaultAttribValue, defaultAttribValue, defaultAttribValue
                });
                return ptr;
            }
        }
        
        private void OnWillRenderObject()
        {
            var material = GetComponent<Renderer>().material;
            if (_shouldUpdateBuffer)
            {
                _shouldUpdateBuffer = false;
                if (_primaryBufferData.Count != _primaryBuffer.count)
                {
                    _primaryBuffer.Release();
                    _primaryBuffer = new ComputeBuffer(_primaryBufferData.Count, 4);
                    _attribBuffer.Release();
                    _attribBuffer = new ComputeBuffer(_attribBufferData.Count, 4);
                }
                _primaryBuffer.SetData(_primaryBufferData);
                _attribBuffer.SetData(_attribBufferData);
            }
            
            // Update Parameters
            material.SetBuffer(OctreePrimaryData, _primaryBuffer);
            material.SetBuffer(OctreeAttribData, _attribBuffer);
            material.SetInt(Initialized, 1);
        }

        private void OnDestroy()
        {
            _primaryBuffer.Release();
            _attribBuffer.Release();
        }
    }
}