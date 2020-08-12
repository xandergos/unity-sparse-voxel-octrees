using System;
using UnityEngine;

namespace SVO
{
    public class Chunk
    {
        public ComputeBuffer Buffer = null;
        public OctreeBranch RootNode = new OctreeBranch(null, 0);

        public void UpdateBuffer(int maxDepth)
        {
            Buffer?.Dispose();
            var dataArr = new int[RootNode.GetTotalDataSize(true)];
            RootNode.InsertIntoArray(dataArr, 0, 0);
            Buffer = new ComputeBuffer(dataArr.Length, 4);
            Buffer.SetData(dataArr);
        }
        
        public Voxel GetVoxel(Vector3 normPos, out int depth)
        {
            return RootNode.GetVoxel(normPos, out depth);
        }

        public ColoredVoxel SetVoxel(Vector3 normPos, int depth, Color color)
        {
            return RootNode.SetVoxel(normPos, depth, color);
        }

        public void Render(Vector3 pos, Vector3 scale, Camera camera, RenderTexture colorTexture, RenderTexture depthTexture)
        {
            if (Buffer == null)
                UpdateBuffer(23);
            
            int threadGroupsX = Mathf.CeilToInt(Screen.width / 16.0f);
            int threadGroupsY = Mathf.CeilToInt(Screen.height / 16.0f);
            
            // Update Parameters
            Shaders.Unlit.SetMatrix("_CameraToWorld", camera.cameraToWorldMatrix);
            Shaders.Unlit.SetMatrix("_CameraInverseProjection", camera.projectionMatrix.inverse);
            Shaders.Unlit.SetBuffer(0, "_OctreeRoot", Buffer);
            Shaders.Unlit.SetTexture(0, "ResultDistance", depthTexture);
            Shaders.Unlit.SetTexture(0, "Result", colorTexture);
            Shaders.Unlit.SetVector("_OctreePos", pos);
            Shaders.Unlit.SetVector("_OctreeScale", scale);
            
            Shaders.Unlit.Dispatch(0, threadGroupsX, threadGroupsY, 1);
        }
        
        ~Chunk()
        {
            Buffer?.Dispose();
        }
    }
}