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
        internal static readonly List<Model> Models = new List<Model>();
        private static ModelSorter _modelSorter;
            
        public ComputeShader clearShader;
        public ComputeShader colorDepthShader;
        public ComputeShader lightingShader;

        private Camera _camera;
        private RenderTexture _diffuseTexture;
        private RenderTexture _positionTexture;
        private RenderTexture _normalTexture;
        private RenderTexture _resultTexture;

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
            clearShader.SetTexture(0, "diffuse_texture", _diffuseTexture);
            clearShader.SetTexture(0, "position_texture", _positionTexture);
            clearShader.SetTexture(0, "normal_texture", _normalTexture);
            clearShader.Dispatch(0, threadGroupsX, threadGroupsY, 1);

            // var frustrumPlanes = GeometryUtility.CalculateFrustumPlanes(_camera);
            foreach (var model in Models)
            {
                model.Render(colorDepthShader, _camera, _diffuseTexture, _positionTexture, _normalTexture);   
            }
            
            lightingShader.SetTexture(0, "result_texture", _resultTexture);
            lightingShader.SetTexture(0, "diffuse_texture", _diffuseTexture);
            lightingShader.SetTexture(0, "position_texture", _positionTexture);
            lightingShader.SetTexture(0, "normal_texture", _normalTexture);
            lightingShader.SetVector("sun_direction", new Vector3(.5f, -1f, .6f).normalized);
            lightingShader.SetVector("sun_diffuse", new Vector3(1f, 1f, 1f));
            lightingShader.SetVector("sun_specular", new Vector3(.04f, .05f, .04f));
            lightingShader.SetFloat("sun_shininess", 2f);
            lightingShader.SetVector("ambient_light", new Vector3(.4f, .45f, .4f));
            lightingShader.SetVector("cam_pos", _camera.transform.position);
            
            lightingShader.Dispatch(0, threadGroupsX, threadGroupsY, 1);
            
            Graphics.Blit(_resultTexture, dest);
        }

        private void CreateTextures()
        {
            if (_diffuseTexture == null || _positionTexture == null ||  _diffuseTexture == null || _resultTexture == null ||
                Screen.width != _diffuseTexture.width || Screen.height != _diffuseTexture.height)
            {
                if(_diffuseTexture != null) _diffuseTexture.Release();
                if(_positionTexture != null) _positionTexture.Release();
                if(_normalTexture != null) _normalTexture.Release();
                if(_resultTexture != null) _resultTexture.Release();
                
                _diffuseTexture = new RenderTexture(Screen.width, Screen.height, 0, RenderTextureFormat.ARGB32);
                _positionTexture = new RenderTexture(Screen.width, Screen.height, 0, RenderTextureFormat.ARGBFloat);
                _normalTexture = new RenderTexture(Screen.width, Screen.height, 0, RenderTextureFormat.ARGB32);
                _resultTexture = new RenderTexture(Screen.width, Screen.height, 0, RenderTextureFormat.ARGB32);

                _diffuseTexture.enableRandomWrite = true;
                _positionTexture.enableRandomWrite = true;
                _normalTexture.enableRandomWrite = true;
                _resultTexture.enableRandomWrite = true;

                _diffuseTexture.Create();
                _positionTexture.Create();
                _normalTexture.Create();
                _resultTexture.Create();   
            }
        }
    }
}