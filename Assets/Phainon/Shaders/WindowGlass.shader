// Cockpit window glazing: almost invisible head-on, with a faint cool reflection at grazing angles
// and a subtle sheen so the pane reads as glass without hiding the view outside.
Shader "Phainon/WindowGlass"
{
    Properties
    {
        _Tint ("Reflection Tint", Color) = (0.55, 0.70, 0.85, 1)
        _HeadOnOpacity ("Head-on Opacity", Range(0, 0.3)) = 0.025
        _GrazingOpacity ("Grazing Opacity", Range(0, 1)) = 0.34
        _FresnelPower ("Fresnel Power", Range(1, 8)) = 4
        _Wear ("Smudges and Scratches", Range(0, 1)) = 0.35
    }

    SubShader
    {
        Tags { "RenderPipeline" = "UniversalPipeline" "Queue" = "Transparent+50" "RenderType" = "Transparent" "IgnoreProjector" = "True" }

        Pass
        {
            Name "WindowGlass"
            Tags { "LightMode" = "SRPDefaultUnlit" }

            Blend SrcAlpha OneMinusSrcAlpha
            ZWrite Off
            Cull Off

            HLSLPROGRAM
            #pragma vertex Vert
            #pragma fragment Frag

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include "PhainonNoise.hlsl"

            CBUFFER_START(UnityPerMaterial)
                float4 _Tint;
                float _HeadOnOpacity;
                float _GrazingOpacity;
                float _FresnelPower;
                float _Wear;
            CBUFFER_END

            float3 _PhainonSunDir;      // world space, towards the Sun; set by EnvironmentDirector

            struct Attributes
            {
                float4 positionOS : POSITION;
                float3 normalOS : NORMAL;
                float2 uv : TEXCOORD0;
            };

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                float3 normalWS : TEXCOORD0;
                float3 viewDirWS : TEXCOORD1;
                float2 uv : TEXCOORD2;
            };

            Varyings Vert(Attributes input)
            {
                Varyings output;
                VertexPositionInputs vertex = GetVertexPositionInputs(input.positionOS.xyz);
                output.positionCS = vertex.positionCS;
                output.normalWS = TransformObjectToWorldNormal(input.normalOS);
                output.viewDirWS = GetCameraPositionWS() - vertex.positionWS;
                output.uv = input.uv;
                return output;
            }

            half4 Frag(Varyings input) : SV_Target
            {
                float facing = abs(dot(normalize(input.normalWS), normalize(input.viewDirWS)));
                float fresnel = pow(saturate(1.0 - facing), _FresnelPower);
                float alpha = lerp(_HeadOnOpacity, _GrazingOpacity, fresnel);

                // Fine scratches and soft smudges. They are almost invisible until the Sun is near the
                // line of sight, when they catch the light the way a real pane does.
                float2 uv = input.uv;
                float scratches = smoothstep(0.90, 0.99, ValueNoise2(uv * float2(420.0, 34.0) + ValueNoise2(uv * 9.0) * 6.0));
                float smudges = ValueNoise2(uv * float2(7.0, 4.0)) * ValueNoise2(uv * float2(19.0, 13.0));
                float wear = (scratches * 0.5 + smudges) * _Wear;
                float towardsSun = saturate(dot(normalize(-input.viewDirWS), normalize(_PhainonSunDir + float3(0.0, 1e-5, 0.0))));
                float glare = pow(towardsSun, 6.0);

                float3 color = lerp(_Tint.rgb, float3(1.0, 0.97, 0.90), glare);
                alpha = saturate(alpha + wear * (0.002 + 0.07 * glare));
                return half4(color, alpha);
            }
            ENDHLSL
        }
    }
    FallBack Off
}
