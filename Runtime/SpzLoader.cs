// Copyright (c) 2025 Niantic Spatial
// SPDX-License-Identifier: MIT

// SPZ format specification: https://github.com/nianticlabs/spz
// Decoding logic ported from nianticlabs/spz/src/cc/load-spz.cc

using System;
using System.IO;
using System.IO.Compression;
using Gsplat.Internal;
using UnityEngine;

namespace Gsplat
{
    internal struct SpzHeader
    {
        public uint Version;
        public uint NumPoints;
        public byte ShDegree;
        public byte FractionalBits;
        public byte Flags;
    }

    internal class SpzData
    {
        public SpzHeader Header;
        public byte[] Positions;  // v1: NumPoints*6 (float16 xyz), v2+: NumPoints*9 (24-bit fixed xyz)
        public byte[] Alphas;     // NumPoints * 1
        public byte[] Colors;     // NumPoints * 3 (quantized SH DC per channel)
        public byte[] Scales;     // NumPoints * 3 (quantized log-scale per axis)
        public byte[] Rotations;  // v1-2: NumPoints*3 (xyz as unsigned bytes, offset-encoded), v3+: NumPoints*4 (smallest-three)
        public byte[] SH;         // NumPoints * ShDim * 3, layout per point: [R0,G0,B0, R1,G1,B1, ..., R_{Dim-1},G_{Dim-1},B_{Dim-1}]
    }

    internal static class SpzLoader
    {
        const uint SpzMagic = 0x5053474e; // "NGSP" little-endian
        const int V4HeaderSize = 32;
        const int V4ExpectedStreams = 6;
        // Stream index → human-readable name, matching the order written by the C++ writer
        // (load-spz.cc serializeNgsp). Used purely for error messages.
        static readonly string[] V4StreamNames = { "positions", "alphas", "colors", "scales", "rotations", "sh" };
        // Hard cap on splat count. 128M is well above any realistic scene and keeps
        // (long)n * stride safe for every multiplication we do (worst-case stride is
        // 72 bytes/point for degree-4 SH, so 128M * 72 ≪ int.MaxValue).
        const uint MaxSupportedPointCount = 1u << 27;
        // Matches the value hardcoded by the upstream C++ writer; reject anything else
        // so a malformed/hostile fractionalBits doesn't silently rescale positions.
        const byte MaxFractionalBits = 12;

        const float ColorScale = 0.15f;
        const float Sqrt1_2 = 0.70710678118f;

        // Number of SH coefficients per color channel, excluding DC (degree 0).
        // Matches GsplatUtils.SHBandsToCoefficientCount.
        public static int ShDim(byte degree) => degree * (degree + 2);

        static int ValidatePointCount(uint numPoints)
        {
            if (numPoints > MaxSupportedPointCount)
                throw new NotSupportedException(
                    $"SPZ: point count {numPoints} exceeds supported maximum {MaxSupportedPointCount}");
            return (int)numPoints;
        }

        static void ValidateFractionalBits(byte fractionalBits)
        {
            if (fractionalBits > MaxFractionalBits)
                throw new NotSupportedException(
                    $"SPZ: fractionalBits {fractionalBits} exceeds supported maximum {MaxFractionalBits}");
        }

        public static SpzData Load(string path)
        {
            using var fs = new FileStream(path, FileMode.Open, FileAccess.Read);
            if (fs.Length < 4)
                throw new InvalidDataException($"{Path.GetFileName(path)} is too short to be an SPZ file");

            var sniff = ReadExact(fs, 4);
            fs.Position = 0;

            // v1–3: single gzip stream wrapping the 16-byte SPZ header + sequential attribute streams.
            if (sniff[0] == 0x1F && sniff[1] == 0x8B) return LoadGzip(fs);

            // v4: plaintext 32-byte header with "NGSP" magic, then optional extensions, TOC, zstd-per-attribute streams.
            uint magic = BitConverter.ToUInt32(sniff, 0);
            if (magic == SpzMagic) return LoadZstd(fs);

            throw new NotSupportedException(
                $"{Path.GetFileName(path)} is not a recognized SPZ file (no gzip or NGSP magic).");
        }

        // v1–3: gzip-wrapped payload.
        static SpzData LoadGzip(FileStream fs)
        {
            using var gz = new GZipStream(fs, System.IO.Compression.CompressionMode.Decompress);

            var headerBytes = new byte[16];
            if (gz.Read(headerBytes, 0, 16) != 16)
                throw new InvalidDataException("SPZ file too short to contain header");

            var header = ParseHeaderGzip(headerBytes);
            return ReadStreams(gz, header);
        }

        static SpzHeader ParseHeaderGzip(byte[] b)
        {
            uint magic = BitConverter.ToUInt32(b, 0);
            if (magic != SpzMagic)
                throw new InvalidDataException($"SPZ: bad magic 0x{magic:X8}, expected 0x{SpzMagic:X8}");

            uint version = BitConverter.ToUInt32(b, 4);
            if (version < 1 || version > 3)
                throw new NotSupportedException(
                    $"SPZ version {version} is not supported in the gzip container (expected 1–3).");

            byte fractionalBits = b[13];
            ValidateFractionalBits(fractionalBits);

            return new SpzHeader
            {
                Version = version,
                NumPoints = BitConverter.ToUInt32(b, 8),
                ShDegree = b[12],
                FractionalBits = fractionalBits,
                Flags = b[14],
            };
        }

        static SpzData ReadStreams(Stream stream, SpzHeader h)
        {
            int n = ValidatePointCount(h.NumPoints);
            bool float16Pos = h.Version == 1;
            bool smallestThree = h.Version >= 3;
            int shDim = ShDim(h.ShDegree);

            return new SpzData
            {
                Header = h,
                Positions = ReadExact(stream, float16Pos ? n * 6 : n * 9),
                Alphas = ReadExact(stream, n),
                Colors = ReadExact(stream, n * 3),
                Scales = ReadExact(stream, n * 3),
                Rotations = ReadExact(stream, smallestThree ? n * 4 : n * 3),
                SH = ReadExact(stream, n * shDim * 3),
            };
        }

        // v4: 32-byte plaintext header, optional extensions, TOC, then N zstd-compressed streams.
        // Stream order (numStreams == 6): positions, alphas, colors, scales, rotations, sh.
        static SpzData LoadZstd(FileStream fs)
        {
            var hb = ReadExact(fs, V4HeaderSize);
            uint magic = BitConverter.ToUInt32(hb, 0);
            if (magic != SpzMagic)
                throw new InvalidDataException($"SPZ v4: bad magic 0x{magic:X8}");

            uint version = BitConverter.ToUInt32(hb, 4);
            if (version != 4)
                throw new NotSupportedException($"SPZ NGSP container with version {version} is not supported (expected 4).");

            uint numPoints = BitConverter.ToUInt32(hb, 8);
            byte shDegree = hb[12];
            byte fractionalBits = hb[13];
            byte flags = hb[14];
            byte numStreams = hb[15];
            uint tocByteOffset = BitConverter.ToUInt32(hb, 16);
            // hb[20..32] are reserved (must be zero); not validated.

            if (shDegree > 4)
                throw new NotSupportedException($"SPZ v4 SH degree {shDegree} is out of spec (max 4).");
            ValidateFractionalBits(fractionalBits);
            if (numStreams != V4ExpectedStreams)
                throw new NotSupportedException(
                    $"SPZ v4 with {numStreams} streams is not supported (expected {V4ExpectedStreams}).");
            long tocBytes = (long)V4ExpectedStreams * 16;
            if (tocByteOffset < V4HeaderSize)
                throw new InvalidDataException($"SPZ v4: tocByteOffset {tocByteOffset} overlaps header.");
            if (tocByteOffset + tocBytes > fs.Length)
                throw new InvalidDataException(
                    $"SPZ v4: tocByteOffset {tocByteOffset} + TOC size {tocBytes} is past EOF (file length {fs.Length}).");

            // Extensions, if present, live between the header and tocByteOffset. We don't
            // consume any defined extensions, so just seek past them.
            fs.Position = tocByteOffset;
            var toc = ReadExact(fs, (int)tocBytes);

            int n = ValidatePointCount(numPoints);
            int shDim = ShDim(shDegree);
            var expectedSizes = new long[V4ExpectedStreams]
            {
                (long)n * 9,           // positions: 24-bit fixed (v4 always)
                n,                     // alphas
                (long)n * 3,           // colors
                (long)n * 3,           // scales
                (long)n * 4,           // rotations: smallest-three (v4 always)
                (long)n * shDim * 3,   // sh
            };

            var streams = new byte[V4ExpectedStreams][];
            using var zstd = new ZstdDecoderSession();
            for (int i = 0; i < V4ExpectedStreams; i++)
            {
                string name = V4StreamNames[i];
                ulong compressedSize = BitConverter.ToUInt64(toc, i * 16);
                ulong uncompressedSize = BitConverter.ToUInt64(toc, i * 16 + 8);
                if (uncompressedSize != (ulong)expectedSizes[i])
                    throw new InvalidDataException(
                        $"SPZ v4 stream {i} ({name}): TOC uncompressedSize {uncompressedSize} != expected {expectedSizes[i]}");
                if (compressedSize > int.MaxValue || uncompressedSize > int.MaxValue)
                    throw new InvalidDataException(
                        $"SPZ v4 stream {i} ({name}): size exceeds 2GB limit");
                // zstd's worst-case expansion is ZSTD_COMPRESSBOUND ≈ src + src/128 + ~512 bytes.
                // Add 4 KB of headroom for frame metadata (header, dictionary id, checksum,
                // multi-frame splits). Anything beyond this is a malformed or hostile file
                // claiming a small uncompressed size but a huge compressed payload.
                ulong maxCompressed = uncompressedSize + uncompressedSize / 128 + 4096;
                if (compressedSize > maxCompressed)
                    throw new InvalidDataException(
                        $"SPZ v4 stream {i} ({name}): compressedSize {compressedSize} exceeds zstd worst-case bound " +
                        $"{maxCompressed} for uncompressedSize {uncompressedSize}");

                // Degenerate empty stream (numPoints == 0). Skip the zstd round-trip entirely
                // rather than asking the decoder to write into a zero-length destination, which
                // ZstdSharp has historically been brittle about.
                if (uncompressedSize == 0)
                {
                    fs.Seek((long)compressedSize, SeekOrigin.Current);
                    streams[i] = Array.Empty<byte>();
                    continue;
                }

                var compressed = ReadExact(fs, (int)compressedSize);
                var dst = new byte[(int)uncompressedSize];
                int written;
                try
                {
                    written = zstd.Decompress(compressed, dst);
                }
                catch (Exception e)
                {
                    throw new InvalidDataException(
                        $"SPZ v4 stream {i} ({name}): zstd decompression failed: {e.Message}", e);
                }
                if (written != (int)uncompressedSize)
                    throw new InvalidDataException(
                        $"SPZ v4 stream {i} ({name}): zstd produced {written} bytes, expected {uncompressedSize}");
                streams[i] = dst;
            }

            return new SpzData
            {
                Header = new SpzHeader
                {
                    Version = version,
                    NumPoints = numPoints,
                    ShDegree = shDegree,
                    FractionalBits = fractionalBits,
                    Flags = flags,
                },
                Positions = streams[0],
                Alphas = streams[1],
                Colors = streams[2],
                Scales = streams[3],
                Rotations = streams[4],
                SH = streams[5],
            };
        }

        static byte[] ReadExact(Stream stream, int count)
        {
            if (count == 0) return Array.Empty<byte>();
            var buf = new byte[count];
            int offset = 0;
            while (offset < count)
            {
                int read = stream.Read(buf, offset, count - offset);
                if (read == 0) throw new EndOfStreamException("Unexpected end of SPZ payload");
                offset += read;
            }
            return buf;
        }

        // Decode 24-bit signed fixed-point XYZ position (v2+).
        public static Vector3 DecodePosition(byte[] positions, int i, byte fractionalBits)
        {
            float scale = 1.0f / (1 << fractionalBits);
            int b = i * 9;
            return new Vector3(
                Fixed24ToFloat(positions, b + 0) * scale,
                Fixed24ToFloat(positions, b + 3) * scale,
                Fixed24ToFloat(positions, b + 6) * scale);
        }

        // Decode float16 XYZ position (v1 only).
        public static Vector3 DecodePositionFloat16(byte[] positions, int i)
        {
            int b = i * 6;
            return new Vector3(
                Mathf.HalfToFloat(BitConverter.ToUInt16(positions, b + 0)),
                Mathf.HalfToFloat(BitConverter.ToUInt16(positions, b + 2)),
                Mathf.HalfToFloat(BitConverter.ToUInt16(positions, b + 4)));
        }

        static float Fixed24ToFloat(byte[] buf, int offset)
        {
            int v = buf[offset] | (buf[offset + 1] << 8) | (buf[offset + 2] << 16);
            if ((v & 0x800000) != 0) v |= unchecked((int)0xFF000000);
            return v;
        }

        // Decode opacity as logit (pre-sigmoid), matching what PLY stores and PackSplat expects.
        public static float DecodeAlphaLogit(byte[] alphas, int i)
        {
            float a = Mathf.Clamp(alphas[i] / 255.0f, 1e-6f, 1f - 1e-6f);
            return Mathf.Log(a / (1f - a));
        }

        // Decode opacity as [0,1] post-sigmoid value, for GsplatAssetUncompressed.
        public static float DecodeAlphaLinear(byte[] alphas, int i) => alphas[i] / 255.0f;

        // Decode quantized color to raw SH DC coefficient (same form as PLY's f_dc_0/1/2).
        public static Vector3 DecodeColor(byte[] colors, int i)
        {
            int b = i * 3;
            return new Vector3(
                (colors[b + 0] / 255.0f - 0.5f) / ColorScale,
                (colors[b + 1] / 255.0f - 0.5f) / ColorScale,
                (colors[b + 2] / 255.0f - 0.5f) / ColorScale);
        }

        // Decode quantized log-scale to ln(scale), matching PLY's scale_0/1/2.
        public static Vector3 DecodeScaleLog(byte[] scales, int i)
        {
            int b = i * 3;
            return new Vector3(
                scales[b + 0] / 16.0f - 10.0f,
                scales[b + 1] / 16.0f - 10.0f,
                scales[b + 2] / 16.0f - 10.0f);
        }

        public static Quaternion DecodeRotation(byte[] rotations, int i, bool usesSmallestThree)
        {
            return usesSmallestThree
                ? DecodeSmallestThree(rotations, i * 4)
                : DecodeXyz3Bytes(rotations, i * 3);
        }

        // v1-2: xyz stored as unsigned bytes with offset encoding: x = byte/127.5 - 1.0.
        // byte 0 = -1.0, byte 127/128 ≈ 0.0, byte 255 = +1.0. w is derived (always non-negative).
        static Quaternion DecodeXyz3Bytes(byte[] r, int offset)
        {
            float x = r[offset + 0] / 127.5f - 1.0f;
            float y = r[offset + 1] / 127.5f - 1.0f;
            float z = r[offset + 2] / 127.5f - 1.0f;
            float w = Mathf.Sqrt(Mathf.Max(0f, 1f - x * x - y * y - z * z));
            return new Quaternion(x, y, z, w);
        }

        // v3+: smallest-three quaternion. 32 bits: 2-bit index of largest component,
        // then three components each as (9-bit magnitude, 1-bit sign). Components are
        // packed high-index-first at LSB: index 3 gets bits[0:9], index 2 gets bits[10:19], etc.
        static Quaternion DecodeSmallestThree(byte[] r, int offset)
        {
            uint comp = r[offset]
                | ((uint)r[offset + 1] << 8)
                | ((uint)r[offset + 2] << 16)
                | ((uint)r[offset + 3] << 24);
            const uint cMask = (1u << 9) - 1u;
            int iLargest = (int)(comp >> 30);
            float[] q = new float[4];
            float sumSq = 0f;

            for (int i = 3; i >= 0; i--)
            {
                if (i == iLargest) continue;
                uint mag = comp & cMask;
                uint neg = (comp >> 9) & 1u;
                comp >>= 10;
                float val = Sqrt1_2 * mag / (float)cMask;
                if (neg == 1) val = -val;
                q[i] = val;
                sumSq += val * val;
            }
            q[iLargest] = Mathf.Sqrt(Mathf.Max(0f, 1f - sumSq));
            return new Quaternion(q[0], q[1], q[2], q[3]);
        }

        // Dequantize a single SH byte: (x - 128) / 128.
        public static float UnquantizeSH(byte[] sh, int byteIndex)
            => (sh[byteIndex] - 128f) / 128f;
    }
}
