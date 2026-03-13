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
        [Range(0, 3)] public int SHDegree = 3;
        [Range(0, 10)] public float Brightness = 1.0f;
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
            m_renderer.PositionBuffer.SetData(GsplatAsset.Positions);
            m_renderer.ScaleBuffer.SetData(GsplatAsset.Scales);
            m_renderer.RotationBuffer.SetData(GsplatAsset.Rotations);
            m_renderer.ColorBuffer.SetData(GsplatAsset.Colors);
            if (GsplatAsset.SHBands > 0)
                m_renderer.SHBuffer.SetData(GsplatAsset.SHs);
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
            m_renderer.PositionBuffer.SetData(GsplatAsset.Positions, offset, offset, count);
            m_renderer.ScaleBuffer.SetData(GsplatAsset.Scales, offset, offset, count);
            m_renderer.RotationBuffer.SetData(GsplatAsset.Rotations, offset, offset, count);
            m_renderer.ColorBuffer.SetData(GsplatAsset.Colors, offset, offset, count);
            if (GsplatAsset.SHBands <= 0) return;
            var coefficientCount = GsplatUtils.SHBandsToCoefficientCount(GsplatAsset.SHBands);
            m_renderer.SHBuffer.SetData(GsplatAsset.SHs, coefficientCount * offset,
                coefficientCount * offset, coefficientCount * count);
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
                    gameObject.layer, GammaToLinear, SHDegree, Brightness);
        }
    }
}