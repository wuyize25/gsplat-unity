// Gaussian Splatting helper functions & structs
// most of these are from https://github.com/playcanvas/engine/tree/main/src/scene/shader-lib/glsl/chunks/gsplat
// Copyright (c) 2011-2024 PlayCanvas Ltd
// Copyright (c) 2025 Yize Wu
// SPDX-License-Identifier: MIT

struct SplatSource
{
    uint order;
    uint id;
    float2 cornerUV;
};

struct SplatCenter
{
    float3 view;
    float4 proj;
    float4x4 modelView;
    float projMat00;
};

struct SplatCovariance
{
    float3 covA;
    float3 covB;
};

// stores the offset from center for the current gaussian
struct SplatCorner
{
    float2 offset; // corner offset from center in clip space
    float2 uv; // corner uv
    #if GSPLAT_AA
    float aaFactor; // for scenes generated with antialiasing
    #endif
};

const float4 discardVec = float4(0.0, 0.0, 2.0, 1.0);

float3x3 QuatToMat3(float4 R)
{
    float4 R2 = R + R;
    float X = R2.x * R.w;
    float4 Y = R2.y * R;
    float4 Z = R2.z * R;
    float W = R2.w * R.w;

    return float3x3(
        1.0 - Z.z - W,
        Y.z + X,
        Y.w - Z.x,
        Y.z - X,
        1.0 - Y.y - W,
        Z.w + Y.x,
        Y.w + Z.x,
        Z.w - Y.x,
        1.0 - Y.y - Z.z
    );
}

// quat format: w, x, y, z
SplatCovariance CalcCovariance(float4 quat, float3 scale)
{
    float3x3 rot = QuatToMat3(quat);

    // M = S * R
    float3x3 M = transpose(float3x3(
        scale.x * rot[0],
        scale.y * rot[1],
        scale.z * rot[2]
    ));

    SplatCovariance cov;
    cov.covA = float3(dot(M[0], M[0]), dot(M[0], M[1]), dot(M[0], M[2]));
    cov.covB = float3(dot(M[1], M[1]), dot(M[1], M[2]), dot(M[2], M[2]));
    return cov;
}

// calculate the clip-space offset from the center for this gaussian
bool InitCorner(SplatSource source, SplatCovariance covariance, SplatCenter center, out SplatCorner corner)
{
    float3 covA = covariance.covA;
    float3 covB = covariance.covB;
    float3x3 Vrk = float3x3(
        covA.x, covA.y, covA.z,
        covA.y, covB.x, covB.y,
        covA.z, covB.y, covB.z
    );

    float focal = _ScreenParams.x * center.projMat00;

    float3 v = unity_OrthoParams.w == 1.0 ? float3(0.0, 0.0, 1.0) : center.view.xyz;

    float J1 = focal / v.z;
    float2 J2 = -J1 / v.z * v.xy;
    float3x3 J = float3x3(
        J1, 0.0, J2.x,
        0.0, J1, J2.y,
        0.0, 0.0, 0.0
    );

    float3x3 W = center.modelView;
    float3x3 T = mul(J, W);
    float3x3 cov = mul(mul(T, Vrk), transpose(T));

    #if GSPLAT_AA
        // calculate AA factor
        float detOrig = cov[0][0] * cov[1][1] - cov[0][1] * cov[0][1];
        float detBlur = (cov[0][0] + 0.3) * (cov[1][1] + 0.3) - cov[0][1] * cov[0][1];
        corner.aaFactor = sqrt(max(detOrig / detBlur, 0.0));
    #endif

    float diagonal1 = cov[0][0] + 0.3;
    float offDiagonal = cov[0][1];
    float diagonal2 = cov[1][1] + 0.3;

    float mid = 0.5 * (diagonal1 + diagonal2);
    float radius = length(float2((diagonal1 - diagonal2) / 2.0, offDiagonal));
    float lambda1 = mid + radius;
    float lambda2 = max(mid - radius, 0.1);

    // Use the smaller viewport dimension to limit the kernel size relative to the screen resolution.
    float vmin = min(1024.0, min(_ScreenParams.x, _ScreenParams.y));

    float l1 = 2.0 * min(sqrt(2.0 * lambda1), vmin);
    float l2 = 2.0 * min(sqrt(2.0 * lambda2), vmin);

    // early-out gaussians smaller than 2 pixels
    if (l1 < 2.0 && l2 < 2.0)
    {
        return false;
    }

    float2 c = center.proj.ww / _ScreenParams.xy;

    // cull against frustum x/y axes
    float maxL = max(l1, l2);
    if (any(abs(center.proj.xy) - float2(maxL, maxL) * c > center.proj.ww))
    {
        return false;
    }

    float2 diagonalVector = normalize(float2(offDiagonal, lambda1 - diagonal1));
    float2 v1 = l1 * diagonalVector;
    float2 v2 = l2 * float2(diagonalVector.y, -diagonalVector.x);

    corner.offset = (source.cornerUV.x * v1 + source.cornerUV.y * v2) * c;
    corner.uv = source.cornerUV;

    return true;
}

void ClipCorner(inout SplatCorner corner, float alpha)
{
    float clip = min(1.0, sqrt(-log(1.0 / 255.0 / alpha)) / 2.0);
    corner.offset *= clip;
    corner.uv *= clip;
}

// spherical Harmonics
#ifdef SH_BANDS_1
#define SH_COEFFS 3
#elif defined(SH_BANDS_2)
#define SH_COEFFS 8
#elif defined(SH_BANDS_3)
#define SH_COEFFS 15
#else
#define SH_COEFFS 0
#endif

#define SH_C0 0.28209479177387814f

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

// see https://github.com/graphdeco-inria/gaussian-splatting/blob/main/utils/sh_utils.py
float3 EvalSH(const inout float3 sh[SH_COEFFS], float3 dir, int degree = 3)
{
    if (degree == 0)
        return float3(0, 0, 0);

    float x = dir.x;
    float y = dir.y;
    float z = dir.z;

    // 1st degree
    float3 result = SH_C1 * (-sh[0] * y + sh[1] * z - sh[2] * x);
    if (degree == 1)
        return result;

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

    if (degree == 2)
        return result;
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

// Packing

// Implementation taken from spark.js
// Decode a 24‐bit encoded uint into a quaternion (float4) using the folded octahedral inverse.
float4 DecodeQuatOctXyz88R8(uint encoded) {
    // Extract the fields.
    uint quantU = encoded & 0xFFU;              // bits 0–7
    uint quantV = (encoded >> 8u) & 0xFFU;       // bits 8–15
    uint angleInt = encoded >> 16u;              // bits 16–23

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

// Implementation taken from spark.js
// Decode a 24‐bit encoded uint into a quaternion (float4)
// float4 decodeQuatXyz888(uint encoded) {
//     int3 iQuat3 = int3(
//         int(encoded << 24) >> 24,
//         int(encoded << 16) >> 24,
//         int(encoded << 8) >> 24
//     );
//     float4 quat = float4(float3(iQuat3) / 127.0, 0.0);
//     quat.w = sqrt(max(0.0, 1.0 - dot(quat.xyz, quat.xyz)));
//     return quat;
// }

#define LN_SCALE_MIN -12.0
#define LN_SCALE_MAX 9.0

void UnpackSplat(uint4 packedData, out float4 color, out float3 modelCenter, out float3 scale, out float4 quat) {
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
