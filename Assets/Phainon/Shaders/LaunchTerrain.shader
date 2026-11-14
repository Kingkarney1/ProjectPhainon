// Ground around the launch complex: coastline, vegetation, the pad apron and the crawlerway,
// generated from noise so no terrain textures are needed. The edge fades out so the patch blends
// into the Earth drawn by Phainon/SpaceSky, and the whole patch fades as the vehicle climbs away.
Shader "Phainon/LaunchTerrain"
{
    Properties
    {
        _LandColor ("Vegetation Colour", Color) = (0.10, 0.17, 0.07, 1)
        _SandColor ("Sand Colour", Color) = (0.38, 0.34, 0.24, 1)
        _WaterColor ("Water Colour", Color) = (0.03, 0.11, 0.20, 1)
        _ConcreteColor ("Concrete Colour", Color) = (0.42, 0.42, 0.40, 1)
        _PadRadius ("Pad Apron Radius (m)", Float) = 95
        _FadeStart ("Edge Fade Start (m)", Float) = 2600
        _FadeEnd ("Edge Fade End (m)", Float) = 3900
    }

    SubShader
    {
        Tags { "RenderPipeline" = "UniversalPipeline" "Queue" = "Transparent-200" "RenderType" = "Transparent" "IgnoreProjector" = "True" }

        Pass
        {
            Name "LaunchTerrain"
            Tags { "LightMode" = "SRPDefaultUnlit" }

            Blend SrcAlpha OneMinusSrcAlpha
            ZWrite Off
            Cull Off

            HLSLPROGRAM
            #pragma vertex Vert
            #pragma fragment Frag
            #pragma target 3.5

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include "PhainonNoise.hlsl"

            CBUFFER_START(UnityPerMaterial)
                float4 _LandColor;
                float4 _SandColor;
                float4 _WaterColor;
                float4 _ConcreteColor;
                float _PadRadius;
                float _FadeStart;
                float _FadeEnd;
            CBUFFER_END

            float3 _PhainonSunDir;
            float3 _PhainonUpDir;
            float3 _PhainonHazeColorNow;    // sky colour at the horizon, set by EnvironmentDirector
            float _PhainonSiteFade;         // 1 on the pad, 0 once the site is far below

            struct Attributes
            {
                float4 positionOS : POSITION;
            };

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                float2 groundPos : TEXCOORD0;   // metres from the pad centre
                float3 positionWS : TEXCOORD1;
            };

            Varyings Vert(Attributes input)
            {
                Varyings output;
                VertexPositionInputs vertex = GetVertexPositionInputs(input.positionOS.xyz);
                output.positionCS = vertex.positionCS;
                output.positionWS = vertex.positionWS;
                output.groundPos = input.positionOS.xz;
                return output;
            }

            half4 Frag(Varyings input) : SV_Target
            {
                float2 p = input.groundPos;
                float radius = length(p);

                // Coastline to the east of the pad, wandering with low-frequency noise.
                float coast = p.x - 620.0 - (Fbm2(p * 0.0011 + 3.0) - 0.5) * 900.0;
                float water = smoothstep(-25.0, 25.0, coast);
                float beach = smoothstep(-110.0, -10.0, coast) * (1.0 - water);

                float patches = Fbm2(p * 0.0042 + 11.0);
                float fine = ValueNoise2(p * 0.06);
                float3 land = lerp(_LandColor.rgb, _SandColor.rgb, smoothstep(0.45, 0.70, patches)) * (0.82 + 0.36 * fine);
                land = lerp(land, _SandColor.rgb * 1.15, beach);

                float ripple = ValueNoise2(p * 0.02 + _Time.y * 0.15);
                float3 sea = _WaterColor.rgb * (0.85 + 0.30 * ripple);
                float3 color = lerp(land, sea, water);

                // Crawlerway running west from the pad, and the circular pad apron with its flame trench.
                float road = (1.0 - smoothstep(18.0, 24.0, abs(p.y))) * step(p.x, 0.0) * (1.0 - smoothstep(2200.0, 2600.0, -p.x));
                color = lerp(color, float3(0.40, 0.37, 0.31) * (0.9 + 0.2 * fine), road);

                float apron = 1.0 - smoothstep(_PadRadius - 3.0, _PadRadius + 3.0, radius);
                float3 concrete = _ConcreteColor.rgb * (0.90 + 0.20 * ValueNoise2(p * 0.4));
                float trench = (1.0 - smoothstep(7.0, 9.0, abs(p.x))) * step(abs(p.y), _PadRadius * 0.8);
                concrete = lerp(concrete, float3(0.05, 0.05, 0.05), trench * 0.85);
                color = lerp(color, concrete, apron);

                // Flat-ground lighting from the Sun, plus sky fill.
                // Small offsets keep the normalisation defined before the game has set these globals.
                float sunHeight = saturate(dot(normalize(_PhainonUpDir + float3(0.0, 1e-5, 0.0)), normalize(_PhainonSunDir + float3(0.0, 1e-5, 0.0))));
                color *= 0.22 + 0.95 * sunHeight;

                // Distance haze towards the horizon colour.
                float distanceToCamera = distance(input.positionWS, GetCameraPositionWS());
                float haze = 1.0 - exp(-distanceToCamera / 7000.0);
                color = lerp(color, _PhainonHazeColorNow, haze);

                float alpha = (1.0 - smoothstep(_FadeStart, _FadeEnd, radius)) * saturate(_PhainonSiteFade);
                return half4(color, alpha);
            }
            ENDHLSL
        }
    }
    FallBack Off
}
