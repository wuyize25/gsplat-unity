// Copyright (c) 2025 Yize Wu
// SPDX-License-Identifier: MIT

using UnityEngine;

namespace Gsplat
{
    [System.Serializable]
    public class SHBand
    {
        public Vector3[] SHs;

        public SHBand(Vector3[] shs)
        {
            SHs = shs;
        }
    }

    public class GsplatAsset : ScriptableObject
    {
        public uint SplatCount;
        public byte SHBands; // 0, 1, 2, or 3
        public Bounds Bounds;
        [HideInInspector] public SHBand[] SHs;
        [HideInInspector] public uint[] PackedSplats;
    }
}
