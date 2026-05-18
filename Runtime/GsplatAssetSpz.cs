// Copyright (c) 2025 Niantic Spatial
// SPDX-License-Identifier: MIT

using System;
using System.Diagnostics;
using System.Threading;
using System.Threading.Tasks;
using UnityEngine;

namespace Gsplat
{
    public struct SpzPhaseTimings
    {
        public long DecompressMs;  // gzip decompression
        public long PackMs;        // per-splat decode + pack loop
    }

    // Loads SPZ files (https://github.com/nianticlabs/spz) and stores them in
    // Spark-compressed format, inheriting all rendering infrastructure from GsplatAssetSpark.
    public class GsplatAssetSpz : GsplatAssetSpark
    {
        // Atomic progress counter. Used as a bit mask to help local threads communicate progress back to the main threads progress bar.
        const int ProgressStride = 65536;

        // Constant decode state shared across all splats; passed by `in` to the per-splat helper.
        readonly struct DecodeContext
        {
            public readonly SpzData Data;
            public readonly bool Float16Pos;
            public readonly bool SmallestThree;
            public readonly byte FractionalBits;
            public readonly int ShDim;
            public readonly int ShBands;
            public readonly float PosXSign, PosYSign, PosZSign;
            public readonly float RotXSign, RotYSign, RotZSign;
            public readonly SourceCoordinates SrcCoords;

            public DecodeContext(SpzData data, SourceCoordinates srcCoords, int shBands)
            {
                Data = data;
                Float16Pos = data.Header.Version == 1;
                SmallestThree = data.Header.Version >= 3;
                FractionalBits = data.Header.FractionalBits;
                ShDim = SpzLoader.ShDim(data.Header.ShDegree);
                ShBands = shBands;
                SrcCoords = srcCoords;
                (PosXSign, PosYSign, PosZSign) = GsplatUtils.AxisSigns(srcCoords);
                RotXSign = PosYSign * PosZSign;
                RotYSign = PosXSign * PosZSign;
                RotZSign = PosXSign * PosYSign;
            }
        }

        public override void LoadFromPly(string plyPath, ProgressCallback progressCallback = null,
            SourceCoordinates sourceCoordinates = SourceCoordinates.RUF)
            => throw new NotSupportedException("GsplatAssetSpz loads SPZ files, not PLY.");

        public SpzPhaseTimings LoadFromSpz(string spzPath,
            SourceCoordinates sourceCoordinates = SourceCoordinates.RUB,
            ProgressCallback progressCallback = null)
        {
            var swDecompress = Stopwatch.StartNew();
            var data = SpzLoader.Load(spzPath);
            swDecompress.Stop();

            var h = data.Header;
            if (h.ShDegree > 4)
                throw new NotSupportedException($"SPZ SH degree {h.ShDegree} is not supported (max 4)");

            SplatCount = h.NumPoints;
            SHBands = h.ShDegree;
            int splatCount = (int)SplatCount;

            // The Allocate call is allocating the SH bands using the parent class
            Allocate();

            var ctx = new DecodeContext(data, sourceCoordinates, SHBands);
            // Band 4 has 9 coefficients × 3 channels; reused each splat. Sized for the
            // widest band so the same buffer serves bands 1–4.
            var tlShBand = new ThreadLocal<float[]>(() => new float[9 * 3]);

            var gMin = Vector3.positiveInfinity;
            var gMax = Vector3.negativeInfinity;
            var boundsLock = new object();

            // Shared counter incremented by worker threads; read by the main thread for progress.
            long processedCount = 0;
            const int progressMask = ProgressStride - 1;

            var swPack = Stopwatch.StartNew();

            // Run parallel work on thread pool so the calling (main) thread can update the
            // progress bar — EditorUtility.DisplayProgressBar requires the main thread.
            var packTask = Task.Run(() =>
                Parallel.For(
                    0, splatCount,
                    () => (min: Vector3.positiveInfinity, max: Vector3.negativeInfinity),
                    (i, _, localBounds) =>
                    {
                        var position = DecodeSplatIntoPackedArrays(i, in ctx, tlShBand.Value);
                        localBounds.min = Vector3.Min(localBounds.min, position);
                        localBounds.max = Vector3.Max(localBounds.max, position);

                        // Bump shared counter every ProgressStride splats
                        if ((i & progressMask) == 0)
                            Interlocked.Add(ref processedCount, ProgressStride);

                        return localBounds;
                    },
                    localBounds =>
                    {
                        lock (boundsLock)
                        {
                            gMin = Vector3.Min(gMin, localBounds.min);
                            gMax = Vector3.Max(gMax, localBounds.max);
                        }
                    }));

            // Main thread polls the counter and drives the progress bar at ~10 fps.
            while (!packTask.IsCompleted)
            {
                if (progressCallback != null)
                {
                    float p = Math.Min(1f, Interlocked.Read(ref processedCount) / (float)splatCount);
                    progressCallback("Packing splats", p);
                }
                Thread.Sleep(100);
            }

            packTask.GetAwaiter().GetResult(); // re-throw any exception from worker threads
            swPack.Stop();
            tlShBand.Dispose();

            if (SplatCount > 0)
                Bounds = new Bounds((gMin + gMax) * 0.5f, gMax - gMin);

            progressCallback?.Invoke("Packing splats", 1f);

            return new SpzPhaseTimings
            {
                DecompressMs = swDecompress.ElapsedMilliseconds,
                PackMs = swPack.ElapsedMilliseconds,
            };
        }

        // Decodes one SPZ splat and writes it into PackedSplats / PackedSH1..3.
        // Returns the world-space position so the caller can extend its bounds reduction.
        Vector3 DecodeSplatIntoPackedArrays(int i, in DecodeContext ctx, float[] shBandData)
        {
            var rawPos = ctx.Float16Pos
                ? SpzLoader.DecodePositionFloat16(ctx.Data.Positions, i)
                : SpzLoader.DecodePosition(ctx.Data.Positions, i, ctx.FractionalBits);
            var position = new Vector3(ctx.PosXSign * rawPos.x, ctx.PosYSign * rawPos.y, ctx.PosZSign * rawPos.z);

            var rgb = SpzLoader.DecodeColor(ctx.Data.Colors, i);
            var color = new Vector4(rgb.x, rgb.y, rgb.z, SpzLoader.DecodeAlphaLogit(ctx.Data.Alphas, i));
            var scale = SpzLoader.DecodeScaleLog(ctx.Data.Scales, i);

            var rawRot = SpzLoader.DecodeRotation(ctx.Data.Rotations, i, ctx.SmallestThree);
            // Apply per-axis sign flips for the chosen frame; same component mapping as the PLY path.
            var rotation = new Quaternion(
                rawRot.w,
                ctx.RotXSign * rawRot.x,
                ctx.RotYSign * rawRot.y,
                ctx.RotZSign * rawRot.z);

            PackedSplats[i] = PackSplat(color, position, scale, rotation);

            for (int j = 1, bandOffset = 0; j <= ctx.ShBands; j++)
            {
                int bandSize = j * 2 + 1;
                int baseCoeff = i * ctx.ShDim * 3;
                for (int k = 0; k < bandSize; k++)
                {
                    int off = baseCoeff + (bandOffset + k) * 3;
                    float sign = GsplatUtils.ShSign(ctx.SrcCoords, j, k);
                    shBandData[k * 3 + 0] = sign * SpzLoader.UnquantizeSH(ctx.Data.SH, off + 0);
                    shBandData[k * 3 + 1] = sign * SpzLoader.UnquantizeSH(ctx.Data.SH, off + 1);
                    shBandData[k * 3 + 2] = sign * SpzLoader.UnquantizeSH(ctx.Data.SH, off + 2);
                }

                if (j == 1) PackSH1(shBandData, PackedSH1.AsSpan(i * 2, 2));
                if (j == 2) PackSH2(shBandData, PackedSH2.AsSpan(i * 4, 4));
                if (j == 3) PackSH3(shBandData, PackedSH3.AsSpan(i * 4, 4));
                if (j == 4) PackSH4(shBandData, PackedSH4.AsSpan(i * 4, 4));

                bandOffset += bandSize;
            }

            return position;
        }
    }
}
