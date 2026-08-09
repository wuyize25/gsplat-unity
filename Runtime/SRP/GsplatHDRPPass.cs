// Copyright (c) 2025 Yize Wu
// SPDX-License-Identifier: MIT

#if GSPLAT_ENABLE_HDRP
using UnityEngine.Rendering;
using UnityEngine.Rendering.HighDefinition;

namespace Gsplat
{
    public class GsplatHDRPPass : CustomPass
    {
        protected override void Execute(CustomPassContext ctx)
        {
            if (GsplatSorter.Instance.Valid && GsplatSettings.Instance.Valid && GsplatSorter.Instance.GatherGsplatsForCamera(ctx.hdCamera.camera))
            {
                GsplatSorter.Instance.DispatchSort(ctx.cmd, ctx.hdCamera.camera);
                CoreUtils.SetRenderTarget(ctx.cmd, ctx.cameraColorBuffer, ctx.cameraDepthBuffer);
                GsplatSorter.Instance.RenderDepthPrepass(ctx.cmd, ctx.hdCamera.camera);
                GsplatSorter.Instance.RenderColor(ctx.cmd, ctx.hdCamera.camera);
            }
        }
    }
}

#endif
