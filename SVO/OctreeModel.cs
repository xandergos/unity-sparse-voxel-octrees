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

        private ulong _lastUpdateIndex = ulong.MaxValue;
        
        private static readonly int OctreePrimaryData = Shader.PropertyToID("octree_primary_data");
        private static readonly int OctreeAttribData = Shader.PropertyToID("octree_attrib_data");
        private static readonly int Initialized = Shader.PropertyToID("initialized");

        private void Update()
        {
            if (data is null) return;
            if (_lastUpdateIndex != data.UpdateIndex)
            {
                _lastUpdateIndex = data.UpdateIndex;
                ReloadBuffers();
            }
        }

        private void ReloadBuffers()
        {
            if (data is null) return;
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
            if (_structureBuffer is null || _shadingBuffer is null) return;
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
    }
}