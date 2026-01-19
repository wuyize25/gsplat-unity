// Copyright (c) 2025 Arthur Aillet
// SPDX-License-Identifier: MIT

using System;
using UnityEngine;

namespace Gsplat.Editor
{
    /// <summary>
    /// Implementation taken from SparkJs
    /// 
    /// A PackedSplats is a collection of Gaussian splats, packed into a format that
    /// takes exactly 16 bytes per Gsplat to maximize memory and cache efficiency.
    /// The center xyz coordinates are encoded as float16 (3 x 2 bytes), scale xyz
    /// as 3 x uint8 that encode a log scale from e^-12 to e^9, rgba as 4 x uint8,
    /// and quaternion encoded via axis+angle using 2 x uint8 for quat encoding
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
    public class GsplatPacker
    {
        /// <summary>
        /// Copied from SparkJs encodeQuatXyz888 implementation
        /// Encode a Quaternion into 3 8-bit integer, converting the xyz coordinates
        /// to signed 8-bit integers (w can be derived from xyz), and flipping the sign
        /// of the quaternion if necessary to make this possible (q == -q for quaternions).
        /// </summary>
        private static (byte, byte, byte) EncodeQuatXyz888(Vector4 quaternion)
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
        /// Copied from SparkJs decodeQuatXyz888 implementation
        /// Decode a 24-bit integer of the quaternion's xyz coordinates into a THREE.Quaternion.
        /// </summary>
        public static Quaternion DecodeQuatXyz888(uint encoded)
        {
            uint iQuatX = (encoded << 24) >> 24;
            uint iQuatY = (encoded << 16) >> 24;
            uint iQuatZ = (encoded << 8) >> 24;
            Quaternion quat = new(iQuatX / 127.0f, iQuatY / 127.0f, iQuatZ / 127.0f, 0.0f);
            float dotSelf = quat.x * quat.x + quat.y * quat.y + quat.z * quat.z;
            quat.w = (float)Math.Sqrt(Math.Max(0.0f, 1.0f - dotSelf));
            return quat;
        }

        const float LN_SCALE_MIN = -12.0f;
        const float LN_SCALE_MAX = 9.0f;
        const float LN_SCALE_ZERO = -30.0f;
        const float LN_SCALE_SCALE = 254.0f / (LN_SCALE_MAX - LN_SCALE_MIN);
        static readonly float SCALE_ZERO = (float)Math.Exp(LN_SCALE_ZERO);

        private static byte EncodeScaleOnLogScale(float scale)
        {
            if (scale < SCALE_ZERO)
            {
                return 0;
            }
            var encoded = ((Math.Log(scale) - LN_SCALE_MIN) * LN_SCALE_SCALE) + 1;
            return (byte)Math.Min(255, Math.Max(1, Math.Round(encoded)));
        }

        public static uint[] PackSplat(Vector4 color, Vector3 position, Vector3 scale, Vector4 rotation)
        {
            byte uR = GsplatUtils.FloatToByte(color.x);
            byte uG = GsplatUtils.FloatToByte(color.y);
            byte uB = GsplatUtils.FloatToByte(color.z);
            byte uA = GsplatUtils.FloatToByte(color.w);

            ushort uPosX = Mathf.FloatToHalf(position.x);
            ushort uPosY = Mathf.FloatToHalf(position.y);
            ushort uPosZ = Mathf.FloatToHalf(position.z);

            (byte uQuatX, byte uQuatY, byte uQuatZ) = EncodeQuatXyz888(rotation.normalized);

            byte uScaleX = EncodeScaleOnLogScale(scale.x);
            byte uScaleY = EncodeScaleOnLogScale(scale.y);
            byte uScaleZ = EncodeScaleOnLogScale(scale.z);

            uint[] packedSplat = new uint[]
            {
                uR | (uint)(uG << 8) | (uint)(uB << 16) | (uint)(uA << 24),
                uPosX | (uint)(uPosY << 16),
                uPosZ | (uint)(uQuatX << 16) | (uint)(uQuatY << 24),
                uScaleX | (uint)(uScaleY << 8) | (uint)(uScaleZ << 16) | (uint)(uQuatZ << 24),
            };

            return packedSplat;
        }
    }
}
