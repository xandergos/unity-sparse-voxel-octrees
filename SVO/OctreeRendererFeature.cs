using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

namespace SVO
{
    public class OctreeRendererFeature : ScriptableRendererFeature
    {
        internal static List<Renderer> octreeRenderers = new List<Renderer>();
        
        private class OctreeRenderPass : ScriptableRenderPass
        {
            /// <summary>
            /// Prepare for execution. Called before render pass executes.
            /// </summary>
            public override void Configure(CommandBuffer cmd, RenderTextureDescriptor cameraTextureDescriptor) { }
            
            /// <summary>
            /// Executes the render pass.
            /// </summary>
            public override void Execute(ScriptableRenderContext context, ref RenderingData renderingData)
            {
                CommandBuffer cmd = CommandBufferPool.Get();

                foreach (var octree in OctreeRendererFeature.octreeRenderers)
                {
                }

                // execution
                context.ExecuteCommandBuffer(cmd);
                CommandBufferPool.Release(cmd);
            }
            
            /// <summary>
            /// Cleanup any allocated resources that were created during the execution of this render pass.
            /// </summary>
            public override void FrameCleanup(CommandBuffer cmd) { }
        }

        private OctreeRenderPass _renderPass;
        
        public override void Create()
        {
            _renderPass = new OctreeRenderPass
            {
                renderPassEvent = RenderPassEvent.AfterRenderingOpaques
            };
        }
        
        /// <summary>
        /// Inject render pass into camera. Called once per camera.
        /// </summary>
        public override void AddRenderPasses(ScriptableRenderer renderer, ref RenderingData renderingData)
        {
            renderingData.cameraData.requiresDepthTexture = true;
            renderer.EnqueuePass(_renderPass);
        }
    }
}