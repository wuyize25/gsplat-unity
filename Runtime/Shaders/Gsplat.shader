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
            #pragma use_dxc
            #pragma multi_compile SH_BANDS_0 SH_BANDS_1 SH_BANDS_2 SH_BANDS_3

            #include "UnityCG.cginc"
            #include "Gsplat.hlsl"
            bool _GammaToLinear;
            int _SplatCount;
            int _SplatInstanceSize;
            float4x4 _MATRIX_M;
            StructuredBuffer<uint> _OrderBuffer;
            StructuredBuffer<float3> _PositionBuffer;
            StructuredBuffer<float3> _ScaleBuffer;
            StructuredBuffer<float4> _RotationBuffer;
            StructuredBuffer<float4> _ColorBuffer;

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

            // sample covariance vectors
            SplatCovariance ReadCovariance(SplatSource source)
            {
                float4 quat = _RotationBuffer[source.id];
                float3 scale = _ScaleBuffer[source.id];
                return CalcCovariance(quat, scale);
            }

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

                float3 modelCenter = _PositionBuffer[source.id];
                SplatCenter center;
                if (!InitCenter(modelCenter, center))
                {
                    o.vertex = discardVec;
                    return o;
                }

                SplatCovariance cov = ReadCovariance(source);
                SplatCorner corner;
                if (!initCorner(source, cov, center, corner))
                {
                    o.vertex = discardVec;
                    return o;
                }

                float4 color = _ColorBuffer[source.id];
                color.rgb = color.rgb * SH_C0 + float3(0.5, 0.5, 0.5);

                #ifndef SH_BANDS_0
                // calculate the model-space view direction
                float3 dir = normalize(mul((float3x3)center.modelView, center.view));
                float3 sh[SH_COEFFS];
                for (int i = 0; i < SH_COEFFS; i++)
                    sh[i] = _SHBuffer[source.id * SH_COEFFS + i];
                color.rgb += evalSH(sh, dir);
                #endif

                clipCorner(corner, color.w);

                o.vertex = center.proj + float4(corner.offset.x, _ProjectionParams.x * corner.offset.y, 0, 0);
                o.color = float4(max(color.rgb, float3(0, 0, 0)), color.a);
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