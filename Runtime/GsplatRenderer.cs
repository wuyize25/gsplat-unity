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
        public bool GammaToLinear;

        GsplatAsset m_prevAsset;

        MaterialPropertyBlock m_propertyBlock;
        GraphicsBuffer m_positionBuffer;
        GraphicsBuffer m_scaleBuffer;
        GraphicsBuffer m_rotationBuffer;
        GraphicsBuffer m_colorBuffer;
        GraphicsBuffer m_shBuffer;
        GraphicsBuffer m_orderBuffer;
        ISorterResource m_sorterResource;

        public bool Valid =>
            GsplatAsset &&
            m_positionBuffer != null &&
            m_scaleBuffer != null &&
            m_rotationBuffer != null &&
            m_colorBuffer != null &&
            (GsplatAsset.SHBands == 0 || m_shBuffer != null);

        public uint SplatCount => GsplatAsset ? GsplatAsset.SplatCount : 0;
        public ISorterResource SorterResource => m_sorterResource;

        static readonly int k_orderBuffer = Shader.PropertyToID("_OrderBuffer");
        static readonly int k_positionBuffer = Shader.PropertyToID("_PositionBuffer");
        static readonly int k_scaleBuffer = Shader.PropertyToID("_ScaleBuffer");
        static readonly int k_rotationBuffer = Shader.PropertyToID("_RotationBuffer");
        static readonly int k_colorBuffer = Shader.PropertyToID("_ColorBuffer");
        static readonly int k_shBuffer = Shader.PropertyToID("_SHBuffer");
        static readonly int k_matrixM = Shader.PropertyToID("_MATRIX_M");
        static readonly int k_splatInstanceSize = Shader.PropertyToID("_SplatInstanceSize");
        static readonly int k_splatCount = Shader.PropertyToID("_SplatCount");
        static readonly int k_gammaToLinear = Shader.PropertyToID("_GammaToLinear");

        void CreateResourcesForAsset()
        {
            if (!GsplatAsset)
                return;

            m_positionBuffer = new GraphicsBuffer(GraphicsBuffer.Target.Structured, (int)GsplatAsset.SplatCount,
                System.Runtime.InteropServices.Marshal.SizeOf(typeof(Vector3)));
            m_scaleBuffer = new GraphicsBuffer(GraphicsBuffer.Target.Structured, (int)GsplatAsset.SplatCount,
                System.Runtime.InteropServices.Marshal.SizeOf(typeof(Vector3)));
            m_rotationBuffer = new GraphicsBuffer(GraphicsBuffer.Target.Structured, (int)GsplatAsset.SplatCount,
                System.Runtime.InteropServices.Marshal.SizeOf(typeof(Vector4)));
            m_colorBuffer = new GraphicsBuffer(GraphicsBuffer.Target.Structured, (int)GsplatAsset.SplatCount,
                System.Runtime.InteropServices.Marshal.SizeOf(typeof(Vector4)));
            if (GsplatAsset.SHBands > 0)
                m_shBuffer = new GraphicsBuffer(GraphicsBuffer.Target.Structured, GsplatAsset.SHs.Length,
                    System.Runtime.InteropServices.Marshal.SizeOf(typeof(Vector3)));
            m_orderBuffer = new GraphicsBuffer(GraphicsBuffer.Target.Structured, (int)GsplatAsset.SplatCount,
                sizeof(uint));


            m_positionBuffer.SetData(GsplatAsset.Positions);
            m_scaleBuffer.SetData(GsplatAsset.Scales);
            m_rotationBuffer.SetData(GsplatAsset.Rotations);
            m_colorBuffer.SetData(GsplatAsset.Colors);
            if (GsplatAsset.SHBands > 0)
                m_shBuffer.SetData(GsplatAsset.SHs);

            m_sorterResource =
                GsplatSorter.Instance.CreateSorterResource(GsplatAsset.SplatCount, m_positionBuffer, m_orderBuffer);

            m_propertyBlock ??= new MaterialPropertyBlock();
            m_propertyBlock.SetInt(k_splatCount, (int)GsplatAsset.SplatCount);
            m_propertyBlock.SetBuffer(k_orderBuffer, m_orderBuffer);
            m_propertyBlock.SetBuffer(k_positionBuffer, m_positionBuffer);
            m_propertyBlock.SetBuffer(k_scaleBuffer, m_scaleBuffer);
            m_propertyBlock.SetBuffer(k_rotationBuffer, m_rotationBuffer);
            m_propertyBlock.SetBuffer(k_colorBuffer, m_colorBuffer);
            if (GsplatAsset.SHBands > 0)
                m_propertyBlock.SetBuffer(k_shBuffer, m_shBuffer);
        }

        void DisposeResourcesForAsset()
        {
            m_positionBuffer?.Dispose();
            m_scaleBuffer?.Dispose();
            m_rotationBuffer?.Dispose();
            m_colorBuffer?.Dispose();
            m_shBuffer?.Dispose();
            m_orderBuffer?.Dispose();
            m_sorterResource?.Dispose();

            m_positionBuffer = null;
            m_scaleBuffer = null;
            m_rotationBuffer = null;
            m_colorBuffer = null;
            m_shBuffer = null;
            m_orderBuffer = null;
        }


        void OnEnable()
        {
            GsplatSorter.Instance.RegisterGsplat(this);
            CreateResourcesForAsset();
        }

        void OnDisable()
        {
            GsplatSorter.Instance.UnregisterGsplat(this);
            DisposeResourcesForAsset();
        }

        void Update()
        {
            if (m_prevAsset != GsplatAsset)
            {
                m_prevAsset = GsplatAsset;
                DisposeResourcesForAsset();
                CreateResourcesForAsset();
            }

            if (!Valid || !GsplatSettings.Instance.Valid || !GsplatSorter.Instance.Valid)
                return;

            m_propertyBlock.SetInteger(k_gammaToLinear, GammaToLinear ? 1 : 0);
            m_propertyBlock.SetInteger(k_splatInstanceSize, (int)GsplatSettings.Instance.SplatInstanceSize);
            m_propertyBlock.SetMatrix(k_matrixM, transform.localToWorldMatrix);
            var rp = new RenderParams(GsplatSettings.Instance.Materials[GsplatAsset.SHBands])
            {
                worldBounds = GsplatUtils.CalcWorldBounds(GsplatAsset.Bounds, transform),
                matProps = m_propertyBlock,
                layer = gameObject.layer
            };

            Graphics.RenderMeshPrimitives(rp, GsplatSettings.Instance.Mesh, 0,
                (int)Math.Ceiling(GsplatAsset.SplatCount / (double)GsplatSettings.Instance.SplatInstanceSize));
        }
    }
}