// Copyright (c) 2025 Yize Wu
// SPDX-License-Identifier: MIT

using System;
using UnityEngine;

namespace Gsplat
{
    public class GsplatRendererImpl
    {
        public uint SplatCount { get; private set; }
        public byte SHBands { get; private set; }

        MaterialPropertyBlock m_propertyBlock;
        public GraphicsBuffer PackedSplatsBuffer { get; private set; }
        public GraphicsBuffer PackedSH1Buffer { get; private set; }
        public GraphicsBuffer PackedSH2Buffer { get; private set; }
        public GraphicsBuffer PackedSH3Buffer { get; private set; }
        public GraphicsBuffer OrderBuffer { get; private set; }
        public ISorterResource SorterResource { get; private set; }

        public bool Valid =>
            PackedSplatsBuffer != null &&
            (SHBands == 0 ||
            (SHBands == 1 && PackedSH1Buffer != null) ||
            (SHBands == 2 && PackedSH1Buffer != null && PackedSH2Buffer != null) ||
            (SHBands == 3 && PackedSH1Buffer != null && PackedSH2Buffer != null && PackedSH3Buffer != null));

        static readonly int k_orderBuffer = Shader.PropertyToID("_OrderBuffer");
        static readonly int k_packedSplatsBuffer = Shader.PropertyToID("_PackedSplatsBuffer");
        static readonly int k_packedSH1Buffer = Shader.PropertyToID("_PackedSH1Buffer");
        static readonly int k_packedSH2Buffer = Shader.PropertyToID("_PackedSH2Buffer");
        static readonly int k_packedSH3Buffer = Shader.PropertyToID("_PackedSH3Buffer");
        static readonly int k_matrixM = Shader.PropertyToID("_MATRIX_M");
        static readonly int k_splatInstanceSize = Shader.PropertyToID("_SplatInstanceSize");
        static readonly int k_splatCount = Shader.PropertyToID("_SplatCount");
        static readonly int k_gammaToLinear = Shader.PropertyToID("_GammaToLinear");

        public GsplatRendererImpl(uint splatCount, byte shBands)
        {
            SplatCount = splatCount;
            SHBands = shBands;
            CreateResources(splatCount);
            CreatePropertyBlock();
        }

        public void RecreateResources(uint splatCount, byte shBands)
        {
            if (SplatCount == splatCount && SHBands == shBands)
                return;
            Dispose();
            SplatCount = splatCount;
            SHBands = shBands;
            CreateResources(splatCount);
            CreatePropertyBlock();
        }

        void CreateResources(uint splatCount)
        {
            PackedSplatsBuffer = new GraphicsBuffer(GraphicsBuffer.Target.Structured, (int)splatCount,
                System.Runtime.InteropServices.Marshal.SizeOf(typeof(uint)) * 4);
            if (SHBands >= 1)
                PackedSH1Buffer = new GraphicsBuffer(GraphicsBuffer.Target.Structured, (int)SplatCount,
                    System.Runtime.InteropServices.Marshal.SizeOf(typeof(uint)) * 2);
            if (SHBands >= 2)
                PackedSH2Buffer = new GraphicsBuffer(GraphicsBuffer.Target.Structured, (int)SplatCount,
                    System.Runtime.InteropServices.Marshal.SizeOf(typeof(uint)) * 4);
            if (SHBands == 3)
                PackedSH3Buffer = new GraphicsBuffer(GraphicsBuffer.Target.Structured, (int)SplatCount,
                    System.Runtime.InteropServices.Marshal.SizeOf(typeof(uint)) * 4);

            OrderBuffer = new GraphicsBuffer(GraphicsBuffer.Target.Structured, (int)splatCount, sizeof(uint));

            SorterResource = GsplatSorter.Instance.CreateSorterResource(splatCount, PackedSplatsBuffer, OrderBuffer);
        }

        void CreatePropertyBlock()
        {
            m_propertyBlock ??= new MaterialPropertyBlock();
            m_propertyBlock.SetBuffer(k_packedSplatsBuffer, PackedSplatsBuffer);
            m_propertyBlock.SetBuffer(k_orderBuffer, OrderBuffer);

            if (SHBands >= 1)
                m_propertyBlock.SetBuffer(k_packedSH1Buffer, PackedSH1Buffer);
            if (SHBands >= 2)
                m_propertyBlock.SetBuffer(k_packedSH2Buffer, PackedSH2Buffer);
            if (SHBands == 3)
                m_propertyBlock.SetBuffer(k_packedSH3Buffer, PackedSH3Buffer);
        }

        public void Dispose()
        {
            PackedSplatsBuffer?.Dispose();
            PackedSH1Buffer?.Dispose();
            PackedSH2Buffer?.Dispose();
            PackedSH3Buffer?.Dispose();
            OrderBuffer?.Dispose();
            SorterResource?.Dispose();

            PackedSplatsBuffer = null;
            PackedSH1Buffer = null;
            PackedSH2Buffer = null;
            PackedSH3Buffer = null;
            OrderBuffer = null;
        }

        /// <summary>
        /// Render the splats.
        /// </summary>
        /// <param name="splatCount">It can be less than or equal to the SplatCount property.</param>
        /// <param name="transform">Object transform.</param>
        /// <param name="localBounds">Bounding box in object space.</param>
        /// <param name="layer">Layer used for rendering.</param>
        /// <param name="gammaToLinear">Covert color space from Gamma to Linear.</param>
        /// <param name="shDegree">Order of SH coefficients used for rendering. The final value is capped by the SHBands property.</param>
        public void Render(uint splatCount, Transform transform, Bounds localBounds, int layer,
            bool gammaToLinear = false, int shDegree = 3)
        {
            if (!Valid || !GsplatSettings.Instance.Valid || !GsplatSorter.Instance.Valid)
                return;

            m_propertyBlock.SetInteger(k_splatCount, (int)splatCount);
            m_propertyBlock.SetInteger(k_gammaToLinear, gammaToLinear ? 1 : 0);
            m_propertyBlock.SetInteger(k_splatInstanceSize, (int)GsplatSettings.Instance.SplatInstanceSize);
            m_propertyBlock.SetMatrix(k_matrixM, transform.localToWorldMatrix);
            var rp = new RenderParams(GsplatSettings.Instance.Materials[Math.Min(SHBands, shDegree)])
            {
                worldBounds = GsplatUtils.CalcWorldBounds(localBounds, transform),
                matProps = m_propertyBlock,
                layer = layer
            };

            Graphics.RenderMeshPrimitives(rp, GsplatSettings.Instance.Mesh, 0,
                Mathf.CeilToInt(splatCount / (float)GsplatSettings.Instance.SplatInstanceSize));
        }
    }
}
