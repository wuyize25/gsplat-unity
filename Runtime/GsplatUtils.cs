// Copyright (c) 2025 Yize Wu
// SPDX-License-Identifier: MIT

using System;
using Unity.Mathematics;
using UnityEngine;

namespace Gsplat
{
    public static class GsplatUtils
    {
        public const string k_PackagePath = "Packages/wu.yize.gsplat/";
        public static readonly Version k_Version = new("1.3.0");

        // radix sort etc. friendly, see http://stereopsis.com/radix.html
        public static uint FloatToSortableUint(float f)
        {
            uint fu = math.asuint(f);
            uint mask = (uint)(-((int)(fu >> 31)) | 0x80000000);
            return fu ^ mask;
        }

        public static float SortableUintToFloat(uint v)
        {
            uint mask = ((v >> 31) - 1) | 0x80000000u;
            return math.asfloat(v ^ mask);
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

        // ─── Coordinate-frame conversion helpers ─────────────────────────────────

        /// <summary>
        /// Decomposes <paramref name="src"/> into per-axis sign flips relative to Unity (RUF).
        /// xSign = −1 for Left frames, ySign = −1 for Down frames, zSign = −1 for Back frames.
        /// Unspecified defaults to RUB (the standard 3DGS / SPZ convention).
        /// </summary>
        public static (float xSign, float ySign, float zSign) AxisSigns(SourceCoordinates src)
        {
            if (src == SourceCoordinates.Unspecified) src = SourceCoordinates.RUB;
            bool left = src is SourceCoordinates.LDB or SourceCoordinates.LUB
                             or SourceCoordinates.LDF or SourceCoordinates.LUF;
            bool down = src is SourceCoordinates.LDB or SourceCoordinates.RDB
                             or SourceCoordinates.LDF or SourceCoordinates.RDF;
            bool back = src is SourceCoordinates.LDB or SourceCoordinates.RDB
                             or SourceCoordinates.LUB or SourceCoordinates.RUB;
            return (left ? -1f : 1f, down ? -1f : 1f, back ? -1f : 1f);
        }

        /// <summary>
        /// Returns the sign to apply to SH band <paramref name="band"/> coefficient at
        /// band-local index <paramref name="k"/> when converting from <paramref name="src"/> to Unity.
        /// The total sign is the product of each applicable single-axis sign:
        /// <list type="bullet">
        ///   <item>Flip Z: negate odd k  (parity from (−1)^k = (−1)^(l+m))</item>
        ///   <item>Flip Y: negate k &lt; band  (sin-φ terms, m &lt; 0)</item>
        ///   <item>Flip X: sign = (−1)^(k−l) for k&gt;l, 1 for k=l, (−1)^(l−k+1) for k&lt;l</item>
        /// </list>
        /// </summary>
        public static float ShSign(SourceCoordinates src, int band, int k)
        {
            var (xs, ys, zs) = AxisSigns(src);
            float sign = 1f;
            if (xs < 0) sign *= ShSignX(band, k);
            if (ys < 0) sign *= ShSignY(band, k);
            if (zs < 0) sign *= ShSignZ(band, k);
            return sign;
        }

        // Real SH sign under X-flip (φ → π−φ):
        //   m > 0 (k > l): (−1)^(k−l)
        //   m = 0 (k = l): 1
        //   m < 0 (k < l): (−1)^(l−k+1)
        static float ShSignX(int l, int k)
        {
            if (k == l) return 1f;
            int power = k > l ? k - l : l - k + 1;
            return (power & 1) == 1 ? -1f : 1f;
        }

        // Real SH sign under Y-flip (φ → −φ): negate sin-φ terms, i.e. m < 0 (k < l).
        static float ShSignY(int l, int k) => k < l ? -1f : 1f;

        // Real SH sign under Z-flip (θ → π−θ): negate when (−1)^(l+m) = (−1)^k is odd.
        static float ShSignZ(int l, int k) => (k & 1) == 1 ? -1f : 1f;
    }
}
