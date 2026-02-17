
#ifndef SH_BANDS_0

// spherical Harmonics
#ifdef SH_BANDS_1
#define SH_COEFFS 3
#elif defined(SH_BANDS_2)
#define SH_COEFFS 8
#define SH_PREV_COEFFS 3
#elif defined(SH_BANDS_3)
#define SH_COEFFS 15
#define SH_PREV_COEFFS 8
#else
#define SH_COEFFS 0
#endif

#define SH_C0 0.28209479177387814f

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

float3 EvaluateSH1(uint2 packedSH1, float3 dir) {
    // Extract sint7 values packed into 2 x uint32
    float3 sh1_0 = float3(int3(
        int(packedSH1.x << 25u) >> 25,
        int(packedSH1.x << 18u) >> 25,
        int(packedSH1.x << 11u) >> 25
    )) / 63.0;
    float3 sh1_1 = float3(int3(
        int(packedSH1.x << 4u) >> 25,
        int((packedSH1.x >> 3u) | (packedSH1.y << 29u)) >> 25,
        int(packedSH1.y << 22u) >> 25
    )) / 63.0;
    float3 sh1_2 = float3(int3(
        int(packedSH1.y << 15u) >> 25,
        int(packedSH1.y << 8u) >> 25,
        int(packedSH1.y << 1u) >> 25
    )) / 63.0;

    return sh1_0 * (-SH_C1 * dir.y)
        + sh1_1 * (SH_C1 * dir.z)
        + sh1_2 * (-SH_C1 * dir.x);
}

#if defined(SH_BANDS_2) || defined(SH_BANDS_3)
float3 EvaluateSH2(uint4 packedSH2, float3 dir) {
    // Extract sint8 values packed into 4 x uint32
    float3 sh2_0 = float3(int3(
        int(packedSH2.x << 24) >> 24,
        int(packedSH2.x << 16) >> 24,
        int(packedSH2.x << 8) >> 24
    )) / 127.0;
    float3 sh2_1 = float3(int3(
        int(packedSH2.x) >> 24,
        int(packedSH2.y << 24) >> 24,
        int(packedSH2.y << 16) >> 24
    )) / 127.0;
    float3 sh2_2 = float3(int3(
        int(packedSH2.y << 8) >> 24,
        int(packedSH2.y) >> 24,
        int(packedSH2.z << 24) >> 24
    )) / 127.0;
    float3 sh2_3 = float3(int3(
        int(packedSH2.z << 16) >> 24,
        int(packedSH2.z << 8) >> 24,
        int(packedSH2.z) >> 24
    )) / 127.0;
    float3 sh2_4 = float3(int3(
        int(packedSH2.w << 24) >> 24,
        int(packedSH2.w << 16) >> 24,
        int(packedSH2.w << 8) >> 24
    )) / 127.0;

    return sh2_0 * (SH_C2_0 * dir.x * dir.y)
        + sh2_1 * (SH_C2_1 * dir.y * dir.z)
        + sh2_2 * (SH_C2_2 * (2.0 * dir.z * dir.z - dir.x * dir.x - dir.y * dir.y))
        + sh2_3 * (SH_C2_3 * dir.x * dir.z)
        + sh2_4 * (SH_C2_4 * (dir.x * dir.x - dir.y * dir.y));
}
#endif

#ifdef SH_BANDS_3
float3 EvaluateSH3(uint4 packedSH3, float3 dir) {
    // Extract sint6 values packed into 4 x uint32
    float3 sh3_0 = float3(int3(
        int(packedSH3.x << 26u) >> 26,
        int(packedSH3.x << 20u) >> 26,
        int(packedSH3.x << 14u) >> 26
    )) / 31.0;
    float3 sh3_1 = float3(int3(
        int(packedSH3.x << 8u) >> 26,
        int(packedSH3.x << 2u) >> 26,
        int((packedSH3.x >> 4u) | (packedSH3.y << 28)) >> 26
    )) / 31.0;
    float3 sh3_2 = float3(int3(
        int(packedSH3.y << 22u) >> 26,
        int(packedSH3.y << 16u) >> 26,
        int(packedSH3.y << 10u) >> 26
    )) / 31.0;
    float3 sh3_3 = float3(int3(
        int(packedSH3.y << 4u) >> 26,
        int((packedSH3.y >> 2u) | (packedSH3.z << 30u)) >> 26,
        int(packedSH3.z << 24u) >> 26
    )) / 31.0;
    float3 sh3_4 = float3(int3(
        int(packedSH3.z << 18u) >> 26,
        int(packedSH3.z << 12u) >> 26,
        int(packedSH3.z << 6u) >> 26
    )) / 31.0;
    float3 sh3_5 = float3(int3(
        int(packedSH3.z) >> 26,
        int(packedSH3.w << 26u) >> 26,
        int(packedSH3.w << 20u) >> 26
    )) / 31.0;
    float3 sh3_6 = float3(int3(
        int(packedSH3.w << 14u) >> 26,
        int(packedSH3.w << 8u) >> 26,
        int(packedSH3.w << 2u) >> 26
    )) / 31.0;

    float xx = dir.x * dir.x;
    float yy = dir.y * dir.y;
    float zz = dir.z * dir.z;
    float xy = dir.x * dir.y;
    float yz = dir.y * dir.z;
    float zx = dir.z * dir.x;

    return sh3_0 * (SH_C3_0 * dir.y * (3.0 * xx - yy))
        + sh3_1 * (SH_C3_1 * xy * dir.z) +
        + sh3_2 * (SH_C3_2 * dir.y * (4.0 * zz - xx - yy))
        + sh3_3 * (SH_C3_3 * dir.z * (2.0 * zz - 3.0 * xx - 3.0 * yy))
        + sh3_4 * (SH_C3_4 * dir.x * (4.0 * zz - xx - yy))
        + sh3_5 * (SH_C3_5 * dir.z * (xx - yy))
        + sh3_6 * (SH_C3_6 * dir.x * (xx - 3.0 * yy));
}
#endif
#endif
