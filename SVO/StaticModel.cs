using UnityEngine;

namespace SVO
{
    /**
     * A model that can not be changed at runtime. These models have no memory overhead on the CPU.
     */
    public class StaticModel: Model
    {
        internal override void Render(ComputeShader shader, Camera camera, RenderTexture diffuseTexture, RenderTexture positionTexture, RenderTexture normalTexture)
        {
            throw new System.NotImplementedException();
        }

        internal override void MapShadows(ComputeShader shader, Camera camera, RenderTexture diffuseTexture, RenderTexture positionTexture,
            RenderTexture normalTexture, RenderTexture shadowTexture, Vector3 sunDir)
        {
            throw new System.NotImplementedException();
        }
    }
}