// Copyright (c) 2025 Yize Wu
// SPDX-License-Identifier: MIT

using UnityEngine;

namespace Gsplat
{
    public enum CompressionMode
    {
        Uncompressed,
        Spark
    }

    public class GsplatAsset : ScriptableObject
    {
        public uint SplatCount;
        public byte SHBands; // 0, 1, 2, or 3
        public Bounds Bounds;
        public CompressionMode Compression;
        public GsplatData Data;
    }
}