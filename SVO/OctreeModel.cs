using System;
using System.Collections.Generic;
using UnityEngine;

namespace SVO
{
    [ExecuteAlways]
    public class OctreeModel: MonoBehaviour
    {
        public OctreeData data;
        private ComputeBuffer _structureBuffer;
        private ComputeBuffer _shadingBuffer;

        private float _lastUpdate = -1f;
        private bool _buffersCleared;
        
        private static readonly int OctreePrimaryData = Shader.PropertyToID("octree_primary_data");
        private static readonly int OctreeAttribData = Shader.PropertyToID("octree_attrib_data");
        private static readonly int Initialized = Shader.PropertyToID("initialized");

        private void Awake()
        {
            ResetBuffers();
        }

        private void Update()
        {
            if(_structureBuffer == null || _shadingBuffer == null) ResetBuffers();
            if (data == null)
            {
                _lastUpdate = -1f;
                // Reset buffers if not already reset
                if (!_buffersCleared) ResetBuffers();
            }
            else if (_lastUpdate != data.LastUpdate)
            {
                _lastUpdate = data.LastUpdate;
                ReloadBuffers();
            }
        }

        private void ReloadBuffers()
        {
            _buffersCleared = false;
            if (_structureBuffer?.count != data.structureData.Count)
            {
                _structureBuffer?.Release();
                _structureBuffer = new ComputeBuffer(data.structureData.Count, 4);
            }
            _structureBuffer.SetData(data.structureData);

            if (_shadingBuffer?.count != data.attributeData.Count)
            {
                _shadingBuffer?.Release();
                _shadingBuffer = new ComputeBuffer(data.attributeData.Count, 4);
            }
            _shadingBuffer.SetData(data.attributeData);
        }

        private void OnWillRenderObject()
        {
            var material = GetComponent<Renderer>().sharedMaterial;
            
            // Update Parameters
            material.SetBuffer(OctreePrimaryData, _structureBuffer);
            material.SetBuffer(OctreeAttribData, _shadingBuffer);
            material.SetInt(Initialized, 1);
        }

        private void OnDestroy()
        {
            _structureBuffer?.Release();
            _shadingBuffer?.Release();
        }

        private void ResetBuffers()
        {
            _buffersCleared = true;
            _structureBuffer?.Release();
            _shadingBuffer?.Release();
            _structureBuffer = new ComputeBuffer(1, 4);
            _shadingBuffer = new ComputeBuffer(1, 4);
            _structureBuffer.SetData(new[] { 1 << 31 });
            _shadingBuffer.SetData(new[] { 0 });
        }
    }
}