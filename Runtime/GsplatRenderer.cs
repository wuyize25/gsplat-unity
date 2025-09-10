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
        public bool GammaToLinear;

        GsplatAsset m_prevAsset;
        GsplatRendererImpl m_renderer;

        public bool Valid => GsplatAsset;
        public uint SplatCount => GsplatAsset ? GsplatAsset.SplatCount : 0;
        public ISorterResource SorterResource => m_renderer.SorterResource;

        void SetBufferData()
        {
            m_renderer.PositionBuffer.SetData(GsplatAsset.Positions);
            m_renderer.ScaleBuffer.SetData(GsplatAsset.Scales);
            m_renderer.RotationBuffer.SetData(GsplatAsset.Rotations);
            m_renderer.ColorBuffer.SetData(GsplatAsset.Colors);
            if (GsplatAsset.SHBands > 0)
                m_renderer.SHBuffer.SetData(GsplatAsset.SHs);
        }

        void OnEnable()
        {
            GsplatSorter.Instance.RegisterGsplat(this);
            if (!GsplatAsset)
                return;
            m_renderer = new GsplatRendererImpl(GsplatAsset.SplatCount, GsplatAsset.SHBands);
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
            if (m_prevAsset != GsplatAsset)
            {
                m_prevAsset = GsplatAsset;
                if (GsplatAsset)
                {
                    if (m_renderer == null)
                        m_renderer = new GsplatRendererImpl(GsplatAsset.SplatCount, GsplatAsset.SHBands);
                    else
                        m_renderer.RecreateResources(GsplatAsset.SplatCount, GsplatAsset.SHBands);
                    SetBufferData();
                }
            }

            if (Valid)
                m_renderer.Render(GsplatAsset.SplatCount, transform, GsplatAsset.Bounds, gameObject.layer,
                    GammaToLinear, SHDegree);
        }
    }
}