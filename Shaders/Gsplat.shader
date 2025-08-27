Shader "Gsplat/Standard"
{
    Properties
    {
    }
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
            #pragma enable_d3d11_debug_symbols

            #include "UnityCG.cginc"

            int _SplatInstanceSize;
            float4x4 _MATRIX_M;
            StructuredBuffer<uint> _OrderBuffer;
            StructuredBuffer<float3> _PositionBuffer;
            StructuredBuffer<float3> _ScaleBuffer;
            StructuredBuffer<float4> _RotationBuffer;
            StructuredBuffer<float4> _ColorBuffer;

            struct appdata
            {
                float4 vertex : POSITION;
                uint vertexID : SV_VertexID;
                uint instanceID : SV_InstanceID;
            };

            struct SplatSource
            {
                uint order;
                uint id;
                float2 cornerUV;
            };

            bool initSource(appdata v, out SplatSource source)
            {
                source.order = v.instanceID * _SplatInstanceSize + asuint(v.vertex.z);
                source.id = _OrderBuffer[source.order];
                source.cornerUV = float2(v.vertex.x, v.vertex.y);
                return true;
            }

            struct SplatCenter
            {
                float3 view;
                float4 proj;
                float4x4 modelView;
                float projMat00;
            };

            bool initCenter(float3 modelCenter, out SplatCenter center)
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

            // stores the offset from center for the current gaussian
            struct SplatCorner
            {
                float2 offset; // corner offset from center in clip space
                float2 uv; // corner uv
                #if GSPLAT_AA
                    float aaFactor; // for scenes generated with antialiasing
                #endif
            };

            float3x3 quatToMat3(float4 R)
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

            // sample covariance vectors
            void readCovariance(in SplatSource source, out float3 covA, out float3 covB)
            {
                float4 quat = _RotationBuffer[source.id];
                float3x3 rot = quatToMat3(quat);
                float3 scale = _ScaleBuffer[source.id];

                // M = S * R
                float3x3 M = transpose(float3x3(
                    scale.x * rot[0],
                    scale.y * rot[1],
                    scale.z * rot[2]
                ));

                covA = float3(dot(M[0], M[0]), dot(M[0], M[1]), dot(M[0], M[2]));
                covB = float3(dot(M[1], M[1]), dot(M[1], M[2]), dot(M[2], M[2]));
            }

            // calculate the clip-space offset from the center for this gaussian
            bool initCorner(SplatSource source, SplatCenter center, out SplatCorner corner)
            {
                // get covariance
                float3 covA, covB;
                readCovariance(source, covA, covB);

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
                if (any((abs(center.proj.xy) - float2(maxL, maxL) * c) > center.proj.ww))
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


            void clipCorner(inout SplatCorner corner, float alpha)
            {
                float clip = min(1.0, sqrt(-log(1.0 / 255.0 / alpha)) / 2.0);
                corner.offset *= clip;
                corner.uv *= clip;
            }

            #define SH_COEFFS 15

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


            struct v2f
            {
                float2 uv : TEXCOORD0;
                float4 vertex : SV_POSITION;
                float4 color: COLOR;
            };


            float4 discardVec = float4(0.0, 0.0, 2.0, 1.0);

            v2f vert(appdata v)
            {
                v2f o;

                SplatSource source;
                if (!initSource(v, source))
                {
                    o.vertex = discardVec;
                    return o;
                }

                float3 modelCenter = _PositionBuffer[source.id];
                SplatCenter center;
                if (!initCenter(modelCenter, center))
                {
                    o.vertex = discardVec;
                    return o;
                }
                SplatCorner corner;
                if (!initCorner(source, center, corner))
                {
                    o.vertex = discardVec;
                    return o;
                }

                float4 color = _ColorBuffer[source.id];
                color.rgb = color.rgb * SH_C0 + float3(0.5, 0.5, 0.5);

                clipCorner(corner, color.w);
                
                o.vertex = center.proj + float4(corner.offset.x, _ProjectionParams.x * corner.offset.y, 0, 0);
                o.color = float4(max(color.rgb, float3(0, 0, 0)), color.a);
                o.uv = corner.uv;
                return o;
            }

            float4 frag(v2f i) : SV_Target
            {
                float A = dot(i.uv, i.uv);
                if (A > 1.0)
                {
                    discard;
                }
                float alpha = exp(-A * 4.0) * i.color.a;
                if (alpha < 1.0 / 255.0)
                {
                    discard;
                }
                return float4(i.color.rgb * alpha, alpha);
            }
            ENDHLSL


        }
    }
}