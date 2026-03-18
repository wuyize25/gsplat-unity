// Copyright (c) 2025 Yize Wu
// SPDX-License-Identifier: MIT

using UnityEngine;
using UnityEngine.Rendering;

namespace Gsplat
{
    [ExecuteAlways]
    public class GsplatRenderer : MonoBehaviour, IGsplat
    {
        public GsplatAsset GsplatAsset;
        [Range(0, 3)] public int SHDegree = 3;
        public float Brightness = 1.0f;
        [Tooltip("Improves rendering speed by shrinking Gaussian splats while trying to keep the impact on visual quality as small as possible.")]
        [Range(0, 1)] public float SplatDownscaleFactor = 0.0f;
        public bool GammaToLinear;
        public bool AsyncUpload;
        public bool RenderBeforeUploadComplete = true;

        GsplatAsset m_prevAsset;
        GsplatRendererImpl m_renderer;

        public bool Valid => RenderBeforeUploadComplete ? SplatCount > 0 : SplatCount == GsplatAsset.SplatCount;

        public uint SplatCount => m_renderer != null ? m_renderer.GsplatResource?.UploadedCount ?? 0 : 0;

        public ISorterResource SorterResource => m_renderer.SorterResource;

        public void ComputeDepth(CommandBuffer cmd, Matrix4x4 matrixMv) => m_renderer.ComputeDepth(cmd, matrixMv);

        void OnEnable()
        {
            GsplatSorter.Instance.RegisterGsplat(this);
            m_prevAsset = null;
        }

        void OnDisable()
        {
            GsplatSorter.Instance.UnregisterGsplat(this);
            m_renderer?.Dispose();
            m_renderer = null;
        }

        void Update()
        {
            if (m_prevAsset != GsplatAsset)
            {
                m_renderer?.ReleaseGsplatAsset();
                m_prevAsset = GsplatAsset;
                if (GsplatAsset)
                {
                    if (m_renderer == null)
                        m_renderer = new GsplatRendererImpl(GsplatAsset.SplatCount, GsplatAsset.SHBands);
                    else
                        m_renderer.RecreateResources(GsplatAsset.SplatCount, GsplatAsset.SHBands);
#if UNITY_EDITOR
                    var asyncUpload = AsyncUpload && Application.isPlaying;
#else
                    var asyncUpload = AsyncUpload;
#endif
                    m_renderer.BindGsplatAsset(GsplatAsset, asyncUpload);
                }
            }

            if (Valid)
                m_renderer.Render(SplatCount, transform, GsplatAsset.Bounds,
                    gameObject.layer, GammaToLinear, SHDegree, Brightness, 1.0f - SplatDownscaleFactor);
        }
    }
}
