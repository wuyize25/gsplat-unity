// Copyright (c) 2025 Yize Wu
// SPDX-License-Identifier: MIT

using System;
using UnityEngine;

namespace Gsplat
{
    [ExecuteAlways]
    public class GsplatRenderer : MonoBehaviour, IGsplat
    {
        public GsplatAsset GsplatAsset;
        [HideInInspector] public int SHDegree = 3;

        public bool GammaToLinear;
        public bool AsyncUpload;

        [Tooltip("Max splat count to be uploaded per frame")]
        public uint UploadBatchSize = 100000;

        public bool RenderBeforeUploadComplete = true;

        GsplatAsset m_prevAsset;
        GsplatRendererImpl m_renderer;

        public bool Valid => RenderBeforeUploadComplete ? SplatCount > 0 : SplatCount == GsplatAsset.SplatCount;
        public uint SplatCount => GsplatAsset ? GsplatAsset.SplatCount - m_pendingSplatCount : 0;
        public ISorterResource SorterResource => m_renderer.SorterResource;

        uint m_pendingSplatCount;

        void SetBufferData()
        {
            m_renderer.PackedSplatsBuffer.SetData(GsplatAsset.PackedSplats);
            if (GsplatAsset.SHBands >= 1)
                m_renderer.PackedSH1Buffer.SetData(GsplatAsset.PackedSH1);
            if (GsplatAsset.SHBands >= 2)
                m_renderer.PackedSH2Buffer.SetData(GsplatAsset.PackedSH2);
            if (GsplatAsset.SHBands == 3)
                m_renderer.PackedSH3Buffer.SetData(GsplatAsset.PackedSH3);
        }

        void SetBufferDataAsync()
        {
            m_pendingSplatCount = GsplatAsset.SplatCount;
        }

        void UploadData()
        {
            var offset = (int)(GsplatAsset.SplatCount - m_pendingSplatCount);
            var count = (int)Math.Min(UploadBatchSize, m_pendingSplatCount);
            m_pendingSplatCount -= (uint)count;
            m_renderer.PackedSplatsBuffer.SetData(GsplatAsset.PackedSplats, offset, offset, count);

            if (GsplatAsset.SHBands >= 1)
                m_renderer.PackedSH1Buffer.SetData(GsplatAsset.PackedSH1, 2 * offset, 2 * offset, 2 * count);
            if (GsplatAsset.SHBands >= 2)
                m_renderer.PackedSH2Buffer.SetData(GsplatAsset.PackedSH2, 4 * offset, 4 * offset, 4 * count);
            if (GsplatAsset.SHBands == 3)
                m_renderer.PackedSH3Buffer.SetData(GsplatAsset.PackedSH3, 4 * offset, 4 * offset, 4 * count);
        }

        void OnEnable()
        {
            GsplatSorter.Instance.RegisterGsplat(this);
            if (!GsplatAsset)
                return;
            m_renderer = new GsplatRendererImpl(GsplatAsset.SplatCount, GsplatAsset.SHBands);
#if UNITY_EDITOR
            if (AsyncUpload && Application.isPlaying)
#else
            if (AsyncUpload)
#endif
                SetBufferDataAsync();
            else
                SetBufferData();
        }

        void OnDisable()
        {
            GsplatSorter.Instance.UnregisterGsplat(this);
            m_renderer?.Dispose();
            m_renderer = null;
        }

        void Update()
        {
            if (m_pendingSplatCount > 0)
                UploadData();

            if (m_prevAsset != GsplatAsset)
            {
                m_prevAsset = GsplatAsset;
                if (GsplatAsset)
                {
                    if (m_renderer == null)
                        m_renderer = new GsplatRendererImpl(GsplatAsset.SplatCount, GsplatAsset.SHBands);
                    else
                        m_renderer.RecreateResources(GsplatAsset.SplatCount, GsplatAsset.SHBands);
#if UNITY_EDITOR
                    if (AsyncUpload && Application.isPlaying)
#else
                    if (AsyncUpload)
#endif
                        SetBufferDataAsync();
                    else
                        SetBufferData();
                }
            }

            if (Valid)
                m_renderer.Render(SplatCount, transform, GsplatAsset.Bounds,
                    gameObject.layer, GammaToLinear, SHDegree);
        }
    }
}
