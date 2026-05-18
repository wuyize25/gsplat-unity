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

#ifndef SH_BANDS_0
StructuredBuffer<uint2> _PackedSH1Buffer;
#if defined(SH_BANDS_2) || defined(SH_BANDS_3) || defined(SH_BANDS_4)
StructuredBuffer<uint4> _PackedSH2Buffer;
#endif
#if defined(SH_BANDS_3) || defined(SH_BANDS_4)
StructuredBuffer<uint4> _PackedSH3Buffer;
#endif
#ifdef SH_BANDS_4
StructuredBuffer<uint4> _PackedSH4Buffer;
#endif

void InitSH(uint id, out float3 sh[SH_COEFFS])
{
    uint2 packedSH1 = _PackedSH1Buffer[id];
    #if defined(SH_BANDS_2) || defined(SH_BANDS_3) || defined(SH_BANDS_4)
    uint4 packedSH2 = _PackedSH2Buffer[id];
    #endif
    #if defined(SH_BANDS_3) || defined(SH_BANDS_4)
    uint4 packedSH3 = _PackedSH3Buffer[id];
    #endif
    #ifdef SH_BANDS_4
    uint4 packedSH4 = _PackedSH4Buffer[id];
    #endif

    // Extract sint7 values packed into 2 x uint32
    sh[0] = float3(
        int(packedSH1.x << 25u) >> 25,
        int(packedSH1.x << 18u) >> 25,
        int(packedSH1.x << 11u) >> 25
    ) / 63.0;
    sh[1] = float3(
        int(packedSH1.x << 4u) >> 25,
        int((packedSH1.x >> 3u) | (packedSH1.y << 29u)) >> 25,
        int(packedSH1.y << 22u) >> 25
    ) / 63.0;
    sh[2] = float3(
        int(packedSH1.y << 15u) >> 25,
        int(packedSH1.y << 8u) >> 25,
        int(packedSH1.y << 1u) >> 25
    ) / 63.0;
    #if defined(SH_BANDS_2) || defined(SH_BANDS_3) || defined(SH_BANDS_4)
    // Extract sint8 values packed into 4 x uint32
    sh[3] = float3(
        int(packedSH2.x << 24u) >> 24,
        int(packedSH2.x << 16u) >> 24,
        int(packedSH2.x << 8u) >> 24
    ) / 127.0;
    sh[4] = float3(
        int(packedSH2.x) >> 24u,
        int(packedSH2.y << 24u) >> 24,
        int(packedSH2.y << 16u) >> 24
    ) / 127.0;
    sh[5] = float3(
        int(packedSH2.y << 8u) >> 24,
        int(packedSH2.y) >> 24,
        int(packedSH2.z << 24u) >> 24
    ) / 127.0;
    sh[6] = float3(
        int(packedSH2.z << 16u) >> 24,
        int(packedSH2.z << 8u) >> 24,
        int(packedSH2.z) >> 24
    ) / 127.0;
    sh[7] = float3(
        int(packedSH2.w << 24u) >> 24,
        int(packedSH2.w << 16u) >> 24,
        int(packedSH2.w << 8u) >> 24
    ) / 127.0;
    #endif
    #if defined(SH_BANDS_3) || defined(SH_BANDS_4)
    // Extract sint6 values packed into 4 x uint32
    sh[8] = float3(
        int(packedSH3.x << 26u) >> 26,
        int(packedSH3.x << 20u) >> 26,
        int(packedSH3.x << 14u) >> 26
    ) / 31.0;
    sh[9] = float3(
        int(packedSH3.x << 8u) >> 26,
        int(packedSH3.x << 2u) >> 26,
        int((packedSH3.x >> 4u) | (packedSH3.y << 28)) >> 26
    ) / 31.0;
    sh[10] = float3(
        int(packedSH3.y << 22u) >> 26,
        int(packedSH3.y << 16u) >> 26,
        int(packedSH3.y << 10u) >> 26
    ) / 31.0;
    sh[11] = float3(
        int(packedSH3.y << 4u) >> 26,
        int((packedSH3.y >> 2u) | (packedSH3.z << 30u)) >> 26,
        int(packedSH3.z << 24u) >> 26
    ) / 31.0;
    sh[12] = float3(
        int(packedSH3.z << 18u) >> 26,
        int(packedSH3.z << 12u) >> 26,
        int(packedSH3.z << 6u) >> 26
    ) / 31.0;
    sh[13] = float3(
        int(packedSH3.z) >> 26,
        int(packedSH3.w << 26u) >> 26,
        int(packedSH3.w << 20u) >> 26
    ) / 31.0;
    sh[14] = float3(
        int(packedSH3.w << 14u) >> 26,
        int(packedSH3.w << 8u) >> 26,
        int(packedSH3.w << 2u) >> 26
    ) / 31.0;
    #endif
    #ifdef SH_BANDS_4
    // Extract sint4 values packed into 4 x uint32 (8 values per word; 5 unused tail slots).
    // value i (i in [0..26]) lives in word i/8 at bit offset (i%8)*4.
    // sh[15] = values 0,1,2 → word 0 bits 0,4,8
    sh[15] = float3(
        int(packedSH4.x << 28u) >> 28,
        int(packedSH4.x << 24u) >> 28,
        int(packedSH4.x << 20u) >> 28
    ) / 7.0;
    // sh[16] = values 3,4,5 → word 0 bits 12,16,20
    sh[16] = float3(
        int(packedSH4.x << 16u) >> 28,
        int(packedSH4.x << 12u) >> 28,
        int(packedSH4.x << 8u) >> 28
    ) / 7.0;
    // sh[17] = values 6,7,8 → word 0 bits 24,28 + word 1 bit 0
    sh[17] = float3(
        int(packedSH4.x << 4u) >> 28,
        int(packedSH4.x) >> 28,
        int(packedSH4.y << 28u) >> 28
    ) / 7.0;
    // sh[18] = values 9,10,11 → word 1 bits 4,8,12
    sh[18] = float3(
        int(packedSH4.y << 24u) >> 28,
        int(packedSH4.y << 20u) >> 28,
        int(packedSH4.y << 16u) >> 28
    ) / 7.0;
    // sh[19] = values 12,13,14 → word 1 bits 16,20,24
    sh[19] = float3(
        int(packedSH4.y << 12u) >> 28,
        int(packedSH4.y << 8u) >> 28,
        int(packedSH4.y << 4u) >> 28
    ) / 7.0;
    // sh[20] = values 15,16,17 → word 1 bit 28 + word 2 bits 0,4
    sh[20] = float3(
        int(packedSH4.y) >> 28,
        int(packedSH4.z << 28u) >> 28,
        int(packedSH4.z << 24u) >> 28
    ) / 7.0;
    // sh[21] = values 18,19,20 → word 2 bits 8,12,16
    sh[21] = float3(
        int(packedSH4.z << 20u) >> 28,
        int(packedSH4.z << 16u) >> 28,
        int(packedSH4.z << 12u) >> 28
    ) / 7.0;
    // sh[22] = values 21,22,23 → word 2 bits 20,24,28
    sh[22] = float3(
        int(packedSH4.z << 8u) >> 28,
        int(packedSH4.z << 4u) >> 28,
        int(packedSH4.z) >> 28
    ) / 7.0;
    // sh[23] = values 24,25,26 → word 3 bits 0,4,8
    sh[23] = float3(
        int(packedSH4.w << 28u) >> 28,
        int(packedSH4.w << 24u) >> 28,
        int(packedSH4.w << 20u) >> 28
    ) / 7.0;
    #endif
}
#endif

#endif
