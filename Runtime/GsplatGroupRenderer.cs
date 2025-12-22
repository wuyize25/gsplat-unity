// Copyright (c) 2025 Yize Wu
// SPDX-License-Identifier: MIT

using System;
using System.Linq;
using UnityEngine;

namespace Gsplat
{
    [Serializable]
    public class GsplatAssetData
    {
        public GsplatAsset Asset;
        public Transform Transform;
    }

    [ExecuteAlways]
    public class GsplatGroupRenderer : MonoBehaviour, IGsplat
    {
        public GsplatAssetData[] GsplatAssets;
        [Range(0, 3)] public int SHDegree = 3;
        public bool GammaToLinear;

        GsplatAssetData[] m_prevAssets;
        GsplatRendererImpl m_renderer;
        uint m_splatCount;
        bool m_dataDirty = true;

        public bool Valid => GsplatAssets != null && GsplatAssets.Length > 0 && m_splatCount > 0;
        public uint SplatCount => m_splatCount;
        public ISorterResource SorterResource => m_renderer?.SorterResource;
        public Matrix4x4 LocalToWorldMatrix => Matrix4x4.identity;

        void RecalculateSplatCount()
        {
            m_splatCount = 0;
            if (GsplatAssets == null) return;
            foreach (var assetData in GsplatAssets)
            {
                if (assetData?.Asset != null)
                {
                    m_splatCount += assetData.Asset.SplatCount;
                }
            }
        }

        void SetBufferData()
        {
            if (m_renderer == null || GsplatAssets == null) return;

            uint offset = 0;
            foreach (var assetData in GsplatAssets)
            {
                if (assetData?.Asset == null) continue;
                var asset = assetData.Asset;
                var transform = assetData.Transform ? assetData.Transform : this.transform;

                var positions = new Vector3[asset.SplatCount];
                var rotations = new Vector4[asset.SplatCount];

                for (int i = 0; i < asset.SplatCount; i++)
                {
                    positions[i] = transform.TransformPoint(asset.Positions[i]);
                    var worldRot = transform.rotation * asset.Rotations[i].ToQuaternion();
                    rotations[i] = worldRot.ToVector4();
                }

                m_renderer.PositionBuffer.SetData(positions, 0, (int)offset, positions.Length);
                m_renderer.ScaleBuffer.SetData(asset.Scales, 0, (int)offset, asset.Scales.Length);
                m_renderer.RotationBuffer.SetData(rotations, 0, (int)offset, rotations.Length);
                m_renderer.ColorBuffer.SetData(asset.Colors, 0, (int)offset, asset.Colors.Length);
                if (asset.SHBands > 0)
                {
                    // TODO: SHs should be rotated to world space
                    m_renderer.SHBuffer.SetData(asset.SHs, 0,
                        (int)(offset * GsplatUtils.SHBandsToCoefficientCount(asset.SHBands)), asset.SHs.Length);
                }

                offset += asset.SplatCount;
            }

            m_dataDirty = false;
        }

        void OnEnable()
        {
            GsplatSorter.Instance.RegisterGsplat(this);
            CheckForAssetChanges();
        }

        void OnDisable()
        {
            GsplatSorter.Instance.UnregisterGsplat(this);
            m_renderer?.Dispose();
            m_renderer = null;
            m_prevAssets = null;
        }

        void Update()
        {
            CheckForAssetChanges();

            if (Valid && m_renderer != null)
            {
                for (int i = 0; i < GsplatAssets.Length; i++)
                {
                    if (GsplatAssets[i] == null) continue;
                    var t = GsplatAssets[i].Transform ? GsplatAssets[i].Transform : transform;
                    if (t.hasChanged)
                    {
                        m_dataDirty = true;
                        t.hasChanged = false;
                    }
                }

                if (m_dataDirty)
                {
                    SetBufferData();
                }

                var bounds = new Bounds(transform.position, Vector3.one * 10000); // Simplified bounds for rendering
                m_renderer.Render(m_splatCount, bounds, gameObject.layer, GammaToLinear, SHDegree);
            }
        }

        void CheckForAssetChanges()
        {
            bool changed = false;
            if (m_prevAssets == null && GsplatAssets != null)
            {
                changed = true;
            }
            else if (m_prevAssets != null && GsplatAssets == null)
            {
                changed = true;
            }
            else if (m_prevAssets != null && GsplatAssets != null && !m_prevAssets.SequenceEqual(GsplatAssets))
            {
                changed = true;
            }

            if (changed)
            {
                RecalculateSplatCount();

                byte maxShBands = 0;
                if (GsplatAssets != null)
                {
                    foreach (var assetData in GsplatAssets)
                    {
                        if (assetData?.Asset != null && assetData.Asset.SHBands > maxShBands)
                        {
                            maxShBands = assetData.Asset.SHBands;
                        }
                    }
                }

                if (m_splatCount > 0)
                {
                    if (m_renderer == null)
                        m_renderer = new GsplatRendererImpl(m_splatCount, maxShBands);
                    else
                        m_renderer.RecreateResources(m_splatCount, maxShBands);
                }
                else
                {
                    m_renderer?.Dispose();
                    m_renderer = null;
                }

                m_prevAssets = GsplatAssets != null ? (GsplatAssetData[])GsplatAssets.Clone() : null;
                m_dataDirty = true;
            }
        }
    }
}