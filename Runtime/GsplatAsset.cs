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
        [HideInInspector] public Vector3[] Positions;
        [HideInInspector] public Vector4[] Colors; // RGB, Opacity
        [HideInInspector] public Vector3[] SHs;
        [HideInInspector] public Vector3[] Scales;
        [HideInInspector] public Vector4[] Rotations; // Quaternion, wxyz
    }
}