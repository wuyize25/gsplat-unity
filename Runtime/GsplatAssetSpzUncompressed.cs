// Copyright (c) 2025 Niantic Spatial
// SPDX-License-Identifier: MIT

using System.Diagnostics;
using UnityEngine;

namespace Gsplat
{
    // Loads SPZ files into GsplatAssetUncompressed
    public class GsplatAssetSpzUncompressed : GsplatAssetUncompressed
    {
        public override void LoadFromPly(string plyPath, ProgressCallback progressCallback = null,
            SourceCoordinates sourceCoordinates = SourceCoordinates.RUF)
            => throw new System.NotSupportedException("GsplatAssetSpzUncompressed loads SPZ files, not PLY.");

        public SpzPhaseTimings LoadFromSpz(string spzPath,
            SourceCoordinates sourceCoordinates = SourceCoordinates.RUB,
            ProgressCallback progressCallback = null)
        {
            var swDecompress = Stopwatch.StartNew();
            var data = SpzLoader.Load(spzPath);
            swDecompress.Stop();
            var h = data.Header;

            if (h.ShDegree > 4)
                throw new System.NotSupportedException($"SPZ SH degree {h.ShDegree} is not supported (max 4)");

            SplatCount = h.NumPoints;
            SHBands = h.ShDegree;
            bool float16Pos = h.Version == 1;
            bool smallestThree = h.Version >= 3;
            int shDim = SpzLoader.ShDim(h.ShDegree);

            var (posXSign, posYSign, posZSign) = GsplatUtils.AxisSigns(sourceCoordinates);
            float rotXSign = posYSign * posZSign;
            float rotYSign = posXSign * posZSign;
            float rotZSign = posXSign * posYSign;

            Allocate();

            int splatCount = (int)SplatCount;

            var swPack = Stopwatch.StartNew();
            for (int i = 0; i < splatCount; i++)
            {
                var rawPos = float16Pos
                    ? SpzLoader.DecodePositionFloat16(data.Positions, i)
                    : SpzLoader.DecodePosition(data.Positions, i, h.FractionalBits);
                Positions[i] = new Vector3(posXSign * rawPos.x, posYSign * rawPos.y, posZSign * rawPos.z);

                if (i == 0) Bounds = new Bounds(Positions[i], Vector3.zero);
                else Bounds.Encapsulate(Positions[i]);

                // Color: raw SH DC coefficients + post-sigmoid alpha.
                // The uncompressed shader applies (* SH_C0 + 0.5) at render time.
                var rgb = SpzLoader.DecodeColor(data.Colors, i);
                Colors[i] = new Vector4(rgb.x, rgb.y, rgb.z,
                    SpzLoader.DecodeAlphaLinear(data.Alphas, i));

                // Scale: uncompressed expects linear scale (exp of log-scale).
                var logScale = SpzLoader.DecodeScaleLog(data.Scales, i);
                Scales[i] = new Vector3(
                    Mathf.Exp(logScale.x),
                    Mathf.Exp(logScale.y),
                    Mathf.Exp(logScale.z));

                // Quaternion stored as Vector4(w_real, x_imag, y_imag, z_imag).
                var rawRot = SpzLoader.DecodeRotation(data.Rotations, i, smallestThree);
                Rotations[i] = new Vector4(rawRot.w, rotXSign * rawRot.x, rotYSign * rawRot.y, rotZSign * rawRot.z).normalized;

                // SH: SPZ stores per-point as [R0,G0,B0, R1,G1,B1, ...] (interleaved by coeff).
                // Uncompressed SHs[i*shDim+j] = Vector3(Rj, Gj, Bj).
                for (int band = 1, j = 0; band <= SHBands; band++)
                {
                    int bandSize = band * 2 + 1;
                    for (int k = 0; k < bandSize; k++, j++)
                    {
                        float sign = GsplatUtils.ShSign(sourceCoordinates, band, k);
                        int off = i * shDim * 3 + j * 3;
                        SHs[i * shDim + j] = sign * new Vector3(
                            SpzLoader.UnquantizeSH(data.SH, off + 0),
                            SpzLoader.UnquantizeSH(data.SH, off + 1),
                            SpzLoader.UnquantizeSH(data.SH, off + 2));
                    }
                }

                if ((i & 0xFFFF) == 0)
                    progressCallback?.Invoke("Reading splats", i / (float)splatCount);
            }
            swPack.Stop();

            return new SpzPhaseTimings
            {
                DecompressMs = swDecompress.ElapsedMilliseconds,
                PackMs = swPack.ElapsedMilliseconds,
            };
        }
    }
}
