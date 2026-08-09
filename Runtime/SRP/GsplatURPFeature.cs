// Originated from the GaussianSplatHDRPPass in aras-p/UnityGaussianSplatting by Aras Pranckevičius
// https://github.com/aras-p/UnityGaussianSplatting/blob/main/package/Runtime/GaussianSplatHDRPPass.cs
// Copyright (c) 2023 Aras Pranckevičius
// Modified by Yize Wu
// Copyright (c) 2025 Yize Wu
// SPDX-License-Identifier: MIT

#if GSPLAT_ENABLE_URP

using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;
#if UNITY_6000_0_OR_NEWER
using UnityEngine.Rendering.RenderGraphModule;
#endif

namespace Gsplat
{
    class GsplatURPFeature : ScriptableRendererFeature
    {
        class GsplatRenderPass : ScriptableRenderPass
        {
#if UNITY_6000_0_OR_NEWER
            class PassData
            {
                public UniversalCameraData CameraData;
            }

            public override void RecordRenderGraph(RenderGraph renderGraph, ContextContainer frameData)
            {
                using var builder = renderGraph.AddUnsafePass(GsplatSorter.k_passName, out PassData passData);
                passData.CameraData = frameData.Get<UniversalCameraData>();
                builder.AllowPassCulling(false);
                builder.SetRenderFunc(static (PassData data, UnsafeGraphContext context) =>
                {
                    var commandBuffer = CommandBufferHelpers.GetNativeCommandBuffer(context.cmd);
                    GsplatSorter.Instance.DispatchSort(commandBuffer, data.CameraData.camera);
                });
            }
#else
            public CommandBuffer CommandBuffer;
            public override void Execute(ScriptableRenderContext context, ref RenderingData renderingData)
            {
                GsplatSorter.Instance.DispatchSort(CommandBuffer, renderingData.cameraData.camera);
                context.ExecuteCommandBuffer(CommandBuffer);
            }
#endif
        }

        class GsplatDepthPrepass : ScriptableRenderPass
        {
#if UNITY_6000_0_OR_NEWER
            class PassData
            {
                public UniversalCameraData CameraData;
            }

            public override void RecordRenderGraph(RenderGraph renderGraph, ContextContainer frameData)
            {
                UniversalResourceData resourceData = frameData.Get<UniversalResourceData>();
                using var builder = renderGraph.AddRasterRenderPass<PassData>("Gsplat.DepthPrepass",
                    out PassData passData);
                passData.CameraData = frameData.Get<UniversalCameraData>();
                builder.SetRenderAttachmentDepth(resourceData.activeDepthTexture, AccessFlags.ReadWrite);
                builder.AllowPassCulling(false);
                builder.SetRenderFunc(static (PassData data, RasterGraphContext context) =>
                {
                    GsplatSorter.Instance.RenderDepthPrepass(context.cmd, data.CameraData.camera);
                });
            }
#else
            public override void Execute(ScriptableRenderContext context, ref RenderingData renderingData)
            {
                var cmd = CommandBufferPool.Get("Gsplat.DepthPrepass");
                GsplatSorter.Instance.RenderDepthPrepass(cmd, renderingData.cameraData.camera);
                context.ExecuteCommandBuffer(cmd);
                CommandBufferPool.Release(cmd);
            }
#endif
        }

        class GsplatColorPass : ScriptableRenderPass
        {
#if UNITY_6000_0_OR_NEWER
            class PassData
            {
                public UniversalCameraData CameraData;
            }

            public override void RecordRenderGraph(RenderGraph renderGraph, ContextContainer frameData)
            {
                UniversalResourceData resourceData = frameData.Get<UniversalResourceData>();
                using var builder = renderGraph.AddRasterRenderPass<PassData>("Gsplat.Color", out PassData passData);
                passData.CameraData = frameData.Get<UniversalCameraData>();
                builder.SetRenderAttachment(resourceData.activeColorTexture, 0, AccessFlags.ReadWrite);
                builder.SetRenderAttachmentDepth(resourceData.activeDepthTexture, AccessFlags.Read);
                builder.AllowPassCulling(false);
                builder.SetRenderFunc(static (PassData data, RasterGraphContext context) =>
                {
                    GsplatSorter.Instance.RenderColor(context.cmd, data.CameraData.camera);
                });
            }
#else
            public override void Execute(ScriptableRenderContext context, ref RenderingData renderingData)
            {
                var cmd = CommandBufferPool.Get("Gsplat.Color");
                GsplatSorter.Instance.RenderColor(cmd, renderingData.cameraData.camera);
                context.ExecuteCommandBuffer(cmd);
                CommandBufferPool.Release(cmd);
            }
#endif
        }

        GsplatRenderPass m_pass;
        GsplatColorPass m_colorPass;
        GsplatDepthPrepass m_depthPrepass;
        bool m_hasGsplats;

        public override void Create()
        {
            // Record all splat work before URP's transparent draw pass. Render splat color before
            // splat depth so overlapping transparent splats can blend before the depth write.
            m_pass = new GsplatRenderPass
            {
                renderPassEvent = (RenderPassEvent)((int)RenderPassEvent.BeforeRenderingTransparents - 3)
            };
            m_depthPrepass = new GsplatDepthPrepass
            {
                renderPassEvent = (RenderPassEvent)((int)RenderPassEvent.BeforeRenderingTransparents - 1)
            };
            m_colorPass = new GsplatColorPass
            {
                renderPassEvent = (RenderPassEvent)((int)RenderPassEvent.BeforeRenderingTransparents - 2)
            };
        }

        public override void OnCameraPreCull(ScriptableRenderer renderer, in CameraData cameraData)
        {
            m_hasGsplats = GsplatSorter.Instance.GatherGsplatsForCamera(cameraData.camera);
#if !UNITY_6000_0_OR_NEWER
            m_pass.CommandBuffer ??= new CommandBuffer { name = "SortGsplats" };
            m_pass.CommandBuffer.Clear();
#endif
        }

        public override void AddRenderPasses(ScriptableRenderer renderer, ref RenderingData renderingData)
        {
            if (GsplatSorter.Instance.Valid && GsplatSettings.Instance.Valid && m_hasGsplats)
            {
                renderer.EnqueuePass(m_pass);
                renderer.EnqueuePass(m_colorPass);
                renderer.EnqueuePass(m_depthPrepass);
            }
        }

        protected override void Dispose(bool disposing)
        {
#if !UNITY_6000_0_OR_NEWER
            m_pass.CommandBuffer?.Dispose();
            m_pass.CommandBuffer = null;
#endif
            m_pass = null;
            m_colorPass = null;
            m_depthPrepass = null;
        }
    }
}

#endif
