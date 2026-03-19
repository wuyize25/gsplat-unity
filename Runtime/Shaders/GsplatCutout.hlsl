// Gaussian Splatting Cutout Specific code
// most of these are from https://https://github.com/aras-p/UnityGaussianSplatting/blob/main/package/Shaders/SplatUtilities.compute
// Copyright (c) 2026 Arthur Aillet
// SPDX-License-Identifier: MIT

#define SPLAT_CUTOUT_TYPE_ELLIPSOID 0
#define SPLAT_CUTOUT_TYPE_BOX 1

uint FloatToSortableUint(float f)
{
    uint fu = asuint(f);
    uint mask = -((int)(fu >> 31)) | 0x80000000;
    return fu ^ mask;
}

bool IsSplatCut(float3 pos)
{
    bool finalCut = false;
    for (uint i = 0; i < _CutoutsCount; ++i)
    {
        GaussianCutoutShaderData cutData = _CutoutsBuffer[i];
        uint type = cutData.typeAndFlags & 0xFF;
        if (type == 0xFF) // invalid/null cutout, ignore
        continue;

        bool invert = (cutData.typeAndFlags & 0xFF00) != 0;

        float3 cutoutPos = mul(cutData.mat, float4(pos, 1)).xyz;
        if (type == SPLAT_CUTOUT_TYPE_ELLIPSOID)
        {
            invert = (dot(cutoutPos, cutoutPos) <= 1) == invert;
        }
        if (type == SPLAT_CUTOUT_TYPE_BOX)
        {
            invert = (all(abs(cutoutPos) <= 1)) == invert;
        }
        finalCut = finalCut | invert;
    }
    return finalCut;
}