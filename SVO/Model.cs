using System;
using UnityEngine;

namespace SVO
{
    public abstract class Model: MonoBehaviour
    {
        protected virtual void Start()
        {
            SVORenderer.Models.Add(this);
        }

        internal abstract void Render(ComputeShader shader, Camera camera, RenderTexture colorTexture, RenderTexture depthMask);
    }
}