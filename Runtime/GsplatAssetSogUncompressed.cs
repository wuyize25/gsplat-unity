// Copyright (c) 2026
// SPDX-License-Identifier: MIT

using System.Diagnostics;
using UnityEngine;

namespace Gsplat
{
    // Loads SOG files into GsplatAssetUncompressed.
    public class GsplatAssetSogUncompressed : GsplatAssetUncompressed
    {
        public override void LoadFromPly(string plyPath, ProgressCallback progressCallback = null,
            SourceCoordinates sourceCoordinates = SourceCoordinates.RUF)
            => throw new System.NotSupportedException("GsplatAssetSogUncompressed loads SOG files, not PLY.");

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

            var (posXSign, posYSign, posZSign) = GsplatUtils.AxisSigns(sourceCoordinates);
            float rotXSign = posYSign * posZSign;
            float rotYSign = posXSign * posZSign;
            float rotZSign = posXSign * posYSign;
            int shDim = data.ShCoefficientCount;
            int splatCount = data.Count;
            var shBandData = new float[7 * 3];

            var swPack = Stopwatch.StartNew();
            for (int i = 0; i < splatCount; i++)
            {
                var rawPos = SogLoader.DecodePosition(data, i);
                Positions[i] = new Vector3(posXSign * rawPos.x, posYSign * rawPos.y, posZSign * rawPos.z);

                if (i == 0) Bounds = new Bounds(Positions[i], Vector3.zero);
                else Bounds.Encapsulate(Positions[i]);

                Colors[i] = SogLoader.DecodeColorLinearAlpha(data, i);

                var logScale = SogLoader.DecodeScaleLog(data, i);
                Scales[i] = new Vector3(
                    Mathf.Exp(logScale.x),
                    Mathf.Exp(logScale.y),
                    Mathf.Exp(logScale.z));

                var rawRot = SogLoader.DecodeRotation(data, i);
                Rotations[i] = new Vector4(
                    rawRot.w,
                    rotXSign * rawRot.x,
                    rotYSign * rawRot.y,
                    rotZSign * rawRot.z).normalized;

                for (int band = 1, bandOffset = 0; band <= SHBands; band++)
                {
                    int bandSize = band * 2 + 1;
                    SogLoader.DecodeShBand(data, i, band, shBandData);
                    for (int k = 0; k < bandSize; k++)
                    {
                        float sign = GsplatUtils.ShSign(sourceCoordinates, band, k);
                        SHs[i * shDim + bandOffset + k] = sign * new Vector3(
                            shBandData[k * 3 + 0],
                            shBandData[k * 3 + 1],
                            shBandData[k * 3 + 2]);
                    }
                    bandOffset += bandSize;
                }

                if ((i & 0xFFFF) == 0)
                    progressCallback?.Invoke("Reading SOG splats", splatCount == 0 ? 1f : i / (float)splatCount);
            }
            swPack.Stop();

            progressCallback?.Invoke("Reading SOG splats", 1f);

            return new SpzPhaseTimings
            {
                DecompressMs = swDecode.ElapsedMilliseconds,
                PackMs = swPack.ElapsedMilliseconds,
            };
        }
    }
}
