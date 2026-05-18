// Gaussian Splatting helper functions & structs
// most of these are from https://github.com/playcanvas/engine/tree/main/src/scene/shader-lib/glsl/chunks/gsplat
// Copyright (c) 2011-2024 PlayCanvas Ltd
// Copyright (c) 2025 Yize Wu
// SPDX-License-Identifier: MIT

#ifndef GSPLAT_INCLUDED
#define GSPLAT_INCLUDED

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

bool InitCenter(float4x4 modelView, float3 modelCenter, out SplatCenter center)
{
    float4 centerView = mul(modelView, float4(modelCenter, 1.0));
    if (centerView.z > 0.0)
    {
        return false;
    }
    float4 centerProj = mul(UNITY_MATRIX_P, centerView);
    centerProj.z = clamp(centerProj.z, -abs(centerProj.w), abs(centerProj.w));
    center.view = centerView.xyz / centerView.w;
    center.proj = centerProj;
    center.projMat00 = UNITY_MATRIX_P[0][0];
    center.modelView = modelView;
    return true;
}

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
#elif defined(SH_BANDS_4)
#define SH_COEFFS 24
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
// Band-4 constants follow the same convention as SH_C3_*: K_{l,|m|} * (-1)^|m|.
// K values cross-checked against gsplat (Berkeley) SphericalHarmonicsCUDA.cu; sign
// pattern matches graphdeco-inria's SH3 (Condon-Shortley phase for m≠0).
#define SH_C4_0 0.6258357354491761f      // m=-4: +K_{4,4}
#define SH_C4_1 -1.7701307697799304f     // m=-3: -K_{4,3}
#define SH_C4_2 0.9461746957575601f      // m=-2: +K_{4,2-}, polynomial xy(7z²-1)
#define SH_C4_3 -0.6690465435572892f     // m=-1: -K_{4,1}
#define SH_C4_4 0.10578554691520431f     // m= 0: +K_{4,0}
#define SH_C4_5 -0.6690465435572892f     // m=+1: -K_{4,1}
#define SH_C4_6 0.47308734787878004f     // m=+2: +K_{4,2+}, polynomial (x²-y²)(7z²-1)
#define SH_C4_7 -1.7701307697799304f     // m=+3: -K_{4,3}
#define SH_C4_8 0.6258357354491761f      // m=+4: +K_{4,4}

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

    #if defined(SH_BANDS_2) || defined(SH_BANDS_3) || defined(SH_BANDS_4)
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

    #if defined(SH_BANDS_3) || defined(SH_BANDS_4)
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

    if (degree == 3)
        return result;
    #endif

    #ifdef SH_BANDS_4
    // 4th degree. Polynomials use unit-sphere substitutions (7z²-1 → 6zz-xx-yy,
    // 7z³-3z → z*(4zz-3xx-3yy)) for consistency with the SH3 block above.
    result = result + (
        sh[15] * (SH_C4_0 * xy * (xx - yy)) +
        sh[16] * (SH_C4_1 * yz * (3.0 * xx - yy)) +
        sh[17] * (SH_C4_2 * xy * (6.0 * zz - xx - yy)) +
        sh[18] * (SH_C4_3 * yz * (4.0 * zz - 3.0 * xx - 3.0 * yy)) +
        sh[19] * (SH_C4_4 * (35.0 * zz * zz - 30.0 * zz + 3.0)) +
        sh[20] * (SH_C4_5 * xz * (4.0 * zz - 3.0 * xx - 3.0 * yy)) +
        sh[21] * (SH_C4_6 * (xx - yy) * (6.0 * zz - xx - yy)) +
        sh[22] * (SH_C4_7 * xz * (xx - 3.0 * yy)) +
        sh[23] * (SH_C4_8 * (xx * xx - 6.0 * xx * yy + yy * yy))
    );
    #endif

    return result;
}
#endif

#endif
