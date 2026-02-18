// Gaussian Splatting Spherical Harmonics unpacking and evaluation functions
// most of these are from https://github.com/sparkjsdev/spark/blob/main/src/SplatMesh.ts
// Copyright (c) 2025 sparkjs
// Copyright (c) 2026 Arthur Aillet
// SPDX-License-Identifier: MIT

#ifndef SH_BANDS_0

#define SH_C1 0.4886025119029199f
#define SH_C2_0 1.0925484305920792f
#define SH_C2_1 -1.0925484305920792f
#define SH_C2_2 0.31539156525252005f
#define SH_C2_3 -1.0925484305920792f
#define SH_C2_4 0.5462742152960396f
#define SH_C3_0 -0.5900435899266435f
#define SH_C3_1 2.890611442640554f
#define SH_C3_2 -0.4570457994644658f
#define SH_C3_3 0.3731763325901154f
#define SH_C3_4 -0.4570457994644658f
#define SH_C3_5 1.445305721320277f
#define SH_C3_6 -0.5900435899266435f

float3 EvalSH(
    uint2 packedSH1,

#if defined(SH_BANDS_2) || defined(SH_BANDS_3)
    uint4 packedSH2,
#endif

#ifdef SH_BANDS_3
    uint4 packedSH3,
#endif

    float3 dir
) {
    float3 sh;

    // Extract sint7 values packed into 2 x uint32
    float3 sh1_0 = float3(
        int(packedSH1.x << 25u) >> 25,
        int(packedSH1.x << 18u) >> 25,
        int(packedSH1.x << 11u) >> 25
    ) / 63.0;
    float3 sh1_1 = float3(
        int(packedSH1.x << 4u) >> 25,
        int((packedSH1.x >> 3u) | (packedSH1.y << 29u)) >> 25,
        int(packedSH1.y << 22u) >> 25
    ) / 63.0;
    float3 sh1_2 = float3(
        int(packedSH1.y << 15u) >> 25,
        int(packedSH1.y << 8u) >> 25,
        int(packedSH1.y << 1u) >> 25
    ) / 63.0;

    sh = sh1_0 * (-SH_C1 * dir.y)
        + sh1_1 * (SH_C1 * dir.z)
        + sh1_2 * (-SH_C1 * dir.x);

#if defined(SH_BANDS_2) || defined(SH_BANDS_3)
    // Extract sint8 values packed into 4 x uint32
    float3 sh2_0 = float3(
        int(packedSH2.x << 24u) >> 24,
        int(packedSH2.x << 16u) >> 24,
        int(packedSH2.x << 8u) >> 24
    ) / 127.0;
    float3 sh2_1 = float3(
        int(packedSH2.x) >> 24u,
        int(packedSH2.y << 24u) >> 24,
        int(packedSH2.y << 16u) >> 24
    ) / 127.0;
    float3 sh2_2 = float3(
        int(packedSH2.y << 8u) >> 24,
        int(packedSH2.y) >> 24,
        int(packedSH2.z << 24u) >> 24
    ) / 127.0;
    float3 sh2_3 = float3(
        int(packedSH2.z << 16u) >> 24,
        int(packedSH2.z << 8u) >> 24,
        int(packedSH2.z) >> 24
    ) / 127.0;
    float3 sh2_4 = float3(
        int(packedSH2.w << 24u) >> 24,
        int(packedSH2.w << 16u) >> 24,
        int(packedSH2.w << 8u) >> 24
    ) / 127.0;

    float xx = dir.x * dir.x;
    float yy = dir.y * dir.y;
    float zz = dir.z * dir.z;
    float xy = dir.x * dir.y;
    float yz = dir.y * dir.z;
    float zx = dir.z * dir.x;

    sh += sh2_0 * (SH_C2_0 * xy)
        + sh2_1 * (SH_C2_1 * yz)
        + sh2_2 * (SH_C2_2 * (2.0 * zz - xx - yy))
        + sh2_3 * (SH_C2_3 * zx)
        + sh2_4 * (SH_C2_4 * (xx- yy));
#endif

#ifdef SH_BANDS_3
    // Extract sint6 values packed into 4 x uint32
    float3 sh3_0 = float3(
        int(packedSH3.x << 26u) >> 26,
        int(packedSH3.x << 20u) >> 26,
        int(packedSH3.x << 14u) >> 26
    ) / 31.0;
    float3 sh3_1 = float3(
        int(packedSH3.x << 8u) >> 26,
        int(packedSH3.x << 2u) >> 26,
        int((packedSH3.x >> 4u) | (packedSH3.y << 28)) >> 26
    ) / 31.0;
    float3 sh3_2 = float3(
        int(packedSH3.y << 22u) >> 26,
        int(packedSH3.y << 16u) >> 26,
        int(packedSH3.y << 10u) >> 26
    ) / 31.0;
    float3 sh3_3 = float3(
        int(packedSH3.y << 4u) >> 26,
        int((packedSH3.y >> 2u) | (packedSH3.z << 30u)) >> 26,
        int(packedSH3.z << 24u) >> 26
    ) / 31.0;
    float3 sh3_4 = float3(
        int(packedSH3.z << 18u) >> 26,
        int(packedSH3.z << 12u) >> 26,
        int(packedSH3.z << 6u) >> 26
    ) / 31.0;
    float3 sh3_5 = float3(
        int(packedSH3.z) >> 26,
        int(packedSH3.w << 26u) >> 26,
        int(packedSH3.w << 20u) >> 26
    ) / 31.0;
    float3 sh3_6 = float3(
        int(packedSH3.w << 14u) >> 26,
        int(packedSH3.w << 8u) >> 26,
        int(packedSH3.w << 2u) >> 26
    ) / 31.0;

    sh += sh3_0 * (SH_C3_0 * dir.y * (3.0 * xx - yy))
        + sh3_1 * (SH_C3_1 * xy * dir.z) +
        + sh3_2 * (SH_C3_2 * dir.y * (4.0 * zz - xx - yy))
        + sh3_3 * (SH_C3_3 * dir.z * (2.0 * zz - 3.0 * xx - 3.0 * yy))
        + sh3_4 * (SH_C3_4 * dir.x * (4.0 * zz - xx - yy))
        + sh3_5 * (SH_C3_5 * dir.z * (xx - yy))
        + sh3_6 * (SH_C3_6 * dir.x * (xx - 3.0 * yy));
#endif

    return sh;
}
#endif
