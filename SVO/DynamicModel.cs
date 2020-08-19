using System;
using System.Collections.Generic;
using System.Text;
using UnityEngine;
using Random = UnityEngine.Random;

namespace SVO
{
    /**
     * Model that can be changed at runtime. Good for voxel, minecraft-like terrain.
     * Its recommended that these models generally stay at a depth of about 7 or less, although at times can go up as high as 10.
     * Dynamic models have to store a copy of their data on the CPU, and must be sent to the GPU when a change is
     * made.
     */
    public class DynamicModel: Model
    {
        private ComputeBuffer _computeBuffer;
        private List<int> _bufferData = new List<int>(new[] {2 << 30});
        private bool _shouldUpdateBuffer;
        private readonly Queue<int> _freeMemoryPointers = new Queue<int>();

        protected override void Awake()
        {
            _computeBuffer = new ComputeBuffer(_bufferData.Count, 4);
            _computeBuffer.SetData(_bufferData.ToArray());
            base.Awake();
        }

        private int GetVoxel(Vector3 pos)
        {
            int ptr = 0; // Root ptr
            int type = (_bufferData[ptr] >> 30) & 3; // Type of root node
            int stepDepth = 0;
            // Step down one depth until a non-ptr node is hit or the max depth is reached.
            while(type == 0)
            {
                // Descend to the next branch
                ptr = _bufferData[ptr];
                
                // Step to next node
                stepDepth++;
                int xm = (BitConverter.ToInt32(BitConverter.GetBytes(pos.x), 0) >> (23 - stepDepth)) & 1;
                int ym = (BitConverter.ToInt32(BitConverter.GetBytes(pos.y), 0) >> (23 - stepDepth)) & 1;
                int zm = (BitConverter.ToInt32(BitConverter.GetBytes(pos.z), 0) >> (23 - stepDepth)) & 1;
                int childIndex = (xm << 2) + (ym << 1) + zm;
                ptr += childIndex;
                
                // Get type of the node
                type = (_bufferData[ptr] >> 30) & 3;
            }
            return _bufferData[ptr];
        }

        public void SetVoxelColor(Vector3 position, int depth, Color color) 
            => SetVoxel(position, depth, (1 << 30) | ((int)(color.r * 255) << 16) | 
                                         ((int)(color.g * 255) << 8) | (int)(color.b * 255));

        public void DeleteVoxels(Vector3 position, int depth) => SetVoxel(position, depth, 2 << 30);

        private void SetVoxel(Vector3 position, int depth, int data)
        {
            int ptr = 0; // Root ptr
            int type = (_bufferData[ptr] >> 30) & 3; // Type of root node
            int stepDepth = 0;
            // Step down one depth until a non-ptr node is hit or the max depth is reached.
            while(type == 0 && stepDepth < depth)
            {
                // Descend to the next branch
                ptr = _bufferData[ptr];
                
                // Step to next node
                stepDepth++;
                int xm = (BitConverter.ToInt32(BitConverter.GetBytes(position.x), 0) >> (23 - stepDepth)) & 1;
                int ym = (BitConverter.ToInt32(BitConverter.GetBytes(position.y), 0) >> (23 - stepDepth)) & 1;
                int zm = (BitConverter.ToInt32(BitConverter.GetBytes(position.z), 0) >> (23 - stepDepth)) & 1;
                int childIndex = (xm << 2) + (ym << 1) + zm;
                ptr += childIndex;
                
                // Get type of the node
                type = (_bufferData[ptr] >> 30) & 3;
            }

            // Data can be compressed
            if (type == 0)
                FreeMemory(_bufferData[ptr]);

            var original = _bufferData[ptr];
            while (stepDepth < depth)
            {
                stepDepth++;
                // Create another branch to go down another depth
                _bufferData[ptr] = AllocateBranch(original);
                ptr = _bufferData[ptr];
                
                // Move to the position of the right child node.
                int xm = (BitConverter.ToInt32(BitConverter.GetBytes(position.x), 0) >> (23 - stepDepth)) & 1;
                int ym = (BitConverter.ToInt32(BitConverter.GetBytes(position.y), 0) >> (23 - stepDepth)) & 1;
                int zm = (BitConverter.ToInt32(BitConverter.GetBytes(position.z), 0) >> (23 - stepDepth)) & 1;
                int childIndex = (xm << 2) + (ym << 1) + zm;
                ptr += childIndex;
            }
            _bufferData[ptr] = data;
            
            _shouldUpdateBuffer = true;
        }

        private void FreeMemory(int ptr)
        {
            _freeMemoryPointers.Enqueue(ptr);
        }

        private int AllocateBranch(int defaultValue)
        {
            if (_freeMemoryPointers.Count != 0)
            {
                var ptr = _freeMemoryPointers.Dequeue();
                for (int i = 0; i < 8; i++)
                    _bufferData[ptr + i] = defaultValue;
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
                var ptr = _bufferData.Count;
                _bufferData.AddRange(new[]
                {
                    defaultValue, defaultValue, defaultValue, defaultValue,
                    defaultValue, defaultValue, defaultValue, defaultValue
                });
                return ptr;
            }
        }
        
        internal override void Render(ComputeShader shader, Camera camera, RenderTexture diffuseTexture, RenderTexture positionTexture, RenderTexture normalTexture)
        {
            if (_shouldUpdateBuffer)
            {
                _shouldUpdateBuffer = false;
                if (_bufferData.Count != _computeBuffer.count)
                {
                    _computeBuffer.Release();
                    _computeBuffer = new ComputeBuffer(_bufferData.Count, 4);
                }
                _computeBuffer.SetData(_bufferData.ToArray());
            }
            
            var threadGroupsX = Mathf.CeilToInt(Screen.width / 16.0f);
            var threadGroupsY = Mathf.CeilToInt(Screen.height / 16.0f);
            
            // Update Parameters
            shader.SetBuffer(0, "octree_root", _computeBuffer);
            shader.SetTexture(0, "diffuse_texture", diffuseTexture);
            shader.SetTexture(0, "position_texture", positionTexture);
            shader.SetTexture(0, "normal_texture", normalTexture);
            shader.SetMatrix("camera_to_world", camera.cameraToWorldMatrix);
            shader.SetMatrix("camera_inverse_projection", camera.projectionMatrix.inverse);
            shader.SetVector("octree_pos", transform.position);
            shader.SetVector("octree_scale", transform.lossyScale);
            
            shader.Dispatch(0, threadGroupsX, threadGroupsY, 1);
        }

        private void OnDestroy()
        {
            _computeBuffer.Release();
            _bufferData = null;
        }
    }
}