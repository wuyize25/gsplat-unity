// Copyright (c) 2026
// SPDX-License-Identifier: MIT

using System;
using System.Diagnostics;
using System.Threading;
using System.Threading.Tasks;
using UnityEngine;

namespace Gsplat
{
    // Loads SOG files and stores them in Spark-compressed format.
    public class GsplatAssetSog : GsplatAssetSpark
    {
        const int ProgressStride = 65536;

        readonly struct DecodeContext
        {
            public readonly SogData Data;
            public readonly float PosXSign, PosYSign, PosZSign;
            public readonly float RotXSign, RotYSign, RotZSign;
            public readonly SourceCoordinates SrcCoords;

            public DecodeContext(SogData data, SourceCoordinates srcCoords)
            {
                Data = data;
                SrcCoords = srcCoords;
                (PosXSign, PosYSign, PosZSign) = GsplatUtils.AxisSigns(srcCoords);
                RotXSign = PosYSign * PosZSign;
                RotYSign = PosXSign * PosZSign;
                RotZSign = PosXSign * PosYSign;
            }
        }

        public override void LoadFromPly(string plyPath, ProgressCallback progressCallback = null,
            SourceCoordinates sourceCoordinates = SourceCoordinates.RUF)
            => throw new NotSupportedException("GsplatAssetSog loads SOG files, not PLY.");

        public SpzPhaseTimings LoadFromSog(string sogPath,
            SourceCoordinates sourceCoordinates = SourceCoordinates.RDB,
            ProgressCallback progressCallback = null)
        {
            var swDecode = Stopwatch.StartNew();
            var data = SogLoader.Load(sogPath);
            swDecode.Stop();

            SplatCount = (uint)data.Count;
            SHBands = (byte)data.ShBands;
            Allocate();

            int splatCount = data.Count;
            var ctx = new DecodeContext(data, sourceCoordinates);
            var tlShBand = new ThreadLocal<float[]>(() => new float[7 * 3]);

            var gMin = Vector3.positiveInfinity;
            var gMax = Vector3.negativeInfinity;
            var boundsLock = new object();
            long processedCount = 0;
            const int progressMask = ProgressStride - 1;

            var swPack = Stopwatch.StartNew();
            var packTask = Task.Run(() =>
                Parallel.For(
                    0, splatCount,
                    () => (min: Vector3.positiveInfinity, max: Vector3.negativeInfinity),
                    (i, _, localBounds) =>
                    {
                        var position = DecodeSplatIntoPackedArrays(i, in ctx, tlShBand.Value);
                        localBounds.min = Vector3.Min(localBounds.min, position);
                        localBounds.max = Vector3.Max(localBounds.max, position);

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

            while (!packTask.IsCompleted)
            {
                if (progressCallback != null)
                {
                    float p = splatCount == 0 ? 1f : Math.Min(1f, Interlocked.Read(ref processedCount) / (float)splatCount);
                    progressCallback("Packing SOG splats", p);
                }
                Thread.Sleep(100);
            }

            packTask.GetAwaiter().GetResult();
            swPack.Stop();
            tlShBand.Dispose();

            if (SplatCount > 0)
                Bounds = new Bounds((gMin + gMax) * 0.5f, gMax - gMin);

            progressCallback?.Invoke("Packing SOG splats", 1f);

            return new SpzPhaseTimings
            {
                DecompressMs = swDecode.ElapsedMilliseconds,
                PackMs = swPack.ElapsedMilliseconds,
            };
        }

        Vector3 DecodeSplatIntoPackedArrays(int i, in DecodeContext ctx, float[] shBandData)
        {
            var rawPos = SogLoader.DecodePosition(ctx.Data, i);
            var position = new Vector3(ctx.PosXSign * rawPos.x, ctx.PosYSign * rawPos.y, ctx.PosZSign * rawPos.z);

            var color = SogLoader.DecodeColorLogitAlpha(ctx.Data, i);
            var scale = SogLoader.DecodeScaleLog(ctx.Data, i);
            var rawRot = SogLoader.DecodeRotation(ctx.Data, i);
            var rotation = new Quaternion(
                rawRot.w,
                ctx.RotXSign * rawRot.x,
                ctx.RotYSign * rawRot.y,
                ctx.RotZSign * rawRot.z);

            PackedSplats[i] = PackSplat(color, position, scale, rotation);

            for (int band = 1; band <= SHBands; band++)
            {
                SogLoader.DecodeShBand(ctx.Data, i, band, shBandData);
                int bandSize = band * 2 + 1;
                for (int k = 0; k < bandSize; k++)
                {
                    float sign = GsplatUtils.ShSign(ctx.SrcCoords, band, k);
                    shBandData[k * 3 + 0] *= sign;
                    shBandData[k * 3 + 1] *= sign;
                    shBandData[k * 3 + 2] *= sign;
                }

                if (band == 1) PackSH1(shBandData, PackedSH1.AsSpan(i * 2, 2));
                if (band == 2) PackSH2(shBandData, PackedSH2.AsSpan(i * 4, 4));
                if (band == 3) PackSH3(shBandData, PackedSH3.AsSpan(i * 4, 4));
            }

            return position;
        }
    }
}
