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

        [HideInInspector] public uint[] PackedSH1;
        [HideInInspector] public uint[] PackedSH2;
        [HideInInspector] public uint[] PackedSH3;
        [HideInInspector] public uint[] PackedSH4;
        [HideInInspector] public uint4[] PackedSplats;

        static readonly int k_packedSplatsBuffer = Shader.PropertyToID("_PackedSplatsBuffer");
        static readonly int k_packedSH1Buffer = Shader.PropertyToID("_PackedSH1Buffer");
        static readonly int k_packedSH2Buffer = Shader.PropertyToID("_PackedSH2Buffer");
        static readonly int k_packedSH3Buffer = Shader.PropertyToID("_PackedSH3Buffer");
        static readonly int k_packedSH4Buffer = Shader.PropertyToID("_PackedSH4Buffer");
        static readonly int k_splatCount = Shader.PropertyToID("_SplatCount");
        static readonly int k_matrixMv = Shader.PropertyToID("_MatrixMV");
        static readonly int k_depthBuffer = Shader.PropertyToID("_DepthBuffer");
        static readonly int k_orderBuffer = Shader.PropertyToID("_OrderBuffer");

        public override void Allocate()
        {
            PackedSplats = new uint4[SplatCount];
            if (SHBands >= 1)
                PackedSH1 = new uint[SplatCount * 2];
            if (SHBands >= 2)
                PackedSH2 = new uint[SplatCount * 4];
            if (SHBands >= 3)
                PackedSH3 = new uint[SplatCount * 4];
            if (SHBands >= 4)
                PackedSH4 = new uint[SplatCount * 4];
        }

        public override GsplatResource CreateResource()
        {
            return new GsplatResourceSpark(SplatCount, SHBands);
        }

        protected override void _UploadData(GsplatResource resource)
        {
            var res = (GsplatResourceSpark)resource;
            res.PackedSplatsBuffer.SetData(PackedSplats);
            if (SHBands >= 1)
                res.PackedSH1Buffer.SetData(PackedSH1);
            if (SHBands >= 2)
                res.PackedSH2Buffer.SetData(PackedSH2);
            if (SHBands >= 3)
                res.PackedSH3Buffer.SetData(PackedSH3);
            if (SHBands >= 4)
                res.PackedSH4Buffer.SetData(PackedSH4);
        }

        protected override async Task _UploadDataAsync(GsplatResource resource)
        {
            var res = (GsplatResourceSpark)resource;
            while (res.UploadedCount < SplatCount)
            {
                var batchSize = (int)Math.Min(GsplatSettings.Instance.UploadBatchSize, SplatCount - res.UploadedCount);
                res.PackedSplatsBuffer.SetData(PackedSplats, (int)res.UploadedCount, (int)res.UploadedCount, batchSize);

                if (SHBands >= 1)
                    res.PackedSH1Buffer.SetData(PackedSH1, 2 * (int)res.UploadedCount, 2 * (int)res.UploadedCount,
                        2 * batchSize);
                if (SHBands >= 2)
                    res.PackedSH2Buffer.SetData(PackedSH2, 4 * (int)res.UploadedCount, 4 * (int)res.UploadedCount,
                        4 * batchSize);
                if (SHBands >= 3)
                    res.PackedSH3Buffer.SetData(PackedSH3, 4 * (int)res.UploadedCount, 4 * (int)res.UploadedCount,
                        4 * batchSize);
                if (SHBands >= 4)
                    res.PackedSH4Buffer.SetData(PackedSH4, 4 * (int)res.UploadedCount, 4 * (int)res.UploadedCount,
                        4 * batchSize);

                res.UploadedCount += (uint)batchSize;
                await Task.Yield();
            }
        }

        public override void SetupMaterialPropertyBlock(MaterialPropertyBlock propertyBlock,
            GsplatResource resource)
        {
            var cs = GsplatMaterial.InitOrderShader;
            m_kernelInitOrder = cs.FindKernel("InitOrder");

            var res = (GsplatResourceSpark)resource;
            propertyBlock.SetBuffer(k_packedSplatsBuffer, res.PackedSplatsBuffer);
            if (SHBands >= 1)
                propertyBlock.SetBuffer(k_packedSH1Buffer, res.PackedSH1Buffer);
            if (SHBands >= 2)
                propertyBlock.SetBuffer(k_packedSH2Buffer, res.PackedSH2Buffer);
            if (SHBands >= 3)
                propertyBlock.SetBuffer(k_packedSH3Buffer, res.PackedSH3Buffer);
            if (SHBands >= 4)
                propertyBlock.SetBuffer(k_packedSH4Buffer, res.PackedSH4Buffer);
        }

        public override void ComputeDepth(CommandBuffer cmd, Matrix4x4 matrixMv,
            ISorterResource sorterResource, GsplatResource resource)
        {
            var res = (GsplatResourceSpark)resource;
            var cs = GsplatMaterial.CalcDepthShader;
            const int kernelCalcDepthSpark = 0;
            cmd.SetComputeIntParam(cs, k_splatCount, (int)res.UploadedCount);
            cmd.SetComputeMatrixParam(cs, k_matrixMv, matrixMv);
            cmd.SetComputeBufferParam(cs, kernelCalcDepthSpark, k_packedSplatsBuffer, res.PackedSplatsBuffer);
            cmd.SetComputeBufferParam(cs, kernelCalcDepthSpark, k_depthBuffer, sorterResource.InputKeys);
            cmd.SetComputeBufferParam(cs, kernelCalcDepthSpark, k_orderBuffer, sorterResource.OrderBuffer);
            cmd.DispatchCompute(cs, kernelCalcDepthSpark, (int)GsplatUtils.DivRoundUp(res.UploadedCount, 1024), 1, 1);
        }

        public override void InitOrder(ISorterResource sorterResource, GsplatResource resource, bool updateBounds)
        {
            var cs = GsplatMaterial.InitOrderShader;
            var res = (GsplatResourceSpark)resource;
            sorterResource.OrderBuffer.SetCounterValue(0);
            cs.SetInt(k_splatCount, (int)res.UploadedCount);
            cs.SetBuffer(m_kernelInitOrder, k_orderBuffer, sorterResource.OrderBuffer);
            cs.SetBuffer(m_kernelInitOrder, k_packedSplatsBuffer, res.PackedSplatsBuffer);
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

            // Decompose source frame into per-axis sign flips relative to Unity (RUF).
            var (posXSign, posYSign, posZSign) = GsplatUtils.AxisSigns(sourceCoordinates);
            // Quaternion conjugation: each imaginary component gets the product of the OTHER two axis signs.
            float rotXSign = posYSign * posZSign;
            float rotYSign = posXSign * posZSign;
            float rotZSign = posXSign * posYSign;

            Allocate();
            var buffer = new byte[plyInfo.PropertyCount * sizeof(float)];
            var shBandData = new float[9 * 3]; // max band 4: 9 coeffs × 3 channels; reused each splat
            for (uint i = 0; i < plyInfo.VertexCount; i++)
            {
                var readBytes = fs.Read(buffer);
                if (readBytes != buffer.Length)
                    throw new EndOfStreamException($"unexpected end of file, got {readBytes} bytes at vertex {i}");

                var properties = MemoryMarshal.Cast<byte, float>(buffer);

                for (int j = 1, shReadOffset = 0; j <= SHBands; j++)
                {
                    var bandSize = j * 2 + 1;
                    for (int k = 0; k < bandSize; k++)
                    {
                        float sign = GsplatUtils.ShSign(sourceCoordinates, j, k);
                        shBandData[k * 3]     = sign * properties[shReadOffset + k + plyInfo.SHOffset];
                        shBandData[k * 3 + 1] = sign * properties[shReadOffset + k + plyInfo.SHOffset + shCoeffs];
                        shBandData[k * 3 + 2] = sign * properties[shReadOffset + k + plyInfo.SHOffset + shCoeffs * 2];
                    }

                    if (j == 1) PackSH1(shBandData, PackedSH1.AsSpan((int)i * 2, 2));
                    if (j == 2) PackSH2(shBandData, PackedSH2.AsSpan((int)i * 4, 4));
                    if (j == 3) PackSH3(shBandData, PackedSH3.AsSpan((int)i * 4, 4));
                    if (j == 4) PackSH4(shBandData, PackedSH4.AsSpan((int)i * 4, 4));

                    shReadOffset += bandSize;
                }

                var color = new Vector4(
                    properties[plyInfo.ColorOffset],
                    properties[plyInfo.ColorOffset + 1],
                    properties[plyInfo.ColorOffset + 2],
                    properties[plyInfo.OpacityOffset]);

                var position = new Vector3(
                    posXSign * properties[plyInfo.PositionOffset],
                    posYSign * properties[plyInfo.PositionOffset + 1],
                    posZSign * properties[plyInfo.PositionOffset + 2]);

                if (i == 0) Bounds = new Bounds(position, Vector3.zero);
                else Bounds.Encapsulate(position);

                var scale = new Vector3(
                    properties[plyInfo.ScaleOffset],
                    properties[plyInfo.ScaleOffset + 1],
                    properties[plyInfo.ScaleOffset + 2]);

                var rotation = new Quaternion(
                    properties[plyInfo.RotationOffset],
                    rotXSign * properties[plyInfo.RotationOffset + 1],
                    rotYSign * properties[plyInfo.RotationOffset + 2],
                    rotZSign * properties[plyInfo.RotationOffset + 3]);

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

        /// <summary>
        /// Inspired from SparkJs encodeSh1Rgb implementation
        ///
        /// Encode an array of 9 signed RGB SH1 coefficients (clamped to [-1,1]) into
        /// a pair of uint32 values, where each coefficient is stored as a sint7
        /// </summary>
        protected static void PackSH1(float[] sh, Span<uint> output)
        {
            output.Clear();
            for (var i = 0; i < 9; ++i)
            {
                float shScaled = sh[i] * 63.0f;
                int shBounded = (int)Math.Round(Math.Max(-63.0f, Math.Min(63.0f, shScaled)));
                int sint7SH = shBounded & 0x7f;

                int bitStart = i * 7;
                int bitEnd = bitStart + 7;

                int wordStart = (int)Math.Floor((double)(bitStart / 32));
                int bitOffset = bitStart - wordStart * 32;
                uint firstWord = (uint)((sint7SH << bitOffset) & 0xffffffff);
                output[wordStart] |= firstWord;

                if (bitEnd > wordStart * 32 + 32)
                {
                    uint secondWord = ((uint)sint7SH >> (32 - bitOffset)) & 0xffffffff;
                    output[wordStart + 1] |= secondWord;
                }
            }
        }

        static uint PackSint8Bytes(float b0, float b1, float b2, float b3)
        {
            sbyte clampedB0 = GsplatUtils.FloatToSByte(b0);
            sbyte clampedB1 = GsplatUtils.FloatToSByte(b1);
            sbyte clampedB2 = GsplatUtils.FloatToSByte(b2);
            sbyte clampedB3 = GsplatUtils.FloatToSByte(b3);
            byte uB0 = (byte)(clampedB0 & 0xff);
            byte uB1 = (byte)(clampedB1 & 0xff);
            byte uB2 = (byte)(clampedB2 & 0xff);
            byte uB3 = (byte)(clampedB3 & 0xff);
            return (uint)(uB0 | (uB1 << 8) | (uB2 << 16) | (uB3 << 24));
        }

        /// <summary>
        /// Inspired from SparkJs encodeSh2Rgb implementation
        ///
        /// Encode an array of 15 signed RGB SH2 coefficients (clamped to [-1,1]) into
        /// an array of 4 uint32 values, where each coefficient is stored as a sint8.
        /// </summary>
        protected static void PackSH2(float[] sh, Span<uint> output)
        {
            output[0] = PackSint8Bytes(sh[0], sh[1], sh[2], sh[3]);
            output[1] = PackSint8Bytes(sh[4], sh[5], sh[6], sh[7]);
            output[2] = PackSint8Bytes(sh[8], sh[9], sh[10], sh[11]);
            output[3] = PackSint8Bytes(sh[12], sh[13], sh[14], 0);
        }

        /// <summary>
        /// Encode an array of 27 signed RGB SH4 coefficients (clamped to [-1,1]) into
        /// an array of 4 uint32 values, where each coefficient is stored as a sint4.
        /// Matches the SPZ writer's shRestBits=4 default precision so there is no
        /// information loss vs the source SPZ data.
        /// </summary>
        protected static void PackSH4(float[] sh, Span<uint> output)
        {
            output.Clear();
            for (var i = 0; i < 27; ++i)
            {
                float shScaled = sh[i] * 7.0f;
                int shBounded = (int)Math.Round(Math.Max(-7.0f, Math.Min(7.0f, shScaled)));
                int sint4SH = shBounded & 0x0f;
                int bitStart = i * 4;
                int wordStart = bitStart / 32;
                int bitOffset = bitStart - wordStart * 32;
                output[wordStart] |= (uint)(sint4SH << bitOffset);
                // With 4-bit values and 32-bit words no value straddles a boundary
                // (32 % 4 == 0); the carry branch is kept for symmetry with PackSH1/3.
                if (bitOffset + 4 > 32)
                    output[wordStart + 1] |= ((uint)sint4SH >> (32 - bitOffset));
            }
        }

        /// <summary>
        /// Inspired from SparkJs encodeSh3Rgb implementation
        ///
        /// Encode an array of 21 signed RGB SH3 coefficients (clamped to [-1,1]) into
        /// an array of 4 uint32 values, where each coefficient is stored as a sint6.
        /// </summary>
        protected static void PackSH3(float[] sh, Span<uint> output)
        {
            output.Clear();
            for (var i = 0; i < 21; ++i)
            {
                float shScaled = sh[i] * 31.0f;
                int shBounded = (int)Math.Round(Math.Max(-31.0f, Math.Min(31.0f, shScaled)));
                int sint6SH = shBounded & 0x3f;
                int bitStart = i * 6;
                int bitEnd = bitStart + 6;

                int wordStart = (int)Math.Floor((double)(bitStart / 32));
                int bitOffset = bitStart - wordStart * 32;
                uint firstWord = (uint)((sint6SH << bitOffset) & 0xffffffff);

                output[wordStart] |= firstWord;
                if (bitEnd > wordStart * 32 + 32)
                {
                    uint secondWord = ((uint)sint6SH >> (32 - bitOffset)) & 0xffffffff;
                    output[wordStart + 1] |= secondWord;
                }
            }
        }

        // ─── Binary import cache ───────────────────────────────────────────────────

        const uint CacheMagic = 0x43435347u; // "GSCC" little-endian
        // v2: added optional PackedSH4 region (only present when SHBands == 4).
        const uint CacheFormatVersion = 2u;

        /// <summary>
        /// Attempts to populate this asset's packed arrays from a previously saved cache
        /// file. Returns true and leaves the asset fully loaded on success; returns false
        /// (and leaves the asset unmodified) if the file is absent, corrupt, or version-
        /// mismatched.
        /// </summary>
        public bool TryLoadFromCache(string cachePath)
        {
            if (!File.Exists(cachePath)) return false;
            try
            {
                using var fs = new FileStream(cachePath, FileMode.Open, FileAccess.Read);
                using var br = new BinaryReader(fs);

                if (br.ReadUInt32() != CacheMagic) return false;
                if (br.ReadUInt32() != CacheFormatVersion) return false;

                SplatCount = br.ReadUInt32();
                SHBands    = br.ReadByte();
                br.ReadByte(); br.ReadByte(); br.ReadByte(); // 3-byte padding

                var center = new Vector3(br.ReadSingle(), br.ReadSingle(), br.ReadSingle());
                var size   = new Vector3(br.ReadSingle(), br.ReadSingle(), br.ReadSingle());
                Bounds = new Bounds(center, size);

                Allocate();

                ReadExactBytes(fs, MemoryMarshal.AsBytes(PackedSplats.AsSpan()));
                if (SHBands >= 1) ReadExactBytes(fs, MemoryMarshal.AsBytes(PackedSH1.AsSpan()));
                if (SHBands >= 2) ReadExactBytes(fs, MemoryMarshal.AsBytes(PackedSH2.AsSpan()));
                if (SHBands >= 3) ReadExactBytes(fs, MemoryMarshal.AsBytes(PackedSH3.AsSpan()));
                if (SHBands >= 4) ReadExactBytes(fs, MemoryMarshal.AsBytes(PackedSH4.AsSpan()));

                return true;
            }
            catch
            {
                // Corrupt or incompatible cache — fall through to full reimport.
                return false;
            }
        }

        /// <summary>
        /// Writes the current packed arrays to a binary cache file so that future imports
        /// of the same asset can skip the expensive pack step.
        /// </summary>
        public void SaveToCache(string cachePath)
        {
            Directory.CreateDirectory(Path.GetDirectoryName(cachePath)!);
            using var fs = new FileStream(cachePath, FileMode.Create, FileAccess.Write);
            using var bw = new BinaryWriter(fs);

            bw.Write(CacheMagic);
            bw.Write(CacheFormatVersion);
            bw.Write(SplatCount);
            bw.Write((byte)SHBands);
            bw.Write((byte)0); bw.Write((byte)0); bw.Write((byte)0); // 3-byte padding

            bw.Write(Bounds.center.x); bw.Write(Bounds.center.y); bw.Write(Bounds.center.z);
            bw.Write(Bounds.size.x);   bw.Write(Bounds.size.y);   bw.Write(Bounds.size.z);

            fs.Write(MemoryMarshal.AsBytes(PackedSplats.AsSpan()));
            if (SHBands >= 1) fs.Write(MemoryMarshal.AsBytes(PackedSH1.AsSpan()));
            if (SHBands >= 2) fs.Write(MemoryMarshal.AsBytes(PackedSH2.AsSpan()));
            if (SHBands >= 3) fs.Write(MemoryMarshal.AsBytes(PackedSH3.AsSpan()));
            if (SHBands >= 4) fs.Write(MemoryMarshal.AsBytes(PackedSH4.AsSpan()));
        }

        static void ReadExactBytes(Stream stream, Span<byte> buffer)
        {
            int offset = 0;
            while (offset < buffer.Length)
            {
                int read = stream.Read(buffer.Slice(offset));
                if (read == 0) throw new EndOfStreamException("Unexpected end of Gsplat cache file");
                offset += read;
            }
        }
    }
}