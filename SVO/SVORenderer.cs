using System;
using System.Collections.Generic;
using UnityEngine;

namespace SVO
{
    /**
     * This class must be attached to a camera for it to render SVOs.
     */
    public class SvoRenderer: MonoBehaviour
    {
        private class ModelSorter: Comparer<Model>
        {
            private readonly Camera _camera;
            
            public ModelSorter(Camera camera)
            {
                _camera = camera;
            }
            
            public override int Compare(Model x, Model y)
            {
                var cameraPos = _camera.transform.position;
                var sqrDistanceX = (x.transform.position - cameraPos).sqrMagnitude;
                var sqrDistanceY = (y.transform.position - cameraPos).sqrMagnitude;

                return sqrDistanceX.CompareTo(sqrDistanceY);
            }
        }

        // Model registry
        internal static List<Model> Models = new List<Model>();
        private static ModelSorter _modelSorter;
            
        public ComputeShader clearShader;
        public ComputeShader colorDepthShader;

        private Camera _camera;
        private RenderTexture _svoTexture;
        private RenderTexture _svoDepthMask;

        public void Start()
        {
            _camera = GetComponent<Camera>();
            _modelSorter = new ModelSorter(_camera);
        }

        private void OnRenderImage(RenderTexture src, RenderTexture dest)
        {
            Models.Sort(_modelSorter);
            
            CreateTextures();
            
            int threadGroupsX = Mathf.CeilToInt(Screen.width / 16.0f);
            int threadGroupsY = Mathf.CeilToInt(Screen.height / 16.0f);
            clearShader.SetTexture(0, "result_texture", _svoTexture);
            clearShader.SetTexture(0, "depth_mask", _svoDepthMask);
            clearShader.Dispatch(0, threadGroupsX, threadGroupsY, 1);

            var frustrumPlanes = GeometryUtility.CalculateFrustumPlanes(_camera);
            foreach (var model in Models)
            {
                model.Render(colorDepthShader, _camera, _svoTexture, _svoDepthMask);   
            }
            Graphics.Blit(_svoTexture, dest);
        }

        private void CreateTextures()
        {
            if (_svoTexture == null || _svoDepthMask == null ||
                Screen.width != _svoTexture.width || Screen.height != _svoTexture.height ||
                Screen.width != _svoDepthMask.width || Screen.height != _svoDepthMask.height)
            {
                if(_svoTexture != null) _svoTexture.Release();
                if(_svoDepthMask != null) _svoDepthMask.Release();
                
                _svoTexture = new RenderTexture(Screen.width, Screen.height, 0, RenderTextureFormat.ARGB32);
                _svoDepthMask = new RenderTexture(Screen.width, Screen.height, 0, RenderTextureFormat.RFloat);

                _svoTexture.enableRandomWrite = true;
                _svoDepthMask.enableRandomWrite = true;

                _svoTexture.Create();
                _svoDepthMask.Create();
            }
        }
    }
}