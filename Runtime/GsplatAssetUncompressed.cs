// Copyright (c) 2025 Yize Wu
// SPDX-License-Identifier: MIT

using System;
using System.IO;
using System.Runtime.InteropServices;
using System.Threading.Tasks;
using UnityEngine;
using UnityEngine.Rendering;

namespace Gsplat
{
    public class GsplatAssetUncompressed : GsplatAsset
    {
        public override CompressionMode Compression => CompressionMode.Uncompressed;

        [HideInInspector] public Vector3[] Positions;
        [HideInInspector] public Vector4[] Colors; // RGB, Opacity
        [HideInInspector] public Vector3[] SHs;
        [HideInInspector] public Vector3[] Scales;
        [HideInInspector] public Vector4[] Rotations; // Quaternion, wxyz

        public GraphicsBuffer PositionBuffer { get; private set; }
        public GraphicsBuffer ScaleBuffer { get; private set; }
        public GraphicsBuffer RotationBuffer { get; private set; }
        public GraphicsBuffer ColorBuffer { get; private set; }
        public GraphicsBuffer SHBuffer { get; private set; }

        static readonly int k_positionBuffer = Shader.PropertyToID("_PositionBuffer");
        static readonly int k_scaleBuffer = Shader.PropertyToID("_ScaleBuffer");
        static readonly int k_rotationBuffer = Shader.PropertyToID("_RotationBuffer");
        static readonly int k_colorBuffer = Shader.PropertyToID("_ColorBuffer");
        static readonly int k_shBuffer = Shader.PropertyToID("_SHBuffer");
        static readonly int k_splatCount = Shader.PropertyToID("_SplatCount");
        static readonly int k_matrixMv = Shader.PropertyToID("_MatrixMV");
        static readonly int k_depthBuffer = Shader.PropertyToID("_DepthBuffer");
        static readonly int k_orderBuffer = Shader.PropertyToID("_OrderBuffer");

        public override void Allocate()
        {
            Positions = new Vector3[SplatCount];
            Colors = new Vector4[SplatCount];
            Scales = new Vector3[SplatCount];
            Rotations = new Vector4[SplatCount];
            if (SHBands > 0)
                SHs = new Vector3[SplatCount * GsplatUtils.SHBandsToCoefficientCount(SHBands)];
        }

        protected override void AllocateGPU()
        {
            if (SplatCount == 0)
                return;
            PositionBuffer = new GraphicsBuffer(GraphicsBuffer.Target.Structured, (int)SplatCount,
                Marshal.SizeOf(typeof(Vector3)));
            ScaleBuffer = new GraphicsBuffer(GraphicsBuffer.Target.Structured, (int)SplatCount,
                Marshal.SizeOf(typeof(Vector3)));
            RotationBuffer = new GraphicsBuffer(GraphicsBuffer.Target.Structured, (int)SplatCount,
                Marshal.SizeOf(typeof(Vector4)));
            ColorBuffer = new GraphicsBuffer(GraphicsBuffer.Target.Structured, (int)SplatCount,
                Marshal.SizeOf(typeof(Vector4)));
            if (SHBands > 0)
                SHBuffer = new GraphicsBuffer(GraphicsBuffer.Target.Structured,
                    GsplatUtils.SHBandsToCoefficientCount(SHBands) * (int)SplatCount, Marshal.SizeOf(typeof(Vector3)));
        }

        protected override void ReleaseGPU()
        {
            PositionBuffer?.Dispose();
            PositionBuffer = null;
            ScaleBuffer?.Dispose();
            ScaleBuffer = null;
            RotationBuffer?.Dispose();
            RotationBuffer = null;
            ColorBuffer?.Dispose();
            ColorBuffer = null;
            SHBuffer?.Dispose();
            SHBuffer = null;
        }

        protected override void _UploadData()
        {
            PositionBuffer.SetData(Positions);
            ScaleBuffer.SetData(Scales);
            RotationBuffer.SetData(Rotations);
            ColorBuffer.SetData(Colors);
            if (SHBands > 0)
                SHBuffer.SetData(SHs);
        }

        protected override async Task _UploadDataAsync()
        {
            while (UploadedCount < SplatCount)
            {
                var batchSize = (int)Math.Min(GsplatSettings.Instance.UploadBatchSize, SplatCount - UploadedCount);
                PositionBuffer.SetData(Positions, (int)UploadedCount, (int)UploadedCount, batchSize);
                ScaleBuffer.SetData(Scales, (int)UploadedCount, (int)UploadedCount, batchSize);
                RotationBuffer.SetData(Rotations, (int)UploadedCount, (int)UploadedCount, batchSize);
                ColorBuffer.SetData(Colors, (int)UploadedCount, (int)UploadedCount, batchSize);

                if (SHBands > 0)
                {
                    var coefficientCount = GsplatUtils.SHBandsToCoefficientCount(SHBands);
                    SHBuffer.SetData(SHs, coefficientCount * (int)UploadedCount,
                        coefficientCount * (int)UploadedCount, coefficientCount * batchSize);
                }

                UploadedCount += (uint)batchSize;
                await Task.Yield();
            }
        }

        public override void SetupMaterialPropertyBlock(MaterialPropertyBlock propertyBlock)
        {
            propertyBlock.SetBuffer(k_positionBuffer, PositionBuffer);
            propertyBlock.SetBuffer(k_scaleBuffer, ScaleBuffer);
            propertyBlock.SetBuffer(k_rotationBuffer, RotationBuffer);
            propertyBlock.SetBuffer(k_colorBuffer, ColorBuffer);
            if (SHBands > 0)
                propertyBlock.SetBuffer(k_shBuffer, SHBuffer);
        }

        public override void ComputeDepth(GsplatMaterial material, CommandBuffer cmd, Matrix4x4 matrixMv, ISorterResource sorterResource)
        {
            var cs = material.CalcDepthShader;
            var kernelCalcDepth = 0;
            cmd.SetComputeIntParam(cs, k_splatCount, (int)SplatCount);
            cmd.SetComputeMatrixParam(cs, k_matrixMv, matrixMv);
            cmd.SetComputeBufferParam(cs, kernelCalcDepth, k_positionBuffer, PositionBuffer);
            cmd.SetComputeBufferParam(cs, kernelCalcDepth, k_depthBuffer, sorterResource.InputKeys);
            cmd.SetComputeBufferParam(cs, kernelCalcDepth, k_orderBuffer, sorterResource.OrderBuffer);
            cmd.DispatchCompute(cs, kernelCalcDepth, (int)GsplatUtils.DivRoundUp(SplatCount, 1024), 1, 1);
        }

        public override void LoadFromPly(string plyPath, ProgressCallback progressCallback = null)
        {
            using var fs = new FileStream(plyPath, FileMode.Open, FileAccess.Read);
            // C# arrays and NativeArrays make it hard to have a "byte" array larger than 2GB :/
            if (fs.Length >= 2 * 1024 * 1024 * 1024L)
                throw new NotSupportedException("currently files larger than 2GB are not supported");

            var plyInfo = new PlyHeaderInfo(fs);
            var shCoeffs = plyInfo.SHPropertyCount / 3;
            SplatCount = plyInfo.VertexCount;
            SHBands = GsplatUtils.CalcSHBandsFromSHPropertyCount(plyInfo.SHPropertyCount);

            if (SHBands > 3 || GsplatUtils.SHBandsToCoefficientCount(SHBands) * 3 != plyInfo.SHPropertyCount)
                throw new NotSupportedException($"unexpected SH property count {plyInfo.SHPropertyCount}");

            if (plyInfo.PositionOffset == -1 || plyInfo.ColorOffset == -1 || plyInfo.OpacityOffset == -1 ||
                plyInfo.ScaleOffset == -1 || plyInfo.RotationOffset == -1)
                throw new NotSupportedException("missing required properties in PLY header");

            GsplatMaterial = GsplatSettings.Instance.Materials[(int)Compression];
            Allocate();
            var buffer = new byte[plyInfo.PropertyCount * sizeof(float)];
            for (uint i = 0; i < plyInfo.VertexCount; i++)
            {
                var readBytes = fs.Read(buffer);
                if (readBytes != buffer.Length)
                    throw new EndOfStreamException($"unexpected end of file, got {readBytes} bytes at vertex {i}");

                var properties = MemoryMarshal.Cast<byte, float>(buffer);
                Positions[i] = new Vector3(
                    properties[plyInfo.PositionOffset],
                    properties[plyInfo.PositionOffset + 1],
                    properties[plyInfo.PositionOffset + 2]);
                Colors[i] = new Vector4(
                    properties[plyInfo.ColorOffset],
                    properties[plyInfo.ColorOffset + 1],
                    properties[plyInfo.ColorOffset + 2],
                    GsplatUtils.Sigmoid(properties[plyInfo.OpacityOffset]));
                for (int j = 0; j < shCoeffs; j++)
                    SHs[i * shCoeffs + j] = new Vector3(
                        properties[j + plyInfo.SHOffset],
                        properties[j + plyInfo.SHOffset + shCoeffs],
                        properties[j + plyInfo.SHOffset + shCoeffs * 2]);
                Scales[i] = new Vector3(
                    Mathf.Exp(properties[plyInfo.ScaleOffset]),
                    Mathf.Exp(properties[plyInfo.ScaleOffset + 1]),
                    Mathf.Exp(properties[plyInfo.ScaleOffset + 2]));
                Rotations[i] = new Vector4(
                    properties[plyInfo.RotationOffset],
                    properties[plyInfo.RotationOffset + 1],
                    properties[plyInfo.RotationOffset + 2],
                    properties[plyInfo.RotationOffset + 3]).normalized;

                if (i == 0) Bounds = new Bounds(Positions[i], Vector3.zero);
                else Bounds.Encapsulate(Positions[i]);

                progressCallback?.Invoke("Reading vertices", i / (float)plyInfo.VertexCount);
            }
        }
    }
}