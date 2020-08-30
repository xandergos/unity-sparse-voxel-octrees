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
        private ComputeBuffer _structureBuffer;
        private ComputeBuffer _shadingBuffer;
        
        private static readonly int OctreePrimaryData = Shader.PropertyToID("octree_primary_data");
        private static readonly int OctreeAttribData = Shader.PropertyToID("octree_attrib_data");
        private static readonly int Initialized = Shader.PropertyToID("initialized");
        
        public void Awake()
        {
            _structureBuffer = new ComputeBuffer(1, 4);
            _structureBuffer.SetData(new[] { 1 << 31 });
            _shadingBuffer = new ComputeBuffer(1, 4);
            _shadingBuffer.SetData(new[] { 0 });
        }

        public void SetData(OctreeData data)
        {
            if (_structureBuffer.count != data.StructureData.Count)
            {
                _structureBuffer.Release();
                _structureBuffer = new ComputeBuffer(data.StructureData.Count, 4);
            }
            _structureBuffer.SetData(data.StructureData);

            if (_shadingBuffer.count != data.ShadingData.Count)
            {
                _shadingBuffer.Release();
                _shadingBuffer = new ComputeBuffer(data.ShadingData.Count, 4);
            }
            _shadingBuffer.SetData(data.ShadingData);
        }

        public OctreeData GetData()
        {
            var structureData = new int[0];
            _structureBuffer.GetData(structureData);
            var structureDataList = new List<int>(structureData);
            
            var shadingData = new int[0];
            _structureBuffer.GetData(shadingData);
            var shadingDataList = new List<int>(shadingData);
            
            return new OctreeData(structureDataList, shadingDataList);
        }

        private void OnWillRenderObject()
        {
            var material = GetComponent<Renderer>().material;
            
            // Update Parameters
            material.SetBuffer(OctreePrimaryData, _structureBuffer);
            material.SetBuffer(OctreeAttribData, _shadingBuffer);
            material.SetInt(Initialized, 1);
        }

        private void OnDestroy()
        {
            _structureBuffer.Release();
            _shadingBuffer.Release();
        }
    }
}