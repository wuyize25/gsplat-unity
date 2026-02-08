// Copyright (c) 2026 Yize Wu
// SPDX-License-Identifier: MIT

#ifndef GSPLAT_UNCOMPRESSED_INCLUDED
#define GSPLAT_UNCOMPRESSED_INCLUDED

#include "Gsplat.hlsl"
StructuredBuffer<float3> _PositionBuffer;
StructuredBuffer<float3> _ScaleBuffer;
StructuredBuffer<float4> _RotationBuffer;
StructuredBuffer<float4> _ColorBuffer;

bool InitSplatData(SplatSource source, float4x4 modelView, out SplatCenter center, out SplatCorner corner,
                   out float4 color)
{
    float3 modelCenter = _PositionBuffer[source.id];
    if (!InitCenter(modelView, modelCenter, center))
        return false;
    float4 quat = _RotationBuffer[source.id];
    float3 scale = _ScaleBuffer[source.id];
    SplatCovariance cov = CalcCovariance(quat, scale);
    if (!InitCorner(source, cov, center, corner))
        return false;
    color = _ColorBuffer[source.id];
    color.rgb = color.rgb * SH_C0 + 0.5;
    return true;
}

#endif
