using UnityEngine;

namespace SVO
{
    public abstract class Model: MonoBehaviour
    {
        protected virtual void Awake()
        {
            SvoRenderer.Models.Add(this);
        }

        internal abstract void Render(ComputeShader shader, Camera camera, RenderTexture diffuseTexture, RenderTexture positionTexture, RenderTexture normalTexture);
        
        internal abstract void MapShadows(ComputeShader shader, Camera camera, RenderTexture diffuseTexture, RenderTexture positionTexture, RenderTexture normalTexture, RenderTexture shadowTexture, Vector3 sunDir);
    }
}