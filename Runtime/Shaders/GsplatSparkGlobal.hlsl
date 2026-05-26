// Copyright (c) 2025 Yize Wu
// Copyright (c) 2026 Keir Rice
// SPDX-License-Identifier: MIT
//
// Global-merge variant of GsplatSpark.hlsl.
// Reads splat data from concatenated global buffers instead of per-renderer buffers.
// Each GlobalOrderBuffer entry packs (renderer_id:8 | local_splat_id:24).
// Renderer transforms and global buffer offsets are provided via structured buffers.

#ifndef GSPLAT_SPARK_GLOBAL_INCLUDED
#define GSPLAT_SPARK_GLOBAL_INCLUDED

#include "Gsplat.hlsl"
#include "GsplatSpark.hlsl"

// Global concatenated data buffers (sub-allocated by renderer at registration time).
StructuredBuffer<uint4>    _GlobalPackedBuffer;
#ifndef SH_BANDS_0
StructuredBuffer<uint2>    _GlobalSH1Buffer;
#if defined(SH_BANDS_2) || defined(SH_BANDS_3) || defined(SH_BANDS_4)
StructuredBuffer<uint4>    _GlobalSH2Buffer;
#endif
#if defined(SH_BANDS_3) || defined(SH_BANDS_4)
StructuredBuffer<uint4>    _GlobalSH3Buffer;
#endif
#ifdef SH_BANDS_4
StructuredBuffer<uint4>    _GlobalSH4Buffer;
#endif
#endif

// Per-renderer metadata (updated when renderers register/move).
StructuredBuffer<uint>     _RendererOffsets;    // splat start index in global buffers, per renderer
StructuredBuffer<float4x4> _RendererTransforms; // localToWorldMatrix, per renderer

// Per-renderer visual settings, looked up by renderer_id. Matches RendererParams in GsplatSorter.cs.
struct RendererParams
{
    float brightness;
    float scaleFactor;
    uint  gammaToLinear;
    uint  shDegree;
};
StructuredBuffer<RendererParams> _RendererParams;

// Merged sorted index buffer (output of the merge pass).
// Entry: (renderer_id:8 | local_splat_id:24)
StructuredBuffer<uint>     _GlobalOrderBuffer;

uint _TotalSplatCount;

// Extended source struct for global rendering.
struct GlobalSplatSource
{
    uint order;
    uint rendererId;
    uint id;          // local splat ID within the renderer
    float2 cornerUV;
};

bool InitGlobalSource(uint instanceId, float3 vertex, out GlobalSplatSource source)
{
    source.order = instanceId * _SplatInstanceSize + asuint(vertex.z);

    if (source.order >= _TotalSplatCount)
        return false;

    uint packed     = _GlobalOrderBuffer[source.order];
    source.rendererId = packed >> 24u;
    source.id         = packed & 0x00FFFFFFu;
    source.cornerUV   = float2(vertex.x, vertex.y) * _RendererParams[source.rendererId].scaleFactor;
    return true;
}

bool InitGlobalSplatData(GlobalSplatSource source, out SplatCenter center, out SplatCorner corner,
                         out float4 color)
{
    center = (SplatCenter)0;
    corner = (SplatCorner)0;
    color  = float4(0, 0, 0, 0);

    uint globalIdx = _RendererOffsets[source.rendererId] + source.id;
    uint4 packedSplat = _GlobalPackedBuffer[globalIdx];

    float3 modelCenter, scale;
    float4 quat;
    UnpackSplat(packedSplat, color, modelCenter, scale, quat);

    float4x4 rendererTransform = _RendererTransforms[source.rendererId];
    float4x4 modelView = mul(UNITY_MATRIX_V, rendererTransform);

    if (!InitCenter(modelView, modelCenter, center))
        return false;

    // Re-use SplatSource.cornerUV via a temporary SplatSource for InitCorner.
    SplatSource tmp;
    tmp.order     = source.order;
    tmp.id        = source.id;
    tmp.cornerUV  = source.cornerUV;

    SplatCovariance cov = CalcCovariance(quat, scale);
    if (!InitCorner(tmp, cov, center, corner))
        return false;

    return true;
}

#ifndef SH_BANDS_0
void InitGlobalSH(GlobalSplatSource source, out float3 sh[SH_COEFFS])
{
    uint globalIdx = _RendererOffsets[source.rendererId] + source.id;

    uint2 packedSH1 = _GlobalSH1Buffer[globalIdx];
    #if defined(SH_BANDS_2) || defined(SH_BANDS_3) || defined(SH_BANDS_4)
    uint4 packedSH2 = _GlobalSH2Buffer[globalIdx];
    #endif
    #if defined(SH_BANDS_3) || defined(SH_BANDS_4)
    uint4 packedSH3 = _GlobalSH3Buffer[globalIdx];
    #endif
    #ifdef SH_BANDS_4
    uint4 packedSH4 = _GlobalSH4Buffer[globalIdx];
    #endif

    // Decode identically to GsplatSpark.hlsl InitSH.
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
    sh[3] = float3(
        int(packedSH2.x << 24u) >> 24,
        int(packedSH2.x << 16u) >> 24,
        int(packedSH2.x << 8u)  >> 24
    ) / 127.0;
    sh[4] = float3(
        int(packedSH2.x) >> 24,
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
        int(packedSH2.z << 8u)  >> 24,
        int(packedSH2.z) >> 24
    ) / 127.0;
    sh[7] = float3(
        int(packedSH2.w << 24u) >> 24,
        int(packedSH2.w << 16u) >> 24,
        int(packedSH2.w << 8u)  >> 24
    ) / 127.0;
    #endif
    #if defined(SH_BANDS_3) || defined(SH_BANDS_4)
    sh[8] = float3(
        int(packedSH3.x << 26u) >> 26,
        int(packedSH3.x << 20u) >> 26,
        int(packedSH3.x << 14u) >> 26
    ) / 31.0;
    sh[9] = float3(
        int(packedSH3.x << 8u) >> 26,
        int(packedSH3.x << 2u) >> 26,
        int((packedSH3.x >> 4u) | (packedSH3.y << 28u)) >> 26
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
        int(packedSH3.z << 6u)  >> 26
    ) / 31.0;
    sh[13] = float3(
        int(packedSH3.z) >> 26,
        int(packedSH3.w << 26u) >> 26,
        int(packedSH3.w << 20u) >> 26
    ) / 31.0;
    sh[14] = float3(
        int(packedSH3.w << 14u) >> 26,
        int(packedSH3.w << 8u)  >> 26,
        int(packedSH3.w << 2u)  >> 26
    ) / 31.0;
    #endif
    #ifdef SH_BANDS_4
    // Extract sint4 values packed into 4 x uint32 (8 values per word; 5 unused tail slots).
    // value i (i in [0..26]) lives in word i/8 at bit offset (i%8)*4.
    sh[15] = float3(
        int(packedSH4.x << 28u) >> 28,
        int(packedSH4.x << 24u) >> 28,
        int(packedSH4.x << 20u) >> 28
    ) / 7.0;
    sh[16] = float3(
        int(packedSH4.x << 16u) >> 28,
        int(packedSH4.x << 12u) >> 28,
        int(packedSH4.x << 8u)  >> 28
    ) / 7.0;
    sh[17] = float3(
        int(packedSH4.x << 4u) >> 28,
        int(packedSH4.x) >> 28,
        int(packedSH4.y << 28u) >> 28
    ) / 7.0;
    sh[18] = float3(
        int(packedSH4.y << 24u) >> 28,
        int(packedSH4.y << 20u) >> 28,
        int(packedSH4.y << 16u) >> 28
    ) / 7.0;
    sh[19] = float3(
        int(packedSH4.y << 12u) >> 28,
        int(packedSH4.y << 8u)  >> 28,
        int(packedSH4.y << 4u)  >> 28
    ) / 7.0;
    sh[20] = float3(
        int(packedSH4.y) >> 28,
        int(packedSH4.z << 28u) >> 28,
        int(packedSH4.z << 24u) >> 28
    ) / 7.0;
    sh[21] = float3(
        int(packedSH4.z << 20u) >> 28,
        int(packedSH4.z << 16u) >> 28,
        int(packedSH4.z << 12u) >> 28
    ) / 7.0;
    sh[22] = float3(
        int(packedSH4.z << 8u) >> 28,
        int(packedSH4.z << 4u) >> 28,
        int(packedSH4.z) >> 28
    ) / 7.0;
    sh[23] = float3(
        int(packedSH4.w << 28u) >> 28,
        int(packedSH4.w << 24u) >> 28,
        int(packedSH4.w << 20u) >> 28
    ) / 7.0;
    #endif
}
#endif // !SH_BANDS_0

#endif // GSPLAT_SPARK_GLOBAL_INCLUDED
