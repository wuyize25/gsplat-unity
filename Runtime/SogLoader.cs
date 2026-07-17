// Copyright (c) 2026
// SPDX-License-Identifier: MIT

using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using UnityEngine;

namespace Gsplat
{
    internal sealed class SogData
    {
        public SogMeta Meta;
        public SogImage MeansL;
        public SogImage MeansU;
        public SogImage Scales;
        public SogImage Quats;
        public SogImage Sh0;
        public SogImage ShNCentroids;
        public SogImage ShNLabels;

        public bool HasShN => SogLoader.HasShN(Meta);
        public int Count => Meta.Count;
        public int ShBands => HasShN ? Meta.ShN.Bands : 0;
        public int ShCoefficientCount => HasShN ? GsplatUtils.SHBandsToCoefficientCount((byte)Meta.ShN.Bands) : 0;
    }

    [Serializable]
    internal sealed class SogMeta
    {
        public int version;
        public int count;
        public SogMetaMeans means;
        public SogMetaCodebook scales;
        public SogMetaQuats quats;
        public SogMetaCodebook sh0;
        public SogMetaShN shN;

        public int Version => version;
        public int Count => count;
        public SogMetaMeans Means => means;
        public SogMetaCodebook Scales => scales;
        public SogMetaQuats Quats => quats;
        public SogMetaCodebook Sh0 => sh0;
        public SogMetaShN ShN => shN;
    }

    [Serializable]
    internal sealed class SogMetaMeans
    {
        public float[] mins;
        public float[] maxs;
        public string[] files;
    }

    [Serializable]
    internal sealed class SogMetaCodebook
    {
        public float[] codebook;
        public string[] files;
    }

    [Serializable]
    internal sealed class SogMetaQuats
    {
        public string[] files;
    }

    [Serializable]
    internal sealed class SogMetaShN
    {
        public int bands;
        public int count;
        public float[] codebook;
        public string[] files;

        public int Bands => bands;
        public int Count => count;
    }

    internal static class SogLoader
    {
        const int SupportedVersion = 2;
        const int CodebookSize = 256;
        const float Sqrt1_2 = 0.70710678118f;

        public static SogData Load(string path)
        {
            try
            {
                using var fs = new FileStream(path, FileMode.Open, FileAccess.Read);
                using var zip = new ZipArchive(fs, ZipArchiveMode.Read);
                return LoadZip(zip, Path.GetFileName(path));
            }
            catch (InvalidDataException)
            {
                throw;
            }
            catch (NotSupportedException)
            {
                throw;
            }
            catch (Exception e)
            {
                throw new InvalidDataException($"SOG: failed to read '{Path.GetFileName(path)}' as a ZIP bundle: {e.Message}", e);
            }
        }

        static SogData LoadZip(ZipArchive zip, string displayName)
        {
            var entries = new Dictionary<string, ZipArchiveEntry>(StringComparer.Ordinal);
            foreach (var entry in zip.Entries)
            {
                var name = entry.FullName.Replace('\\', '/');
                if (!name.Contains("/") && !string.IsNullOrEmpty(name))
                    entries[name] = entry;
            }

            if (!entries.TryGetValue("meta.json", out var metaEntry))
                throw new InvalidDataException($"SOG: '{displayName}' is missing root entry 'meta.json'.");

            var metaJson = ReadEntryText(metaEntry);
            var meta = JsonUtility.FromJson<SogMeta>(metaJson);
            ValidateMeta(meta);

            var data = new SogData
            {
                Meta = meta,
                MeansL = ReadImage(entries, meta.Means.files[0]),
                MeansU = ReadImage(entries, meta.Means.files[1]),
                Scales = ReadImage(entries, meta.Scales.files[0]),
                Quats = ReadImage(entries, meta.Quats.files[0]),
                Sh0 = ReadImage(entries, meta.Sh0.files[0]),
            };

            if (data.HasShN)
            {
                data.ShNCentroids = ReadImage(entries, meta.ShN.files[0]);
                data.ShNLabels = ReadImage(entries, meta.ShN.files[1]);
            }

            ValidateImages(data);
            return data;
        }

        static string ReadEntryText(ZipArchiveEntry entry)
        {
            using var stream = entry.Open();
            using var reader = new StreamReader(stream);
            return reader.ReadToEnd();
        }

        static SogImage ReadImage(Dictionary<string, ZipArchiveEntry> entries, string name)
        {
            if (!entries.TryGetValue(name, out var entry))
                throw new InvalidDataException($"SOG: missing image '{name}'.");

            using var stream = entry.Open();
            using var ms = new MemoryStream();
            stream.CopyTo(ms);
            return SogImageDecoder.Decode(ms.ToArray(), name);
        }

        public static bool HasShN(SogMeta meta)
        {
            var shN = meta?.ShN;
            return shN != null &&
                   (shN.Bands != 0 || shN.Count != 0 || shN.codebook != null || shN.files != null);
        }

        static void ValidateMeta(SogMeta meta)
        {
            if (meta == null)
                throw new InvalidDataException("SOG: meta.json could not be parsed.");
            if (meta.Version != SupportedVersion)
                throw new NotSupportedException($"SOG: version {meta.Version} is not supported (expected {SupportedVersion}).");
            if (meta.Count < 0)
                throw new InvalidDataException($"SOG: count {meta.Count} is invalid.");
            if (meta.Means == null)
                throw new InvalidDataException("SOG: meta.means is missing.");
            ValidateFloatArray(meta.Means.mins, 3, "meta.means.mins");
            ValidateFloatArray(meta.Means.maxs, 3, "meta.means.maxs");
            ValidateFileArray(meta.Means.files, 2, "meta.means.files");
            if (meta.Scales == null)
                throw new InvalidDataException("SOG: meta.scales is missing.");
            ValidateFloatArray(meta.Scales.codebook, CodebookSize, "meta.scales.codebook");
            ValidateFileArray(meta.Scales.files, 1, "meta.scales.files");
            if (meta.Quats == null)
                throw new InvalidDataException("SOG: meta.quats is missing.");
            ValidateFileArray(meta.Quats.files, 1, "meta.quats.files");
            if (meta.Sh0 == null)
                throw new InvalidDataException("SOG: meta.sh0 is missing.");
            ValidateFloatArray(meta.Sh0.codebook, CodebookSize, "meta.sh0.codebook");
            ValidateFileArray(meta.Sh0.files, 1, "meta.sh0.files");

            if (!HasShN(meta))
                return;

            if (meta.ShN.Bands < 1 || meta.ShN.Bands > 3)
                throw new NotSupportedException($"SOG: shN.bands {meta.ShN.Bands} is not supported (expected 1..3).");
            if (meta.ShN.Count < 1 || meta.ShN.Count > 65536)
                throw new InvalidDataException($"SOG: shN.count {meta.ShN.Count} is invalid.");
            ValidateFloatArray(meta.ShN.codebook, CodebookSize, "meta.shN.codebook");
            ValidateFileArray(meta.ShN.files, 2, "meta.shN.files");
        }

        static void ValidateFloatArray(float[] values, int expectedLength, string name)
        {
            if (values == null || values.Length != expectedLength)
                throw new InvalidDataException($"SOG: {name} must contain {expectedLength} values.");
        }

        static void ValidateFileArray(string[] values, int expectedLength, string name)
        {
            if (values == null || values.Length != expectedLength)
                throw new InvalidDataException($"SOG: {name} must contain {expectedLength} filenames.");
            for (int i = 0; i < values.Length; i++)
            {
                if (string.IsNullOrEmpty(values[i]))
                    throw new InvalidDataException($"SOG: {name}[{i}] is empty.");
                if (values[i].Contains("/") || values[i].Contains("\\"))
                    throw new InvalidDataException($"SOG: bundled imports require root entry filenames; {name}[{i}] is '{values[i]}'.");
            }
        }

        static void ValidateImages(SogData data)
        {
            ValidatePropertyImage(data.MeansL, data.Count, data.Meta.Means.files[0]);
            ValidatePropertyImage(data.MeansU, data.Count, data.Meta.Means.files[1]);
            ValidatePropertyImage(data.Scales, data.Count, data.Meta.Scales.files[0]);
            ValidatePropertyImage(data.Quats, data.Count, data.Meta.Quats.files[0]);
            ValidatePropertyImage(data.Sh0, data.Count, data.Meta.Sh0.files[0]);

            if (!data.HasShN)
                return;

            ValidatePropertyImage(data.ShNLabels, data.Count, data.Meta.ShN.files[1]);
            int coeffs = data.ShCoefficientCount;
            int requiredCentroidWidth = 64 * coeffs;
            int requiredCentroidHeight = (data.Meta.ShN.Count + 63) / 64;
            if (data.ShNCentroids.Width < requiredCentroidWidth || data.ShNCentroids.Height < requiredCentroidHeight)
            {
                throw new InvalidDataException(
                    $"SOG: {data.Meta.ShN.files[0]} is {data.ShNCentroids.Width}x{data.ShNCentroids.Height}, expected at least {requiredCentroidWidth}x{requiredCentroidHeight}.");
            }
        }

        static void ValidatePropertyImage(SogImage image, int count, string name)
        {
            if (image.PixelCount < count)
                throw new InvalidDataException($"SOG: image '{name}' has {image.PixelCount} pixels, fewer than meta.count {count}.");
        }

        public static Vector3 DecodePosition(SogData data, int i)
        {
            var lo = data.MeansL[i];
            var hi = data.MeansU[i];
            return new Vector3(
                DecodePositionAxis(lo.r, hi.r, data.Meta.Means.mins[0], data.Meta.Means.maxs[0]),
                DecodePositionAxis(lo.g, hi.g, data.Meta.Means.mins[1], data.Meta.Means.maxs[1]),
                DecodePositionAxis(lo.b, hi.b, data.Meta.Means.mins[2], data.Meta.Means.maxs[2]));
        }

        static float DecodePositionAxis(byte lo, byte hi, float min, float max)
        {
            int v = lo | (hi << 8);
            float n = Mathf.Lerp(min, max, v / 65535.0f);
            return Mathf.Sign(n) * (Mathf.Exp(Mathf.Abs(n)) - 1.0f);
        }

        public static Vector3 DecodeScaleLog(SogData data, int i)
        {
            var p = data.Scales[i];
            var codebook = data.Meta.Scales.codebook;
            return new Vector3(codebook[p.r], codebook[p.g], codebook[p.b]);
        }

        public static Vector4 DecodeColorLinearAlpha(SogData data, int i)
        {
            var p = data.Sh0[i];
            var codebook = data.Meta.Sh0.codebook;
            return new Vector4(codebook[p.r], codebook[p.g], codebook[p.b], p.a / 255.0f);
        }

        public static Vector4 DecodeColorLogitAlpha(SogData data, int i)
        {
            var c = DecodeColorLinearAlpha(data, i);
            float a = Mathf.Clamp(c.w, 1e-6f, 1f - 1e-6f);
            c.w = Mathf.Log(a / (1f - a));
            return c;
        }

        public static Quaternion DecodeRotation(SogData data, int i)
        {
            var p = data.Quats[i];
            int largest = p.a - 252;
            if (largest < 0 || largest > 3)
                throw new InvalidDataException($"SOG: quats.webp pixel {i} has invalid alpha {p.a}; expected 252..255.");

            float r = DecodeRotationComponent(p.r);
            float g = DecodeRotationComponent(p.g);
            float b = DecodeRotationComponent(p.b);
            float missing = Mathf.Sqrt(Mathf.Max(0f, 1f - r * r - g * g - b * b));

            float q0, q1, q2, q3;
            switch (largest)
            {
                case 0: (q0, q1, q2, q3) = (missing, r, g, b); break;
                case 1: (q0, q1, q2, q3) = (r, missing, g, b); break;
                case 2: (q0, q1, q2, q3) = (r, g, missing, b); break;
                case 3: (q0, q1, q2, q3) = (r, g, b, missing); break;
                default: throw new InvalidOperationException();
            }

            return new Quaternion(q1, q2, q3, q0).normalized;
        }

        static float DecodeRotationComponent(byte value) =>
            ((value / 255.0f) * 2.0f - 1.0f) * Sqrt1_2;

        public static void DecodeShBand(SogData data, int i, int band, float[] output)
        {
            if (!data.HasShN)
                throw new InvalidOperationException("SOG: shN data is not present.");

            int label = DecodeShLabel(data, i);
            int coeffBase = GsplatUtils.SHBandsToCoefficientCount((byte)(band - 1));
            int bandSize = band * 2 + 1;
            var codebook = data.Meta.ShN.codebook;

            for (int k = 0; k < bandSize; k++)
            {
                int coeff = coeffBase + k;
                int u = (label % 64) * data.ShCoefficientCount + coeff;
                int v = label / 64;
                var p = data.ShNCentroids.Pixel(u, v);
                output[k * 3 + 0] = codebook[p.r];
                output[k * 3 + 1] = codebook[p.g];
                output[k * 3 + 2] = codebook[p.b];
            }
        }

        static int DecodeShLabel(SogData data, int i)
        {
            var p = data.ShNLabels[i];
            int label = p.r | (p.g << 8);
            if (label >= data.Meta.ShN.Count)
                throw new InvalidDataException($"SOG: shN label {label} at pixel {i} exceeds shN.count {data.Meta.ShN.Count}.");
            return label;
        }
    }
}
