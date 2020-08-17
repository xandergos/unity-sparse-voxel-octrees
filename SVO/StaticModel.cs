using UnityEngine;

namespace SVO
{
    /**
     * A model that can not be changed at runtime. These models have no memory overhead on the CPU.
     */
    public class StaticModel: Model
    {
        // TODO
        internal override void Render(ComputeShader shader, Camera camera, RenderTexture colorTexture, RenderTexture depthMask)
        {
            throw new System.NotImplementedException();
        }
    }
}