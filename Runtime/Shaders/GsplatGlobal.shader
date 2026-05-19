// Copyright (c) 2026 Keir Rice
// SPDX-License-Identifier: MIT
//
// Unified draw shader for global K-way merged Gaussian splatting.
// Reads from concatenated global buffers (GlobalPackedBuffer, GlobalSH*Buffers)
// via indices from GlobalOrderBuffer. Per-renderer transforms applied via
// RendererTransforms structured buffer.

Shader "Gsplat/Global"
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
            #pragma multi_compile SH_BANDS_0 SH_BANDS_1 SH_BANDS_2 SH_BANDS_3 SH_BANDS_4

            #include "UnityCG.cginc"

            bool _GammaToLinear;
            int _SplatInstanceSize;
            int _SHDegree;
            float _Brightness;
            float _ScaleFactor;

            #include "GsplatSparkGlobal.hlsl"

            struct appdata
            {
                float4 vertex : POSITION;
                #if !defined(UNITY_INSTANCING_ENABLED) && !defined(UNITY_PROCEDURAL_INSTANCING_ENABLED) && !defined(UNITY_STEREO_INSTANCING_ENABLED)
                uint instanceID : SV_InstanceID;
                #endif
                UNITY_VERTEX_INPUT_INSTANCE_ID
            };

            struct v2f
            {
                float2 uv : TEXCOORD0;
                float4 vertex : SV_POSITION;
                float4 color : COLOR;
                UNITY_VERTEX_OUTPUT_STEREO
            };

            v2f vert(appdata v)
            {
                v2f o;
                UNITY_SETUP_INSTANCE_ID(v);
                UNITY_INITIALIZE_OUTPUT(v2f, o);
                UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(o);
                o.vertex = discardVec;

                #if !defined(UNITY_INSTANCING_ENABLED) && !defined(UNITY_PROCEDURAL_INSTANCING_ENABLED) && !defined(UNITY_STEREO_INSTANCING_ENABLED)
                uint instanceId = v.instanceID;
                #else
                uint instanceId = unity_InstanceID;
                #endif

                GlobalSplatSource source;
                if (!InitGlobalSource(instanceId, v.vertex.xyz, _ScaleFactor, source))
                    return o;

                SplatCenter center;
                SplatCorner corner;
                float4 color;
                if (!InitGlobalSplatData(source, center, corner, color))
                    return o;

                #ifndef SH_BANDS_0
                // center.modelView is already computed by InitGlobalSplatData → InitCenter.
                float3 dir = normalize(mul(center.view, (float3x3)center.modelView));
                float3 sh[SH_COEFFS];
                InitGlobalSH(source, sh);
                color.rgb += EvalSH(sh, dir, _SHDegree);
                #endif

                ClipCorner(corner, color.a);

                o.vertex = center.proj + float4(corner.offset.x, _ProjectionParams.x * corner.offset.y, 0, 0);
                o.color  = color;
                o.uv     = corner.uv;
                return o;
            }

            float4 frag(v2f i) : SV_Target
            {
                float A = dot(i.uv, i.uv);
                if (A > 1.0) discard;

                float2 absUV = abs(i.uv);
                float maxUV = max(absUV.x, absUV.y);

                float falloff = -exp((maxUV - _ScaleFactor * 1.16) * 25 * _ScaleFactor);
                float alpha = (exp(-A * 4.0) + falloff) * i.color.a;

                if (alpha < 1.0 / 255.0) discard;
                if (_GammaToLinear)
                    return float4(GammaToLinearSpace(i.color.rgb) * alpha * _Brightness, alpha);
                return float4(i.color.rgb * alpha * _Brightness, alpha);
            }
            ENDHLSL
        }
    }
}
