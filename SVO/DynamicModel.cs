using System;
using System.Collections.Generic;
using System.Text;
using UnityEngine;
using Random = UnityEngine.Random;

namespace SVO
{
    /**
     * Model that can be changed at runtime. Good for voxel, minecraft-like terrain.
     * Its recommended that these models keep a depth of around ~5, occasionally going up to about 8.
     * Dynamic models have to store a copy of their data on the CPU, and must be sent to the GPU when a change is
     * made.
     */
    public class DynamicModel: Model
    {
        private ComputeBuffer _computeBuffer;
        private int _rootPtr = 0;
        private List<int> _bufferData = new List<int>(new[] {2 << 30});
        private bool _shouldUpdateBuffer;

        protected override void Start()
        {
            _computeBuffer = new ComputeBuffer(_bufferData.Count, 4);
            _computeBuffer.SetData(_bufferData.ToArray());
            base.Start();

            const int depth = 7;
            float step = 1/Mathf.Pow(2f, depth);
            var position = transform.position;
            var scale = transform.lossyScale;
            for (var x = 1f; x < 2f; x += step)
            {
                for (var z = 1f; z < 2f; z += step)
                {
                    var height = Mathf.PerlinNoise(position.x / scale.x + x + 100f, position.z / scale.z + z + 100f) + 1;
                    for (var y = 1f; y + step < height; y += step)
                    {
                        SetVoxelColor(new Vector3(x, y, z), depth, new Color(x - 1, y - 1, z - 1));
                    }
                }
            }
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

            var rgb = ((int) (color.r * 255) << 16) + ((int) (color.g * 255) << 8) + (int) (color.b * 255);
            var original = _bufferData[ptr];
            while (stepDepth < depth)
            {
                stepDepth++;
                // Create another branch to go down another depth
                _bufferData[ptr] = _bufferData.Count;
                ptr = _bufferData.Count;
                int xm = (BitConverter.ToInt32(BitConverter.GetBytes(position.x), 0) >> (23 - stepDepth)) & 1;
                int ym = (BitConverter.ToInt32(BitConverter.GetBytes(position.y), 0) >> (23 - stepDepth)) & 1;
                int zm = (BitConverter.ToInt32(BitConverter.GetBytes(position.z), 0) >> (23 - stepDepth)) & 1;
                int childIndex = (xm << 2) + (ym << 1) + zm;
                ptr += childIndex;
                // Create a new branch full of the previous voxel.
                _bufferData.AddRange(new [] { original, original, original, original, original, original, original, original });
            }
            _bufferData[ptr] = (1 << 30) | rgb;
            
            _shouldUpdateBuffer = true;
        }

        internal override void Render(ComputeShader shader, Camera camera, RenderTexture colorTexture, RenderTexture depthMask)
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
            
            int threadGroupsX = Mathf.CeilToInt(Screen.width / 16.0f);
            int threadGroupsY = Mathf.CeilToInt(Screen.height / 16.0f);
            
            // Update Parameters
            shader.SetBuffer(0, "octree_root", _computeBuffer);
            shader.SetTexture(0, "result_texture", colorTexture);
            shader.SetTexture(0, "depth_mask", depthMask);
            shader.SetMatrix("camera_to_world", camera.cameraToWorldMatrix);
            shader.SetMatrix("camera_inverse_projection", camera.projectionMatrix.inverse);
            shader.SetVector("octree_pos", transform.position);
            shader.SetVector("octree_scale", transform.lossyScale);
            shader.SetInt("root_ptr", _rootPtr); 
            
            shader.Dispatch(0, threadGroupsX, threadGroupsY, 1);
        }

        private void OnDestroy()
        {
            _computeBuffer.Release();
            _bufferData = null;
        }
        
        // For debugging
        private String VisualizeBufferData()
        {
            var str = new StringBuilder();

            void AppendBranch(int ptr, int indentSize)
            {
                var v = _bufferData[ptr];

                for (int i = 0; i < indentSize; i++)
                    str.Append('\t');
                str.Append("BRANCH ");
                str.Append(v);
                str.AppendLine();

                int j = 1;
                for (int i = 0; i < 8; i++)
                {
                    uint type = (uint)_bufferData[ptr + j] >> 30;
                    if (type == 0)
                    {
                        AppendBranch(ptr + j, indentSize + 1);
                        j += _bufferData[ptr + j];
                    }
                    else
                    {
                        for (int k = 0; k < indentSize + 1; k++)
                            str.Append('\t');
                        if (type == 2)
                        {
                            str.Append("EMPTY");
                        }
                        else
                        {
                            str.Append($"COLORED RGB({_bufferData[ptr + j] << 16 & 0xFF}, {_bufferData[ptr + j] << 8 & 0xFF}, {_bufferData[ptr + j] & 0xFF})");
                        }
                        str.AppendLine();
                        j++;
                    }
                }
            }
            
            AppendBranch(0, 0);
            
            return str.ToString();
        }

    }
}