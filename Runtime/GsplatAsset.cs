// Copyright (c) 2025 Yize Wu
// SPDX-License-Identifier: MIT

using UnityEngine;

namespace Gsplat
{
    public class GsplatAsset : ScriptableObject
    {
        public uint SplatCount;
        public byte SHBands; // 0, 1, 2, or 3
        public Bounds Bounds;
        [HideInInspector] public uint[] PackedSH1;
        [HideInInspector] public uint[] PackedSH2;
        [HideInInspector] public uint[] PackedSH3;
        [HideInInspector] public uint[] PackedSplats;
    }
}
