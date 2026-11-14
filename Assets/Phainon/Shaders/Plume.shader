// Rocket exhaust plume for the external camera: an additive cone with a scrolling noise pattern,
// a hot white core that cools to orange, and soft silhouette edges so it reads as a volume.
Shader "Phainon/Plume"
{
    Properties
    {
        [HDR] _CoreColor ("Core Colour", Color) = (3.2, 3.0, 2.6, 1)
        [HDR] _TailColor ("Tail Colour", Color) = (2.0, 0.65, 0.15, 1)
        _Intensity ("Intensity", Range(0, 4)) = 1
        _ScrollSpeed ("Scroll Speed", Float) = 9
        _Vacuum ("Vacuum (0 sea level, 1 space)", Range(0, 1)) = 0
    }

    SubShader
    {
        Tags { "RenderPipeline" = "UniversalPipeline" "Queue" = "Transparent" "RenderType" = "Transparent" "IgnoreProjector" = "True" }

        Pass
        {
            Name "Plume"
            Tags { "LightMode" = "SRPDefaultUnlit" }

            Blend One One
            ZWrite Off
            Cull Off

            HLSLPROGRAM
            #pragma vertex Vert
            #pragma fragment Frag
            #pragma target 3.5

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include "PhainonNoise.hlsl"

            CBUFFER_START(UnityPerMaterial)
                float4 _CoreColor;
                float4 _TailColor;
                float _Intensity;
                float _ScrollSpeed;
                float _Vacuum;
            CBUFFER_END

            struct Attributes
            {
                float4 positionOS : POSITION;
                float3 normalOS : NORMAL;
                float2 uv : TEXCOORD0;      // x around the cone, y from nozzle (0) to tail (1)
            };

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                float2 uv : TEXCOORD0;
                float3 normalWS : TEXCOORD1;
                float3 viewDirWS : TEXCOORD2;
            };

            Varyings Vert(Attributes input)
            {
                Varyings output;
                VertexPositionInputs vertex = GetVertexPositionInputs(input.positionOS.xyz);
                output.positionCS = vertex.positionCS;
                output.uv = input.uv;
                output.normalWS = TransformObjectToWorldNormal(input.normalOS);
                output.viewDirWS = GetCameraPositionWS() - vertex.positionWS;
                return output;
            }

            half4 Frag(Varyings input) : SV_Target
            {
                float along = saturate(input.uv.y);
                float flicker = ValueNoise2(float2(input.uv.x * 9.0, along * 5.0 - _Time.y * _ScrollSpeed));
                float streaks = ValueNoise2(float2(input.uv.x * 26.0, along * 2.0 - _Time.y * _ScrollSpeed * 0.6));

                float body = pow(1.0 - along, 1.6) * (0.55 + 0.45 * flicker) * (0.75 + 0.25 * streaks);

                // Shock diamonds: bright bands near the nozzle while ambient pressure squeezes the jet.
                float diamonds = pow(0.5 + 0.5 * sin(along * 46.0), 8.0) * pow(1.0 - along, 4.0) * (1.0 - _Vacuum);
                body *= 1.0 + diamonds * 1.5;

                // Edges soften towards the silhouette, but never to nothing: looking straight down the
                // axis every part of the cone is edge-on, and the plume must still be there.
                float facing = abs(dot(normalize(input.normalWS), normalize(input.viewDirWS)));
                float soft = lerp(0.5, 1.0, smoothstep(0.0, 0.55, facing));

                // In vacuum the jet is fainter, paler and blue-white, with none of the sooty orange.
                float3 coreColor = lerp(_CoreColor.rgb, float3(1.5, 1.7, 2.3), _Vacuum);
                float3 tailColor = lerp(_TailColor.rgb, float3(0.35, 0.50, 0.95), _Vacuum);
                float3 color = lerp(coreColor, tailColor, smoothstep(0.0, 0.55, along));
                return half4(color * (body * soft * _Intensity * lerp(1.0, 0.55, _Vacuum)), 1.0);
            }
            ENDHLSL
        }
    }
    FallBack Off
}
