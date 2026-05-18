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

        public override GsplatResource CreateResource()
        {
            return new GsplatResourceUncompressed(SplatCount, SHBands);
        }

        protected override void _UploadData(GsplatResource resource)
        {
            var res = (GsplatResourceUncompressed)resource;
            res.PositionBuffer.SetData(Positions);
            res.ScaleBuffer.SetData(Scales);
            res.RotationBuffer.SetData(Rotations);
            res.ColorBuffer.SetData(Colors);
            if (SHBands > 0)
                res.SHBuffer.SetData(SHs);
        }

        protected override async Task _UploadDataAsync(GsplatResource resource)
        {
            var res = (GsplatResourceUncompressed)resource;
            while (res.UploadedCount < SplatCount)
            {
                var batchSize = (int)Math.Min(GsplatSettings.Instance.UploadBatchSize, SplatCount - res.UploadedCount);
                res.PositionBuffer.SetData(Positions, (int)res.UploadedCount, (int)res.UploadedCount, batchSize);
                res.ScaleBuffer.SetData(Scales, (int)res.UploadedCount, (int)res.UploadedCount, batchSize);
                res.RotationBuffer.SetData(Rotations, (int)res.UploadedCount, (int)res.UploadedCount, batchSize);
                res.ColorBuffer.SetData(Colors, (int)res.UploadedCount, (int)res.UploadedCount, batchSize);

                if (SHBands > 0)
                {
                    var coefficientCount = GsplatUtils.SHBandsToCoefficientCount(SHBands);
                    res.SHBuffer.SetData(SHs, coefficientCount * (int)res.UploadedCount,
                        coefficientCount * (int)res.UploadedCount, coefficientCount * batchSize);
                }

                res.UploadedCount += (uint)batchSize;
                await Task.Yield();
            }
        }

        public override void SetupMaterialPropertyBlock(MaterialPropertyBlock propertyBlock,
            GsplatResource resource)
        {
            var cs = GsplatMaterial.InitOrderShader;
            m_kernelInitOrder = cs.FindKernel("InitOrder");

            var res = (GsplatResourceUncompressed)resource;
            propertyBlock.SetBuffer(k_positionBuffer, res.PositionBuffer);
            propertyBlock.SetBuffer(k_scaleBuffer, res.ScaleBuffer);
            propertyBlock.SetBuffer(k_rotationBuffer, res.RotationBuffer);
            propertyBlock.SetBuffer(k_colorBuffer, res.ColorBuffer);
            if (SHBands > 0)
                propertyBlock.SetBuffer(k_shBuffer, res.SHBuffer);
        }

        public override void ComputeDepth(CommandBuffer cmd, Matrix4x4 matrixMv,
            ISorterResource sorterResource, GsplatResource resource)
        {
            var res = (GsplatResourceUncompressed)resource;
            var cs = GsplatMaterial.CalcDepthShader;
            var kernelCalcDepth = 0;
            cmd.SetComputeIntParam(cs, k_splatCount, (int)res.UploadedCount);
            cmd.SetComputeMatrixParam(cs, k_matrixMv, matrixMv);
            cmd.SetComputeBufferParam(cs, kernelCalcDepth, k_positionBuffer, res.PositionBuffer);
            cmd.SetComputeBufferParam(cs, kernelCalcDepth, k_depthBuffer, sorterResource.InputKeys);
            cmd.SetComputeBufferParam(cs, kernelCalcDepth, k_orderBuffer, sorterResource.OrderBuffer);
            cmd.DispatchCompute(cs, kernelCalcDepth, (int)GsplatUtils.DivRoundUp(res.UploadedCount, 1024), 1, 1);
        }

        public override void InitOrder(ISorterResource sorterResource, GsplatResource resource, bool updateBounds)
        {
            var cs = GsplatMaterial.InitOrderShader;
            var res = (GsplatResourceUncompressed)resource;
            sorterResource.OrderBuffer.SetCounterValue(0);
            cs.SetInt(k_splatCount, (int)res.UploadedCount);
            cs.SetBuffer(m_kernelInitOrder, k_orderBuffer, sorterResource.OrderBuffer);
            cs.SetBuffer(m_kernelInitOrder, k_positionBuffer, res.PositionBuffer);
            if (updateBounds)
                cs.EnableKeyword("UPDATE_BOUNDS");
            else
                cs.DisableKeyword("UPDATE_BOUNDS");
            cs.Dispatch(m_kernelInitOrder, (int)GsplatUtils.DivRoundUp(res.UploadedCount, 1024), 1, 1);
        }

        public override void LoadFromPly(string plyPath, ProgressCallback progressCallback = null,
            SourceCoordinates sourceCoordinates = SourceCoordinates.RUF)
        {
            using var fs = new FileStream(plyPath, FileMode.Open, FileAccess.Read);
            // C# arrays and NativeArrays make it hard to have a "byte" array larger than 2GB :/
            if (fs.Length >= 2 * 1024 * 1024 * 1024L)
                throw new NotSupportedException("currently files larger than 2GB are not supported");

            var plyInfo = new PlyHeaderInfo(fs);
            var shCoeffs = plyInfo.SHPropertyCount / 3;
            SplatCount = plyInfo.VertexCount;
            SHBands = GsplatUtils.CalcSHBandsFromSHPropertyCount(plyInfo.SHPropertyCount);

            if (SHBands > 4 || GsplatUtils.SHBandsToCoefficientCount(SHBands) * 3 != plyInfo.SHPropertyCount)
                throw new NotSupportedException($"unexpected SH property count {plyInfo.SHPropertyCount}");

            if (plyInfo.PositionOffset == -1 || plyInfo.ColorOffset == -1 || plyInfo.OpacityOffset == -1 ||
                plyInfo.ScaleOffset == -1 || plyInfo.RotationOffset == -1)
                throw new NotSupportedException("missing required properties in PLY header");

            var (posXSign, posYSign, posZSign) = GsplatUtils.AxisSigns(sourceCoordinates);
            float rotXSign = posYSign * posZSign;
            float rotYSign = posXSign * posZSign;
            float rotZSign = posXSign * posYSign;

            Allocate();
            var buffer = new byte[plyInfo.PropertyCount * sizeof(float)];
            for (uint i = 0; i < plyInfo.VertexCount; i++)
            {
                var readBytes = fs.Read(buffer);
                if (readBytes != buffer.Length)
                    throw new EndOfStreamException($"unexpected end of file, got {readBytes} bytes at vertex {i}");

                var properties = MemoryMarshal.Cast<byte, float>(buffer);
                Positions[i] = new Vector3(
                    posXSign * properties[plyInfo.PositionOffset],
                    posYSign * properties[plyInfo.PositionOffset + 1],
                    posZSign * properties[plyInfo.PositionOffset + 2]);
                Colors[i] = new Vector4(
                    properties[plyInfo.ColorOffset],
                    properties[plyInfo.ColorOffset + 1],
                    properties[plyInfo.ColorOffset + 2],
                    GsplatUtils.Sigmoid(properties[plyInfo.OpacityOffset]));

                for (int j = 0, bandOffset = 0; j < SHBands; j++)
                {
                    int bandSize = (j + 1) * 2 + 1; // band l = j+1 has 2l+1 coefficients
                    for (int k = 0; k < bandSize; k++)
                    {
                        float sign = GsplatUtils.ShSign(sourceCoordinates, j + 1, k);
                        int idx = (int)i * shCoeffs + bandOffset + k;
                        SHs[idx] = sign * new Vector3(
                            properties[bandOffset + k + plyInfo.SHOffset],
                            properties[bandOffset + k + plyInfo.SHOffset + shCoeffs],
                            properties[bandOffset + k + plyInfo.SHOffset + shCoeffs * 2]);
                    }
                    bandOffset += bandSize;
                }

                Scales[i] = new Vector3(
                    Mathf.Exp(properties[plyInfo.ScaleOffset]),
                    Mathf.Exp(properties[plyInfo.ScaleOffset + 1]),
                    Mathf.Exp(properties[plyInfo.ScaleOffset + 2]));
                Rotations[i] = new Vector4(
                    properties[plyInfo.RotationOffset],
                    rotXSign * properties[plyInfo.RotationOffset + 1],
                    rotYSign * properties[plyInfo.RotationOffset + 2],
                    rotZSign * properties[plyInfo.RotationOffset + 3]).normalized;

                if (i == 0) Bounds = new Bounds(Positions[i], Vector3.zero);
                else Bounds.Encapsulate(Positions[i]);

                progressCallback?.Invoke("Reading vertices", i / (float)plyInfo.VertexCount);
            }
        }
    }
}
