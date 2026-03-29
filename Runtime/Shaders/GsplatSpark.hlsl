// Copyright (c) 2026 Arthur Aillet, Yize Wu
// SPDX-License-Identifier: MIT

#ifndef GSPLAT_SPARK_INCLUDED
#define GSPLAT_SPARK_INCLUDED

#include "Gsplat.hlsl"
StructuredBuffer<uint4> _PackedSplatsBuffer;

// Implementation taken from spark.js
// Decode a 24‐bit encoded uint into a quaternion (float4) using the folded octahedral inverse.
float4 DecodeQuatOctXyz88R8(uint encoded)
{
    // Extract the fields.
    uint quantU = encoded & 0xFFU; // bits 0–7
    uint quantV = (encoded >> 8u) & 0xFFU; // bits 8–15
    uint angleInt = encoded >> 16u; // bits 16–23

    // Recover u and v in [0,1], then map to [-1,1].
    float u_f = float(quantU) / 255.0;
    float v_f = float(quantV) / 255.0;
    float2 f = float2(u_f * 2.0 - 1.0, v_f * 2.0 - 1.0);

    float3 axis = float3(f.xy, 1.0 - abs(f.x) - abs(f.y));
    float t = max(-axis.z, 0.0);
    axis.x += (axis.x >= 0.0) ? -t : t;
    axis.y += (axis.y >= 0.0) ? -t : t;
    axis = normalize(axis);

    // Decode the angle θ ∈ [0,π].
    float theta = (float(angleInt) / 255.0) * UNITY_PI;
    float halfTheta = theta * 0.5;
    float s = sin(halfTheta);
    float w = cos(halfTheta);

    return float4(axis * s, w);
}

#define LN_SCALE_MIN -12.0
#define LN_SCALE_MAX 9.0

void UnpackSplat(uint4 packedData, out float4 color, out float3 modelCenter, out float3 scale, out float4 quat)
{
    uint word0 = packedData.x;
    uint word1 = packedData.y;
    uint word2 = packedData.z;
    uint word3 = packedData.w;

    uint4 uColor = uint4(word0 & 0xFFU, (word0 >> 8u) & 0xFFU, (word0 >> 16u) & 0xFFU, (word0 >> 24u) & 0xFFU);
    color = (float4(uColor) / 255.0);

    modelCenter = float3(f16tof32(word1 & 0xFFFFU), f16tof32((word1 >> 16u) & 0xFFFFU), f16tof32(word2 & 0xFFFFU));

    uint3 uScale = uint3(word3 & 0xFFU, (word3 >> 8u) & 0xFFU, (word3 >> 16u) & 0xFFU);
    float lnScaleScale = (LN_SCALE_MAX - LN_SCALE_MIN) / 254.0;
    scale = float3(
        (uScale.x == 0u) ? 0.0 : exp(LN_SCALE_MIN + float(uScale.x - 1u) * lnScaleScale),
        (uScale.y == 0u) ? 0.0 : exp(LN_SCALE_MIN + float(uScale.y - 1u) * lnScaleScale),
        (uScale.z == 0u) ? 0.0 : exp(LN_SCALE_MIN + float(uScale.z - 1u) * lnScaleScale)
    );

    uint uQuat = ((word2 >> 16u) & 0xFFFFU) | ((word3 >> 8u) & 0xFF0000U);
    quat = DecodeQuatOctXyz88R8(uQuat);
}

bool InitSplatData(SplatSource source, float4x4 modelView, out SplatCenter center, out SplatCorner corner,
                   out float4 color)
{
    uint4 packedSplat = _PackedSplatsBuffer[source.id];
    float3 modelCenter, scale;
    float4 quat;
    UnpackSplat(packedSplat, color, modelCenter, scale, quat);
    if (!InitCenter(modelView, modelCenter, center))
        return false;
    SplatCovariance cov = CalcCovariance(quat, scale);
    if (!InitCorner(source, cov, center, corner))
        return false;
    return true;
}

#endif
