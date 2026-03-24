// Copyright (c) 2026 Yize Wu
// SPDX-License-Identifier: MIT

#ifndef GSPLAT_UNCOMPRESSED_INCLUDED
#define GSPLAT_UNCOMPRESSED_INCLUDED

#include "Gsplat.hlsl"
StructuredBuffer<float3> _PositionBuffer;
StructuredBuffer<float3> _ScaleBuffer;
StructuredBuffer<float4> _RotationBuffer;
StructuredBuffer<float4> _ColorBuffer;

#ifndef SH_BANDS_0
StructuredBuffer<float3> _SHBuffer;
#endif

bool InitSplatData(SplatSource source, float4x4 modelView, out SplatCenter center, out SplatCorner corner,
                   out float4 color, float cullArea, float frustumMultiplier)
{
    float3 modelCenter = _PositionBuffer[source.id];
    if (!InitCenter(modelView, modelCenter, center))
        return false;
    float4 quat = _RotationBuffer[source.id];
    float3 scale = _ScaleBuffer[source.id];
    SplatCovariance cov = CalcCovariance(quat, scale);
    if (!InitCorner(source, cov, center, corner, cullArea, frustumMultiplier))
        return false;
    color = _ColorBuffer[source.id];
    color.rgb = color.rgb * SH_C0 + 0.5;
    return true;
}

#ifndef SH_BANDS_0

#ifdef SH_BANDS_1
#define SH_COEFFS 3
#elif defined(SH_BANDS_2)
#define SH_COEFFS 8
#elif defined(SH_BANDS_3)
#define SH_COEFFS 15
#endif

// see https://github.com/graphdeco-inria/gaussian-splatting/blob/main/utils/sh_utils.py
float3 EvalSH(float3 dir, uint id)
{
    float3 sh[SH_COEFFS];
        for (int i = 0; i < SH_COEFFS; i++)
            sh[i] = _SHBuffer[id * SH_COEFFS + i];

    float x = dir.x;
    float y = dir.y;
    float z = dir.z;

    float3 result = SH_C1 * (-sh[0] * y + sh[1] * z - sh[2] * x);

#if defined(SH_BANDS_2) || defined(SH_BANDS_3)
    // 2nd degree
    float xx = x * x;
    float yy = y * y;
    float zz = z * z;
    float xy = x * y;
    float yz = y * z;
    float xz = x * z;

    result = result + (
        sh[3] * (SH_C2_0 * xy) +
        sh[4] * (SH_C2_1 * yz) +
        sh[5] * (SH_C2_2 * (2.0 * zz - xx - yy)) +
        sh[6] * (SH_C2_3 * xz) +
        sh[7] * (SH_C2_4 * (xx - yy))
    );
#endif

#ifdef SH_BANDS_3
    // 3rd degree
    result = result + (
        sh[8] * (SH_C3_0 * y * (3.0 * xx - yy)) +
        sh[9] * (SH_C3_1 * xy * z) +
        sh[10] * (SH_C3_2 * y * (4.0 * zz - xx - yy)) +
        sh[11] * (SH_C3_3 * z * (2.0 * zz - 3.0 * xx - 3.0 * yy)) +
        sh[12] * (SH_C3_4 * x * (4.0 * zz - xx - yy)) +
        sh[13] * (SH_C3_5 * z * (xx - yy)) +
        sh[14] * (SH_C3_6 * x * (xx - 3.0 * yy))
    );
#endif

    return result;
}

#endif

#endif
