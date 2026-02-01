// Copyright (c) 2025 Yize Wu
// SPDX-License-Identifier: MIT

using Unity.Mathematics;
using UnityEngine;


namespace Gsplat
{
    public class GsplatDataSpark : GsplatData
    {
        [HideInInspector] public Vector3[] SHs;
        [HideInInspector] public uint4[] PackedSplats;
    }
}