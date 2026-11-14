// Minimal particle shader: a soft round sprite computed from the UVs and tinted by the particle
// colour. Used for cloud wisps, vapour, staging flashes and thruster puffs, so no sprite textures
// are needed. Set Blend Mode to Additive for light-emitting effects.
Shader "Phainon/SoftParticle"
{
    Properties
    {
        [HDR] _Tint ("Tint", Color) = (1, 1, 1, 1)
        _Softness ("Edge Softness", Range(0.05, 1)) = 0.7
        [Enum(UnityEngine.Rendering.BlendMode)] _SrcBlend ("Source Blend", Float) = 5
        [Enum(UnityEngine.Rendering.BlendMode)] _DstBlend ("Destination Blend", Float) = 10
    }

    SubShader
    {
        Tags { "RenderPipeline" = "UniversalPipeline" "Queue" = "Transparent" "RenderType" = "Transparent" "IgnoreProjector" = "True" "PreviewType" = "Plane" }

        Pass
        {
            Name "SoftParticle"
            Tags { "LightMode" = "SRPDefaultUnlit" }

            Blend [_SrcBlend] [_DstBlend]
            ZWrite Off
            Cull Off

            HLSLPROGRAM
            #pragma vertex Vert
            #pragma fragment Frag

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

            CBUFFER_START(UnityPerMaterial)
                float4 _Tint;
                float _Softness;
                float _SrcBlend;
                float _DstBlend;
            CBUFFER_END

            struct Attributes
            {
                float4 positionOS : POSITION;
                float4 color : COLOR;
                float2 uv : TEXCOORD0;
            };

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                float4 color : COLOR;
                float2 uv : TEXCOORD0;
            };

            Varyings Vert(Attributes input)
            {
                Varyings output;
                output.positionCS = TransformObjectToHClip(input.positionOS.xyz);
                output.color = input.color * _Tint;
                output.uv = input.uv;
                return output;
            }

            half4 Frag(Varyings input) : SV_Target
            {
                float2 centred = input.uv * 2.0 - 1.0;
                float falloff = 1.0 - smoothstep(1.0 - _Softness, 1.0, length(centred));
                float alpha = input.color.a * falloff * falloff;
                // Premultiplying by alpha makes the same shader correct for additive blending too.
                return half4(input.color.rgb * lerp(1.0, alpha, step(_DstBlend, 1.5)), alpha);
            }
            ENDHLSL
        }
    }
    FallBack Off
}
