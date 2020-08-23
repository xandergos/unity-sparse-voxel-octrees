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
        public ComputeShader shadowMapper;

        private Camera _camera;
        private RenderTexture _diffuseTexture;
        private RenderTexture _positionTexture;
        private RenderTexture _normalTexture;
        private RenderTexture _resultTexture;
        private RenderTexture _shadowTexture;
        private float shadowTextureScale = 0.5f;

        public Light sun;
        public Color ambientLight;

        public bool shadows = true;

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
            clearShader.SetTexture(0, "shadow_texture", _shadowTexture);
            clearShader.Dispatch(0, threadGroupsX, threadGroupsY, 1);

            var frustrumPlanes = GeometryUtility.CalculateFrustumPlanes(_camera);
            foreach (var model in Models)
            {
                if (!model.isActiveAndEnabled) continue;
                var bounds = new Bounds(model.transform.position + model.transform.lossyScale / 2, model.transform.lossyScale);
                if(GeometryUtility.TestPlanesAABB(frustrumPlanes, bounds))
                    model.Render(colorDepthShader, _camera, _diffuseTexture, _positionTexture, _normalTexture);
            }   
            if(shadows)
            {
                foreach (var model in Models)
                    model.MapShadows(shadowMapper, _camera, _diffuseTexture, _positionTexture, _normalTexture, _shadowTexture, sun.transform.forward);
            }   
            
            lightingShader.SetTexture(0, "result_texture", _resultTexture);
            lightingShader.SetTexture(0, "diffuse_texture", _diffuseTexture);
            lightingShader.SetTexture(0, "position_texture", _positionTexture);
            lightingShader.SetTexture(0, "normal_texture", _normalTexture);
            lightingShader.SetTexture(0, "shadow_texture", _shadowTexture);
            lightingShader.SetVector("sun_direction", sun.transform.forward);
            lightingShader.SetVector("sun_diffuse", sun.color * sun.intensity);
            lightingShader.SetVector("sun_specular", sun.color * sun.bounceIntensity);
            lightingShader.SetFloat("mat_shininess", 16f);
            lightingShader.SetFloat("mat_roughness", 0.2f);
            lightingShader.SetVector("ambient_light", new Vector3(ambientLight.r, ambientLight.g, ambientLight.b));
            lightingShader.SetVector("cam_pos", _camera.transform.position);
            
            lightingShader.Dispatch(0, threadGroupsX, threadGroupsY, 1);
            
            Graphics.Blit(_resultTexture, dest);
        }

        private void CreateTextures()
        {
            if (_diffuseTexture == null || _positionTexture == null ||  _diffuseTexture == null || _resultTexture == null ||
                _shadowTexture == null || Screen.width != _diffuseTexture.width || Screen.height != _diffuseTexture.height)
            {
                if(_diffuseTexture != null) _diffuseTexture.Release();
                if(_positionTexture != null) _positionTexture.Release();
                if(_normalTexture != null) _normalTexture.Release();
                if(_resultTexture != null) _resultTexture.Release();
                if(_shadowTexture != null) _shadowTexture.Release();
                
                _diffuseTexture = new RenderTexture(Screen.width, Screen.height, 0, RenderTextureFormat.ARGB32);
                _positionTexture = new RenderTexture(Screen.width, Screen.height, 0, RenderTextureFormat.ARGBFloat);
                _normalTexture = new RenderTexture(Screen.width, Screen.height, 0, RenderTextureFormat.ARGB32);
                _resultTexture = new RenderTexture(Screen.width, Screen.height, 0, RenderTextureFormat.ARGB32);
                _shadowTexture = new RenderTexture((int)(Screen.width * shadowTextureScale),
                    (int)(Screen.height * shadowTextureScale), 0, RenderTextureFormat.RFloat);
                
                _diffuseTexture.enableRandomWrite = true;
                _positionTexture.enableRandomWrite = true;
                _normalTexture.enableRandomWrite = true;
                _resultTexture.enableRandomWrite = true;
                _shadowTexture.enableRandomWrite = true;

                _diffuseTexture.Create();
                _positionTexture.Create();
                _normalTexture.Create();
                _resultTexture.Create();   
                _shadowTexture.Create();
            }
        }
    }
}