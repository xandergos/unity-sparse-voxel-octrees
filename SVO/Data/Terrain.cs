using System.Collections.Generic;
using System.Linq;
using UnityEngine;

namespace SVO
{
    internal class TerrainCompSorter : IComparer<Vector3Int>
    {
        public static Camera Camera;
        
        public int Compare(Vector3Int x, Vector3Int y)
        {
            Vector3 xf = new Vector3(x.x * 32f + 16f, x.y * 32f + 16f, x.z * 32f + 16f);
            Vector3 yf = new Vector3(y.x * 32f + 16f, y.y * 32f + 16f, x.z * 32f + 16f);
            var position = Camera.transform.position;
            var xMag = (xf - position).sqrMagnitude;
            var yMag = (yf - position).sqrMagnitude;
            return xMag.CompareTo(yMag);
        }
    }
    
    public class Terrain
    {
        public Camera Camera;
        public Dictionary<Vector3Int, Chunk> Chunks = new Dictionary<Vector3Int, Chunk>();
        private RenderTexture _colorTexture;
        private RenderTexture _depthTexture;
        public readonly Vector3 ChunkScale;

        public Terrain(Vector3 chunkScale, Camera camera)
        {
            ChunkScale = chunkScale;
            Camera = camera;
            TerrainCompSorter.Camera = camera;
        }
        
        public void Render(RenderTexture targetTexture)
        {
            // Make sure we have a current render target
            UpdateRenderTextures();
            int threadGroupsX = Mathf.CeilToInt(Screen.width / 16.0f);
            int threadGroupsY = Mathf.CeilToInt(Screen.height / 16.0f);
            Shaders.Clear.SetTexture(0, "Result", _colorTexture);
            Shaders.Clear.SetTexture(0, "ResultDistance", _depthTexture);
            Shaders.Clear.Dispatch(0, threadGroupsX, threadGroupsY, 1);

            var keys = Chunks.Keys.ToList();
            var frustrumPlanes = GeometryUtility.CalculateFrustumPlanes(Camera);
            /*keys.RemoveAll(v =>
            {
                var offset = ChunkScale / 2;
                var worldCenter = new Vector3(v.x * ChunkScale.x, v.y * ChunkScale.y, v.z * ChunkScale.z) + offset;
                var bounds = new Bounds(worldCenter, ChunkScale);
                return !GeometryUtility.TestPlanesAABB(frustrumPlanes, bounds);
            });*/
            keys.Sort(new TerrainCompSorter());
            Debug.Log($"{keys.Count} chunks drawn.");
            foreach (var key in keys)
            {
                var pos = new Vector3(key.x, key.y, key.z) * 32f;
                Chunks[key].Render(pos, ChunkScale, Camera, _colorTexture, _depthTexture);   
            }
            Graphics.Blit(_colorTexture, targetTexture);
        }

        private void UpdateRenderTextures()
        {
            if (_depthTexture == null || _colorTexture == null || 
                _colorTexture.width != Screen.width || _colorTexture.height != Screen.height)
            {
                if (_colorTexture != null)
                    _colorTexture.Release();
                if(_depthTexture != null)
                    _depthTexture.Release();

                _colorTexture = new RenderTexture(Screen.width, Screen.height, 0,
                    RenderTextureFormat.ARGBFloat, RenderTextureReadWrite.sRGB);
                _colorTexture.enableRandomWrite = true;
                _colorTexture.Create();
                
                _depthTexture = new RenderTexture(Screen.width, Screen.height, 0, RenderTextureFormat.RFloat);
                _depthTexture.enableRandomWrite = true;
                _depthTexture.Create();
            }
        }
    }
}