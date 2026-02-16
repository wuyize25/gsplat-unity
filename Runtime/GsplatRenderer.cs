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

        private int shDegree = 3;
        public int SHDegree
        {
            get
            {
                return shDegree;
            }
            set
            {
                shDegree = value;
                SetSHDegreeData();
            }
        }

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
            SetSHDegreeData();
        }

        void SetSHDegreeData()
        {
            if (m_renderer != null)
            {
                m_renderer.EditSHBands((byte)shDegree);
                int lastPos = 0;
                for (int i = 0; i != m_renderer.SHBands; i++)
                {
                    m_renderer.SHBuffer.SetData(GsplatAsset.SHs[i].SHs, 0, lastPos, GsplatAsset.SHs[i].SHs.Length);
                    lastPos += GsplatAsset.SHs[i].SHs.Length;
                }
            }
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
            UploadSHDegreeData(offset, count);
        }

        void UploadSHDegreeData(int offset, int count)
        {
            if (m_renderer != null)
            {
                m_renderer.EditSHBands((byte)shDegree);
                int lastPos = 0;
                for (int i = 0; i != m_renderer.SHBands; i++)
                {
                    var coefficientOffset = GsplatUtils.SHBandsToCoefficientOffsetCount(GsplatAsset.SHBands);

                    m_renderer.SHBuffer.SetData(GsplatAsset.SHs[i].SHs, coefficientOffset * offset, coefficientOffset * offset + lastPos, coefficientOffset * count);
                    lastPos += GsplatAsset.SHs[i].SHs.Length;
                }
            }
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
                    gameObject.layer, GammaToLinear);
        }
    }
}
