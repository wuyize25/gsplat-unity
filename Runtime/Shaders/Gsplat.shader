// Copyright (c) 2025 Yize Wu
// SPDX-License-Identifier: MIT

Shader "Gsplat/Standard"
{
    Properties {}
    SubShader
    {
        Tags
        {
            "RenderType"="Transparent"
            "Queue"="Transparent"
        }

        Pass
        {
            ZWrite Off
            Blend One OneMinusSrcAlpha
            Cull Off

            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #pragma require compute
            #pragma multi_compile SH_BANDS_0 SH_BANDS_1 SH_BANDS_2 SH_BANDS_3

            #include "UnityCG.cginc"
            #include "Gsplat.hlsl"
            bool _GammaToLinear;
            int _SplatCount;
            int _SplatInstanceSize;
            int _SHDegree;
            float4x4 _MATRIX_M;
            StructuredBuffer<uint> _OrderBuffer;
            StructuredBuffer<uint4> _PackedSplatsBuffer;
            #ifndef SH_BANDS_0
            StructuredBuffer<float3> _SHBuffer;
            #endif

            struct appdata
            {
                float4 vertex : POSITION;
                #if !defined(UNITY_INSTANCING_ENABLED) && !defined(UNITY_PROCEDURAL_INSTANCING_ENABLED) && !defined(UNITY_STEREO_INSTANCING_ENABLED)
                uint instanceID : SV_InstanceID;
                #endif
                UNITY_VERTEX_INPUT_INSTANCE_ID
            };

            bool InitSource(appdata v, out SplatSource source)
            {
                #if !defined(UNITY_INSTANCING_ENABLED) && !defined(UNITY_PROCEDURAL_INSTANCING_ENABLED) && !defined(UNITY_STEREO_INSTANCING_ENABLED)
                source.order = v.instanceID * _SplatInstanceSize + asuint(v.vertex.z);
                #else
                source.order = unity_InstanceID * _SplatInstanceSize + asuint(v.vertex.z);
                #endif

                if (source.order >= _SplatCount)
                    return false;

                source.id = _OrderBuffer[source.order];
                source.cornerUV = float2(v.vertex.x, v.vertex.y);
                return true;
            }

            bool InitCenter(float3 modelCenter, out SplatCenter center)
            {
                float4x4 modelView = mul(UNITY_MATRIX_V, _MATRIX_M);
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
            
            // Implementation taken from spark.js
            // Decode a 24‐bit encoded uint into a quaternion (vec4) using the folded octahedral inverse.
            float4 decodeQuatOctXyz88R8(uint encoded) {
                // Extract the fields.
                uint quantU = encoded & 0xFF;               // bits 0–7
                uint quantV = (encoded >> 8) & 0xFF;        // bits 8–15
                uint angleInt = encoded >> 16;              // bits 16–23

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

            struct v2f
            {
                float2 uv : TEXCOORD0;
                float4 vertex : SV_POSITION;
                float4 color: COLOR;
                UNITY_VERTEX_OUTPUT_STEREO
            };

            v2f vert(appdata v)
            {
                v2f o;
                UNITY_SETUP_INSTANCE_ID(v);
                UNITY_INITIALIZE_OUTPUT(v2f, o);
                UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(o);
                
                SplatSource source;
                if (!InitSource(v, source))
                {
                    o.vertex = discardVec;
                    return o;
                }
                
                uint4 word = _PackedSplatsBuffer[source.id];
                uint word0 = word.x;
                uint word1 = word.y;
                uint word2 = word.z;
                uint word3 = word.w;
                
                float3 modelCenter = float3(f16tof32(word1 & 0xffffu), f16tof32((word1 >> 16u) & 0xffffu), f16tof32(word2 & 0xffffu)); 

                SplatCenter center;
                if (!InitCenter(modelCenter, center))
                {
                    o.vertex = discardVec;
                    return o;
                }

                uint3 uScale = uint3(word3 & 0xffu, (word3 >> 8u) & 0xffu, (word3 >> 16u) & 0xffu);
                float lnScaleMin = -12.0;
                float lnScaleMax = 9.0;
                float lnScaleScale = (lnScaleMax - lnScaleMin) / 254.0;
                float3 scale = float3(
                    (uScale.x == 0u) ? 0.0 : exp(lnScaleMin + float(uScale.x - 1u) * lnScaleScale),
                    (uScale.y == 0u) ? 0.0 : exp(lnScaleMin + float(uScale.y - 1u) * lnScaleScale),
                    (uScale.z == 0u) ? 0.0 : exp(lnScaleMin + float(uScale.z - 1u) * lnScaleScale)
                );

                uint uQuat = ((word2 >> 16u) & 0xFFFFu) | ((word3 >> 8u) & 0xFF0000u);
                float4 quat = decodeQuatOctXyz88R8(uQuat);

                SplatCovariance cov = CalcCovariance(quat, scale);
                SplatCorner corner;
                if (!InitCorner(source, cov, center, corner))
                {
                    o.vertex = discardVec;
                    return o;
                }

                uint4 uColor = uint4(word0 & 0xff, (word0 >> 8) & 0xff, (word0 >> 16) & 0xff, (word0 >> 24) & 0xff);
                float4 color = (float4(uColor) / 255.0);

                #ifndef SH_BANDS_0
                // calculate the model-space view direction
                float3 dir = normalize(mul(center.view, (float3x3)center.modelView));
                float3 sh[SH_COEFFS];
                for (int i = 0; i < SH_COEFFS; i++)
                    sh[i] = _SHBuffer[source.id * SH_COEFFS + i];
                color.rgb += EvalSH(sh, dir, _SHDegree);
                #endif

                ClipCorner(corner, color.w);

                o.vertex = center.proj + float4(corner.offset.x, _ProjectionParams.x * corner.offset.y, 0, 0);
                o.color = color;
                o.uv = corner.uv;
                return o;
            }

            float4 frag(v2f i) : SV_Target
            {
                float A = dot(i.uv, i.uv);
                if (A > 1.0) discard;
                float alpha = exp(-A * 4.0) * i.color.a;
                if (alpha < 1.0 / 255.0) discard;
                if (_GammaToLinear)
                    return float4(GammaToLinearSpace(i.color.rgb) * alpha, alpha);
                return float4(i.color.rgb * alpha, alpha);
            }
            ENDHLSL


        }
    }
}