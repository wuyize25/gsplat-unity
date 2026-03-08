// Copyright (c) 2026 Arthur Aillet, Yize Wu
// SPDX-License-Identifier: MIT

using System;
using System.IO;
using System.Runtime.InteropServices;
using System.Threading.Tasks;
using Unity.Mathematics;
using UnityEngine;
using UnityEngine.Rendering;


namespace Gsplat
{
    /// <summary>
    /// Implementation taken from SparkJS
    ///
    /// A PackedSplats is a collection of Gaussian splats, packed into a format that
    /// takes exactly 16 bytes per Gsplat to maximize memory and cache efficiency.
    /// The center xyz coordinates are encoded as float16 (3 x 2 bytes), scale xyz
    /// as 3 x uint8 that encode a log scale from e^-12 to e^9, rgba as 4 x uint8,
    /// and quaternion encoded via axis+angle using 2 x uint8 for Octahedral encoding
    /// of the axis direction and a uint8 to encode rotation amount from 0..Pi.
    ///
    /// | Offset (bytes) | Field           | Size (bytes) | Description                                                |
    /// |----------------|-----------------|--------------|------------------------------------------------------------|
    /// | 0              | R               | 1            | Red color channel (uint8 0–255 → 0.0–1.0)                  |
    /// | 1              | G               | 1            | Green color channel (uint8 0–255 → 0.0–1.0)                |
    /// | 2              | B               | 1            | Blue color channel (uint8 0–255 → 0.0–1.0)                 |
    /// | 3              | A               | 1            | Alpha (opacity) channel (uint8 0–255 → 0.0–1.0)            |
    /// | 4–5            | center.x        | 2            | X coordinate of splat center (float16)                     |
    /// | 6–7            | center.y        | 2            | Y coordinate of splat center (float16)                     |
    /// | 8–9            | center.z        | 2            | Z coordinate of splat center (float16)                     |
    /// | 10             | quat oct.U      | 1            | Octahedral quaternion U component (uint8)                  |
    /// | 11             | quat oct.V      | 1            | Octahedral quaternion V component (uint8)                  |
    /// | 12             | scale.x         | 1            | X scale, log-encoded to uint8                              |
    /// | 13             | scale.y         | 1            | Y scale, log-encoded to uint8                              |
    /// | 14             | scale.z         | 1            | Z scale, log-encoded to uint8                              |
    /// | 15             | quat angle (θ)  | 1            | Encoded quaternion rotation angle (uint8, θ/π·255)         |
    /// </summary>
    public class GsplatAssetSpark : GsplatAsset
    {
        public override CompressionMode Compression => CompressionMode.Spark;

        [HideInInspector] public Vector3[] SHs;
        [HideInInspector] public uint4[] PackedSplats;

        static readonly int k_packedSplatsBuffer = Shader.PropertyToID("_PackedSplatsBuffer");
        static readonly int k_shBuffer = Shader.PropertyToID("_SHBuffer");
        static readonly int k_splatCount = Shader.PropertyToID("_SplatCount");
        static readonly int k_matrixMv = Shader.PropertyToID("_MatrixMV");
        static readonly int k_depthBuffer = Shader.PropertyToID("_DepthBuffer");
        static readonly int k_orderBuffer = Shader.PropertyToID("_OrderBuffer");

        public override void Allocate()
        {
            PackedSplats = new uint4[SplatCount];
            if (SHBands > 0)
                SHs = new Vector3[SplatCount * GsplatUtils.SHBandsToCoefficientCount(SHBands)];
        }

        public override GsplatResource CreateResource()
        {
            return new GsplatResourceSpark(SplatCount, SHBands);
        }

        protected override void _UploadData(GsplatResource resource)
        {
            var res = (GsplatResourceSpark)resource;
            res.PackedSplatsBuffer.SetData(PackedSplats);
            if (SHBands > 0)
                res.SHBuffer.SetData(SHs);
        }

        protected override async Task _UploadDataAsync(GsplatResource resource)
        {
            var res = (GsplatResourceSpark)resource;
            while (res.UploadedCount < SplatCount)
            {
                var batchSize = (int)Math.Min(GsplatSettings.Instance.UploadBatchSize, SplatCount - res.UploadedCount);
                res.PackedSplatsBuffer.SetData(PackedSplats, (int)res.UploadedCount, (int)res.UploadedCount, batchSize);

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
            var res = (GsplatResourceSpark)resource;
            propertyBlock.SetBuffer(k_packedSplatsBuffer, res.PackedSplatsBuffer);
            if (SHBands > 0)
                propertyBlock.SetBuffer(k_shBuffer, res.SHBuffer);
        }

        public override void ComputeDepth(GsplatMaterial material, CommandBuffer cmd, Matrix4x4 matrixMv,
            ISorterResource sorterResource, GsplatResource resource)
        {
            var res = (GsplatResourceSpark)resource;
            var cs = material.CalcDepthShader;
            const int kernelCalcDepthSpark = 0;
            cmd.SetComputeIntParam(cs, k_splatCount, (int)SplatCount);
            cmd.SetComputeMatrixParam(cs, k_matrixMv, matrixMv);
            cmd.SetComputeBufferParam(cs, kernelCalcDepthSpark, k_packedSplatsBuffer, res.PackedSplatsBuffer);
            cmd.SetComputeBufferParam(cs, kernelCalcDepthSpark, k_depthBuffer, sorterResource.InputKeys);
            cmd.SetComputeBufferParam(cs, kernelCalcDepthSpark, k_orderBuffer, sorterResource.OrderBuffer);
            cmd.DispatchCompute(cs, kernelCalcDepthSpark, (int)GsplatUtils.DivRoundUp(SplatCount, 1024), 1, 1);
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
                for (var j = 0; j < shCoeffs; j++)
                    SHs[i * shCoeffs + j] = new Vector3(
                        properties[j + plyInfo.SHOffset],
                        properties[j + plyInfo.SHOffset + shCoeffs],
                        properties[j + plyInfo.SHOffset + shCoeffs * 2]);

                var color = new Vector4(
                    properties[plyInfo.ColorOffset],
                    properties[plyInfo.ColorOffset + 1],
                    properties[plyInfo.ColorOffset + 2],
                    properties[plyInfo.OpacityOffset]);

                var position = new Vector3(
                    properties[plyInfo.PositionOffset],
                    properties[plyInfo.PositionOffset + 1],
                    properties[plyInfo.PositionOffset + 2]);

                if (i == 0) Bounds = new Bounds(position, Vector3.zero);
                else Bounds.Encapsulate(position);

                var scale = new Vector3(
                    properties[plyInfo.ScaleOffset],
                    properties[plyInfo.ScaleOffset + 1],
                    properties[plyInfo.ScaleOffset + 2]);

                var rotation = new Quaternion(properties[plyInfo.RotationOffset],
                    properties[plyInfo.RotationOffset + 1],
                    properties[plyInfo.RotationOffset + 2],
                    properties[plyInfo.RotationOffset + 3]);

                PackedSplats[i] = PackSplat(color, position, scale, rotation);
                progressCallback?.Invoke("Reading vertices", i / (float)plyInfo.VertexCount);
            }
        }

        /// <summary>
        /// Copied from SparkJS encodeQuatXyz888 implementation
        /// Encode a Quaternion into 3 8-bit integer, converting the xyz coordinates
        /// to signed 8-bit integers (w can be derived from xyz), and flipping the sign
        /// of the quaternion if necessary to make this possible (q == -q for quaternions).
        /// </summary>
        static (byte, byte, byte) EncodeQuatXyz888(Quaternion quaternion)
        {
            bool negQuat = quaternion.w < 0.0;
            sbyte iQuatX = GsplatUtils.FloatToSByte(negQuat ? -quaternion.x : quaternion.x);
            sbyte iQuatY = GsplatUtils.FloatToSByte(negQuat ? -quaternion.y : quaternion.y);
            sbyte iQuatZ = GsplatUtils.FloatToSByte(negQuat ? -quaternion.z : quaternion.z);
            byte uQuatX = (byte)(iQuatX & 0xff);
            byte uQuatY = (byte)(iQuatY & 0xff);
            byte uQuatZ = (byte)(iQuatZ & 0xff);
            return (uQuatX, uQuatY, uQuatZ);
        }

        /// <summary>
        /// Copied from SparkJS decodeQuatXyz888 implementation
        /// Decode a 24-bit integer of the quaternion's xyz coordinates into a THREE.Quaternion.
        /// </summary>
        static Quaternion DecodeQuatXyz888(uint encoded)
        {
            uint iQuatX = (encoded << 24) >> 24;
            uint iQuatY = (encoded << 16) >> 24;
            uint iQuatZ = (encoded << 8) >> 24;
            Quaternion quat = new(iQuatX / 127.0f, iQuatY / 127.0f, iQuatZ / 127.0f, 0.0f);
            float dotSelf = quat.x * quat.x + quat.y * quat.y + quat.z * quat.z;
            quat.w = (float)Math.Sqrt(Math.Max(0.0f, 1.0f - dotSelf));
            return quat;
        }

        /// <summary>
        /// Copied from SparkJS encodeQuatOctXy88R8 implementation
        /// Encodes a THREE.Quaternion into a 24‐bit integer.
        ///
        /// Bit layout (LSB → MSB):
        ///   - Bits  0–7:  quantized U (8 bits)
        ///   - Bits  8–15: quantized V (8 bits)
        ///   - Bits 16–23: quantized angle θ (8 bits) from [0,π]
        ///
        /// This version uses folded octahedral mapping (all inline).
        /// </summary>
        public static (byte, byte, byte) EncodeQuatOctXy88R8(Quaternion quat)
        {
            // Force the minimal representation (quat.w >= 0)
            quat.Normalize();
            if (quat.w < 0)
            {
                quat.Set(-quat.x, -quat.y, -quat.z, -quat.w);
            }

            // Compute the rotation angle θ in [0, π]
            double theta = 2.0 * Math.Acos(quat.w);

            // Recover the rotation axis (default to (1,0,0) for near-zero rotation)
            float xyz_norm = (float)Math.Sqrt(quat.x * quat.x + quat.y * quat.y + quat.z * quat.z);
            Vector3 axis = xyz_norm < 1e-6 ? new Vector3(1, 0, 0) : (new Vector3(quat.x, quat.y, quat.z) / xyz_norm);

            // --- Folded Octahedral Mapping (inline) ---
            // Compute p = (axis.x, axis.y) / (|axis.x|+|axis.y|+|axis.z|)
            float sum = Math.Abs(axis.x) + Math.Abs(axis.y) + Math.Abs(axis.z);
            float p_x = axis.x / sum;
            float p_y = axis.y / sum;

            // Fold the lower hemisphere.
            if (axis.z < 0)
            {
                float tmp = p_x;
                p_x = (1.0f - Math.Abs(p_y)) * (p_x > 0.0f ? 1.0f : -1.0f);
                p_y = (1.0f - Math.Abs(tmp)) * (p_y > 0.0f ? 1.0f : -1.0f);
            }

            // Remap from [-1,1] to [0,1]
            float u_f = p_x * 0.5f + 0.5f;
            float v_f = p_y * 0.5f + 0.5f;

            // Quantize to 7 bits (0..127)
            byte quantU = (byte)((uint)Math.Round(u_f * 255.0f) & 0xff);
            byte quantV = (byte)((uint)Math.Round(v_f * 255.0f) & 0xff);

            // --- Angle Quantization: Quantize θ ∈ [0,π] to 10 bits (0..1023) ---
            byte angleInt = (byte)((uint)Math.Round(theta * (255 / Math.PI)) & 0xff);

            // Pack into 24 bits: bits [0–7]: quantU, [8–15]: quantV, [16–23]: angleInt.
            return (quantU, quantV, angleInt);
        }

        const float LN_SCALE_MIN = -12.0f;
        const float LN_SCALE_MAX = 9.0f;
        const float LN_SCALE_ZERO = -30.0f;
        const float LN_SCALE_SCALE = 254.0f / (LN_SCALE_MAX - LN_SCALE_MIN);
        static readonly float SCALE_ZERO = (float)Math.Exp(LN_SCALE_ZERO);

        static byte EncodeScaleOnLogScale(float scale)
        {
            if (scale < SCALE_ZERO)
            {
                return 0;
            }

            var encoded = ((Math.Log(scale) - LN_SCALE_MIN) * LN_SCALE_SCALE) + 1;
            return (byte)Math.Min(255, Math.Max(1, Math.Round(encoded)));
        }

        const float shC0 = 0.28209479177387814f;

        public static uint4 PackSplat(Vector4 color, Vector3 position, Vector3 scale, Quaternion rotation)
        {
            byte uR = GsplatUtils.FloatToByte(color.x * shC0 + 0.5f);
            byte uG = GsplatUtils.FloatToByte(color.y * shC0 + 0.5f);
            byte uB = GsplatUtils.FloatToByte(color.z * shC0 + 0.5f);
            byte uA = GsplatUtils.FloatToByte(GsplatUtils.Sigmoid(color.w));

            ushort uPosX = Mathf.FloatToHalf(position.x);
            ushort uPosY = Mathf.FloatToHalf(position.y);
            ushort uPosZ = Mathf.FloatToHalf(position.z);

            (byte quantU, byte quantV, byte angleInt) = EncodeQuatOctXy88R8(rotation);

            byte uScaleX = EncodeScaleOnLogScale(Mathf.Exp(scale.x));
            byte uScaleY = EncodeScaleOnLogScale(Mathf.Exp(scale.y));
            byte uScaleZ = EncodeScaleOnLogScale(Mathf.Exp(scale.z));

            return new uint4
            (
                uR | (uint)(uG << 8) | (uint)(uB << 16) | (uint)(uA << 24),
                uPosX | (uint)(uPosY << 16),
                uPosZ | (uint)(quantU << 16) | (uint)(quantV << 24),
                uScaleX | (uint)(uScaleY << 8) | (uint)(uScaleZ << 16) | (uint)(angleInt << 24)
            );
        }
    }
}