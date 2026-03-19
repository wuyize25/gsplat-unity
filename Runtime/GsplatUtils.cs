// Copyright (c) 2025 Yize Wu
// SPDX-License-Identifier: MIT

using System;
using System.Runtime.CompilerServices;
using UnityEngine;

namespace Gsplat
{
    public static class GsplatUtils
    {
        public const string k_PackagePath = "Packages/wu.yize.gsplat/";
        public static readonly Version k_Version = new("1.2.0");

            /// Taken from Unity.Mathematics
        /// <summary>Returns the bit pattern of a uint as a float.</summary>
        /// <param name="x">The uint bits to copy.</param>
        /// <returns>The float with the same bit pattern as the input.</returns>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static float asfloat(uint x)
        {
            unsafe
            {
                return *(float*)&x;
            }
        }

        /// Taken from Unity.Mathematics
        /// <summary>Returns the bit pattern of a float as a uint.</summary>
        /// <param name="x">The float bits to copy.</param>
        /// <returns>The uint with the same bit pattern as the input.</returns>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static uint asuint(float x)
        {
            unsafe
            {
                return *(uint*)&x;
            }
        }

        // radix sort etc. friendly, see http://stereopsis.com/radix.html
        public static uint FloatToSortableUint(float f)
        {
            uint fu = asuint(f);
            uint mask = (uint)(-((int)(fu >> 31)) | 0x80000000);
            return fu ^ mask;
        }

        public static float SortableUintToFloat(uint v)
        {
            uint mask = ((v >> 31) - 1) | 0x80000000u;
            return asfloat(v ^ mask);
        }

        /// <summary>
        /// Convert float ranging between -1..1 to a -127..127 sint8
        /// </summary>
        public static sbyte FloatToSByte(float x)
        {
            return (sbyte)Math.Max(-127.0, Math.Min(127.0, Math.Round(x * 127.0)));
        }

        /// <summary>
        /// Convert float ranging between 0..1 to a 0..255 uint8
        /// </summary>
        public static byte FloatToByte(float x)
        {
            return (byte)Math.Max(0.0, Math.Min(255.0, Math.Round(x * 255.0)));
        }

        public static float Sigmoid(float x)
        {
            return 1.0f / (1.0f + Mathf.Exp(-x));
        }

        public const int k_PlyPropertyCountNoSH = 17;

        public static byte CalcSHBandsFromPropertyCount(int propertyCount)
        {
            return CalcSHBandsFromSHPropertyCount(propertyCount - k_PlyPropertyCountNoSH);
        }

        public static byte CalcSHBandsFromSHPropertyCount(int shPropertyCount)
        {
            return (byte)(Math.Sqrt((shPropertyCount + 3) / 3) - 1);
        }

        public static int SHBandsToCoefficientCount(byte shBands)
        {
            return (shBands + 1) * (shBands + 1) - 1;
        }

        public static Bounds CalcWorldBounds(Bounds localBounds, Transform transform)
        {
            var localCenter = localBounds.center;
            var localExtents = localBounds.extents;

            var localCorners = new[]
            {
                localCenter + new Vector3(localExtents.x, localExtents.y, localExtents.z),
                localCenter + new Vector3(localExtents.x, localExtents.y, -localExtents.z),
                localCenter + new Vector3(localExtents.x, -localExtents.y, localExtents.z),
                localCenter + new Vector3(localExtents.x, -localExtents.y, -localExtents.z),
                localCenter + new Vector3(-localExtents.x, localExtents.y, localExtents.z),
                localCenter + new Vector3(-localExtents.x, localExtents.y, -localExtents.z),
                localCenter + new Vector3(-localExtents.x, -localExtents.y, localExtents.z),
                localCenter + new Vector3(-localExtents.x, -localExtents.y, -localExtents.z)
            };

            var worldBounds = new Bounds(transform.TransformPoint(localCorners[0]), Vector3.zero);
            for (var i = 1; i < 8; i++)
                worldBounds.Encapsulate(transform.TransformPoint(localCorners[i]));

            return worldBounds;
        }

        public static uint DivRoundUp(uint x, uint y) => (x + y - 1) / y;
    }
}
