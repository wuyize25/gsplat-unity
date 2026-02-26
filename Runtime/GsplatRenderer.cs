// Copyright (c) 2025 Yize Wu
// SPDX-License-Identifier: MIT

using System;
using UnityEngine;
using UnityEngine.Rendering;

namespace Gsplat
{
    [ExecuteAlways]
    public class GsplatRenderer : MonoBehaviour, IGsplat
    {
        public GsplatAsset GsplatAsset;
        [Range(0, 3)] public int SHDegree = 3;
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

        public void ComputeDepth(CommandBuffer cmd, Matrix4x4 matrixMv) =>
            GsplatAsset.ComputeDepth(cmd, matrixMv, SorterResource);

        uint m_pendingSplatCount;


        void SetBufferDataAsync()
        {
            m_pendingSplatCount = GsplatAsset.SplatCount;
        }

        /*void UploadData()
        {
            var offset = (int)(GsplatAsset.SplatCount - m_pendingSplatCount);
            var count = (int)Math.Min(UploadBatchSize, m_pendingSplatCount);
            m_pendingSplatCount -= (uint)count;
            var asset = (GsplatAssetSpark)GsplatAsset;
            m_renderer.PackedSplatsBuffer.SetData(asset.PackedSplats, offset, offset, count);
            if (GsplatAsset.SHBands <= 0) return;
            var coefficientCount = GsplatUtils.SHBandsToCoefficientCount(GsplatAsset.SHBands);
            m_renderer.SHBuffer.SetData(asset.SHs, coefficientCount * offset,
                coefficientCount * offset, coefficientCount * count);
        }*/

        void OnEnable()
        {
            GsplatSorter.Instance.RegisterGsplat(this);
            if (!GsplatAsset)
                return;
            m_renderer = new GsplatRendererImpl(GsplatAsset.SplatCount, GsplatAsset.SHBands);
            m_renderer.BindGsplatAsset(GsplatAsset);
#if UNITY_EDITOR
            if (AsyncUpload && Application.isPlaying)
#else
            if (AsyncUpload)
#endif
                SetBufferDataAsync();
            else
                GsplatAsset.UploadData();
        }

        void OnDisable()
        {
            GsplatSorter.Instance.UnregisterGsplat(this);
            m_renderer?.Dispose();
            m_renderer = null;
        }

        void Update()
        {
            //if (m_pendingSplatCount > 0)
            //    UploadData();

            if (m_prevAsset != GsplatAsset)
            {
                m_prevAsset = GsplatAsset;
                if (GsplatAsset)
                {
                    if (m_renderer == null)
                        m_renderer = new GsplatRendererImpl(GsplatAsset.SplatCount, GsplatAsset.SHBands);
                    else
                        m_renderer.RecreateResources(GsplatAsset.SplatCount, GsplatAsset.SHBands);
                    m_renderer.BindGsplatAsset(GsplatAsset);
#if UNITY_EDITOR
                    if (AsyncUpload && Application.isPlaying)
#else
                    if (AsyncUpload)
#endif
                        SetBufferDataAsync();
                    else
                        GsplatAsset.UploadData();
                }
            }

            if (Valid)
                m_renderer.Render(SplatCount, transform, GsplatAsset.Bounds, gameObject.layer, GammaToLinear, SHDegree);
        }
    }
}