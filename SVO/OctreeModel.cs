using System;
using System.Collections.Generic;
using UnityEngine;

namespace SVO
{
    [ExecuteAlways]
    public class OctreeModel: MonoBehaviour
    {
        public OctreeData data;
        
        private static readonly int OctreePrimaryData = Shader.PropertyToID("octree_primary_data");
        private static readonly int OctreeAttribData = Shader.PropertyToID("octree_attrib_data");
        private static readonly int Initialized = Shader.PropertyToID("initialized");

        private void OnWillRenderObject()
        {
            var material = GetComponent<Renderer>().sharedMaterial;
            if (data is null)
            {
                material.SetInt(Initialized, 0);
            }
            else
            {
                // Update Parameters
                material.SetBuffer(OctreePrimaryData, data.StructureBuffer);
                material.SetBuffer(OctreeAttribData, data.AttributeBuffer);
                material.SetInt(Initialized, 1);
            }
        }
    }
}