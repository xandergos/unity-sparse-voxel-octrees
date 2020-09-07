using System.Collections.Generic;
using UnityEngine;

namespace SVO
{
    public class OctreeModel: MonoBehaviour
    {
        private ComputeBuffer _structureBuffer;
        private ComputeBuffer _shadingBuffer;
        
        private static readonly int OctreePrimaryData = Shader.PropertyToID("octree_primary_data");
        private static readonly int OctreeAttribData = Shader.PropertyToID("octree_attrib_data");
        private static readonly int Initialized = Shader.PropertyToID("initialized");
        
        internal Queue<int> FreeStructureMemoryCache = new Queue<int>();
        internal List<int> FreeShadingMemoryCached = new List<int>();

        private OctreeData _initialData;
        
        public void Awake()
        {
            _structureBuffer = new ComputeBuffer(1, 4);
            _shadingBuffer = new ComputeBuffer(1, 4);
            if(_initialData != null) SetData(_initialData);
            _initialData = null;
        }

        public void SetData(OctreeData data)
        {
            FreeShadingMemoryCached = data.FreeShadingMemory;
            FreeStructureMemoryCache = data.FreeStructureMemory;
            
            if (_structureBuffer == null)
            {
                _initialData = data;
                return;
            }
            
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
            
            return new OctreeData(structureDataList, shadingDataList, 
                FreeStructureMemoryCache, FreeShadingMemoryCached);
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