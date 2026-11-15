// The whole view outside the spacecraft in one pass: sky, atmosphere, Earth, stars, Sun and Moon.
//
// It is drawn on a large inside-out sphere before everything else. The Earth is not a mesh: each
// pixel intersects its view ray with a sphere of Earth's radius, which gives a perfectly smooth
// limb and the correct curvature at any altitude, from the launch pad to orbit. The script
// EnvironmentDirector feeds it the spacecraft's altitude and orientation through global properties.
Shader "Phainon/SpaceSky"
{
    Properties
    {
        _EarthDayTex ("Earth Day Map (equirectangular)", 2D) = "black" {}
        [Toggle] _HasEarthTex ("Use Day Map", Float) = 0
        _JupiterTex ("Jupiter Map (equirectangular)", 2D) = "black" {}
        [Toggle] _HasJupiterTex ("Use Jupiter Map", Float) = 0
        _CloudCoverage ("Cloud Coverage", Range(0, 1)) = 0.52
        _StarBrightness ("Star Brightness", Range(0, 4)) = 1.4
        _SunIntensity ("Sun Disc Intensity", Range(0, 80)) = 30
        _ZenithColor ("Sky Zenith Colour", Color) = (0.07, 0.20, 0.58, 1)
        _HorizonColor ("Sky Horizon Colour", Color) = (0.56, 0.74, 0.95, 1)
        _LimbColor ("Atmosphere Limb Colour", Color) = (0.20, 0.45, 1.0, 1)
        _HazeColor ("Surface Haze Colour", Color) = (0.50, 0.68, 0.95, 1)
    }

    SubShader
    {
        Tags { "RenderPipeline" = "UniversalPipeline" "Queue" = "Background" "RenderType" = "Background" "IgnoreProjector" = "True" }

        Pass
        {
            Name "SpaceSky"
            Tags { "LightMode" = "SRPDefaultUnlit" }

            Cull Front
            ZWrite Off
            ZTest LEqual

            HLSLPROGRAM
            #pragma vertex Vert
            #pragma fragment Frag
            #pragma target 3.5

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include "PhainonNoise.hlsl"

            TEXTURE2D(_EarthDayTex);
            SAMPLER(sampler_EarthDayTex);
            TEXTURE2D(_JupiterTex);
            SAMPLER(sampler_JupiterTex);

            CBUFFER_START(UnityPerMaterial)
                float4 _EarthDayTex_ST;
                float _HasEarthTex;
                float4 _JupiterTex_ST;
                float _HasJupiterTex;
                float _CloudCoverage;
                float _StarBrightness;
                float _SunIntensity;
                float4 _ZenithColor;
                float4 _HorizonColor;
                float4 _LimbColor;
                float4 _HazeColor;
            CBUFFER_END

            // Set every frame by EnvironmentDirector.
            float3 _PhainonSunDir;          // world space, towards the Sun
            float3 _PhainonUpDir;           // world space, local vertical at the spacecraft
            float3 _PhainonMoonDir;         // world space, towards the Moon
            float _PhainonAltitudeKm;
            float _PhainonAtmosphere;       // 1 on the ground, 0 in vacuum
            float _PhainonStarVisibility;   // 0 in daylight sky, 1 in space
            float _PhainonCloudTime;
            float4x4 _PhainonWorldToEarth;      // rotation from world space to Earth-fixed axes
            float4x4 _PhainonWorldToInertial;   // rotation from world space to the star field
            float _PhainonBodyRadiusKm;     // radius of the body below; unset means Earth
            float _PhainonBody;             // 0 Earth, 1 Jupiter
            float _PhainonFeatureLon;       // longitude of the Great Red Spot, radians, for the drawn fallback
            float4 _PhainonMoons[4];        // Jupiter's large moons: world direction and apparent radius in radians (0 = not shown)

            // The body's radius. Everything that used to assume Earth reads this instead.
            #define EARTH_RADIUS_KM (_PhainonBodyRadiusKm > 1.0 ? _PhainonBodyRadiusKm : 6371.0)
            static const float PI_F = 3.14159265;

            struct Attributes
            {
                float4 positionOS : POSITION;
            };

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                float3 viewDirWS : TEXCOORD0;
            };

            Varyings Vert(Attributes input)
            {
                Varyings output;
                VertexPositionInputs vertex = GetVertexPositionInputs(input.positionOS.xyz);
                output.positionCS = vertex.positionCS;
                output.viewDirWS = vertex.positionWS - GetCameraPositionWS();
                return output;
            }

            // The global properties are only set while the game is running. In the editor's Scene view
            // they are zero, so rotations fall back to identity and directions to sensible defaults.
            float3x3 RotationOrIdentity(float4x4 m)
            {
                float3x3 r = (float3x3)m;
                if (dot(r[0], r[0]) < 0.25) r = float3x3(1.0, 0.0, 0.0, 0.0, 1.0, 0.0, 0.0, 0.0, 1.0);
                return r;
            }

            float3 DirectionOr(float3 direction, float3 fallback)
            {
                return dot(direction, direction) < 0.25 ? fallback : normalize(direction);
            }

            // The atmosphere is a shell of fixed thickness, so its apparent width shrinks with the distance
            // to the limb. 1546 km is that distance from the 185 km parking orbit, where the look was tuned.
            float LimbScale(float tangentDistanceKm)
            {
                return min(1.0, 1546.0 / max(tangentDistanceKm, 1.0));
            }

            float Galaxy(float3 dir, float3 centre, float3 axisHint, float longRadius, float shortRadius)
            {
                float3 major = normalize(axisHint - centre * dot(axisHint, centre));
                float3 minor = cross(centre, major);
                float3 offset = dir - centre;
                float u = dot(offset, major) / longRadius;
                float v = dot(offset, minor) / shortRadius;
                float r2 = u * u + v * v;
                // A wide faint disc with a small bright core, only on the near side of the sky.
                return step(0.9, dot(dir, centre)) * (exp(-r2) * 0.5 + exp(-r2 * 7.0) * 0.7);
            }

            // Faint nebulae and two small galaxies, fixed among the stars.
            float3 DeepSky(float3 dir)
            {
                float mottle = 0.25 + 0.75 * Fbm3(dir * 9.0 + 4.0);
                float3 color = float3(0.55, 0.16, 0.26) * pow(saturate(dot(dir, normalize(float3(-0.62, 0.31, 0.72)))), 60.0);
                color += float3(0.14, 0.30, 0.55) * pow(saturate(dot(dir, normalize(float3(0.48, -0.55, -0.68)))), 90.0);
                color += float3(0.30, 0.20, 0.50) * pow(saturate(dot(dir, normalize(float3(0.10, 0.80, -0.59)))), 140.0);
                color *= mottle * 0.16;
                color += float3(0.90, 0.85, 0.70) * Galaxy(dir, normalize(float3(0.74, 0.42, -0.52)), float3(0.2, 0.9, 0.1), 0.035, 0.012) * 0.12;
                color += float3(0.70, 0.80, 1.00) * Galaxy(dir, normalize(float3(-0.30, -0.72, 0.62)), float3(0.9, 0.1, 0.4), 0.022, 0.009) * 0.12;
                return color;
            }

            // The Milky Way, fixed among the stars: a band that swells and warms towards the galactic
            // centre, is broken into clouds and is split by dust lanes. "crowd" comes back as how thick
            // the star field is in this direction, for the faint stars that gather along the band.
            float3 MilkyWay(float3 dir, out float crowd)
            {
                float3 pole = normalize(float3(0.35, 0.86, 0.37));
                float3 centre = normalize(float3(0.86, -0.35, 0.0));
                float latitude = dot(dir, pole);
                float3 inPlane = dir - pole * latitude;
                float towardsCentre = dot(inPlane, centre) / max(length(inPlane), 1e-4);    // cosine of galactic longitude
                float inner = smoothstep(0.2, 1.0, towardsCentre);

                float large = Fbm3(dir * 3.5 + 1.7);
                float fine = Fbm3(dir * 12.0 + 5.3);
                // The band wanders a little off its great circle and is widest in the inner galaxy.
                float offset = latitude - (large - 0.5) * 0.07;
                float width = lerp(0.085, 0.17, inner);
                float band = exp(-(offset * offset) / (width * width));
                float clouds = smoothstep(0.22, 0.72, large) * (0.55 + 0.45 * fine);
                float bulge = exp(-latitude * latitude * 22.0 - (1.0 - towardsCentre) * 9.0);

                // Dust: a ragged dark rift down the middle, strongest towards the centre.
                float riftOffset = (latitude - (Fbm3(dir * 6.0 + 9.1) - 0.5) * 0.10) / 0.030;
                float rift = exp(-riftOffset * riftOffset) * smoothstep(0.30, 0.62, fine) * (0.35 + 0.65 * inner);

                crowd = band * (0.35 + 0.65 * clouds);
                float glow = (band * clouds * lerp(0.45, 1.0, 0.5 + 0.5 * towardsCentre) + bulge * 0.6) * (1.0 - 0.8 * rift);
                float3 tint = lerp(float3(0.60, 0.70, 0.95), float3(1.0, 0.86, 0.66), saturate(inner * 0.7 + bulge));
                // Kept faint: it is a glow behind the stars, not a cloud in front of them.
                return tint * (glow * 0.045);
            }

            float3 Stars(float3 dir, float crowd)
            {
                float3 color = 0.0;
                for (int layer = 0; layer < 3; layer++)
                {
                    float scale = layer == 0 ? 85.0 : (layer == 1 ? 170.0 : 250.0);
                    // The third layer is faint and thickens along the Milky Way.
                    float threshold = layer == 0 ? 0.93 : (layer == 1 ? 0.955 : lerp(0.975, 0.86, crowd));
                    float3 p = dir * scale;
                    float3 cell = floor(p);
                    float3 local = frac(p) - 0.5;
                    float3 rnd = Hash33(cell);

                    float present = step(threshold, rnd.x);
                    float3 offset = (Hash33(cell + 7.0) - 0.5) * 0.55;
                    float size = lerp(0.07, 0.15, rnd.y * rnd.y);
                    float falloff = smoothstep(size, 0.0, length(local - offset));
                    // A wide spread: most stars faint, a few bright.
                    float brightness = lerp(0.14, 2.6, pow(rnd.z, 7.0)) * (layer == 2 ? 0.3 : 1.0);
                    // Stars only twinkle through air. In vacuum this term is exactly one.
                    brightness *= 1.0 + _PhainonAtmosphere * 0.35 * sin(_Time.y * (2.0 + 5.0 * rnd.x) + rnd.y * 40.0);
                    float3 tint = lerp(float3(0.72, 0.84, 1.0), float3(1.0, 0.90, 0.74), rnd.y);
                    color += falloff * brightness * tint * present;
                }
                return color;
            }

            // Fallback surface used when no day map is assigned: noise continents, deserts and ice caps.
            float3 ProceduralSurface(float3 n)
            {
                float continents = Fbm3(n * 2.1 + 4.0);
                float land = smoothstep(0.50, 0.55, continents);
                float dryness = Fbm3(n * 5.0 + 9.0);
                float3 ocean = lerp(float3(0.010, 0.035, 0.12), float3(0.02, 0.08, 0.20), smoothstep(0.38, 0.50, continents));
                float3 ground = lerp(float3(0.06, 0.13, 0.04), float3(0.30, 0.24, 0.14), smoothstep(0.42, 0.62, dryness));
                float3 terrainColor = lerp(ocean, ground, land);
                float ice = smoothstep(0.80, 0.90, abs(n.y) + (dryness - 0.5) * 0.12);
                return lerp(terrainColor, float3(0.85, 0.88, 0.92), ice);
            }

            float3 SampleSurface(float3 n, float lod)
            {
                // One exit point with the result assigned on both paths: some compilers warn about a
                // return inside a branch, and an if/else skips the unused path's work.
                float3 result;
                if (_HasEarthTex < 0.5)
                {
                    result = ProceduralSurface(n);
                }
                else
                {
                    float u = atan2(n.z, n.x) / (2.0 * PI_F) + 0.5;
                    float v = asin(clamp(n.y, -1.0, 1.0)) / PI_F + 0.5;
                    // The mip level is chosen explicitly. Screen-space derivatives would be wrong along
                    // the longitude seam, where u jumps from 1 back to 0.
                    result = SAMPLE_TEXTURE2D_LOD(_EarthDayTex, sampler_EarthDayTex, float2(u, v), lod).rgb;
                }
                return result;
            }

            float3 ShadeEarth(float3 viewDir, float3 up, float3 sun, float camRadius, float altitudeKm, float cosNadir, float sinNadir)
            {
                // Ray-sphere intersection from the spacecraft. The numerically stable root is used
                // because close to the ground the naive form subtracts two nearly equal numbers.
                float b = -camRadius * cosNadir;
                float c = altitudeKm * (2.0 * EARTH_RADIUS_KM + altitudeKm);
                // b*b - c, written so it stays accurate when the Earth is a small disc a million km away.
                float miss = camRadius * sinNadir;
                float disc = max(EARTH_RADIUS_KM * EARTH_RADIUS_KM - miss * miss, 0.0);
                float t = c / (-b + sqrt(disc));

                float3 hit = up * camRadius + viewDir * t;
                float3 n = normalize(hit);
                float3 nEarth = normalize(mul(RotationOrIdentity(_PhainonWorldToEarth), n));

                // Surface distance covered by one pixel, compared with the day map's texel size at the
                // equator (about 19.5 km for a 2048-wide map), gives the mip level to read.
                float viewCos = saturate(dot(n, -viewDir));
                float footprintKm = t * 0.0011 / max(viewCos, 0.03);
                float lod = log2(max(footprintKm / 19.5, 1.0));

                float3 albedo = SampleSurface(nEarth, lod);
                float ocean = saturate((albedo.b - max(albedo.r, albedo.g)) * 8.0 + 0.15);

                // Extra ground detail fades in at low altitude, where the day map alone would look soft.
                float lowAltitude = saturate(1.0 - altitudeKm / 140.0);
                float detail = ValueNoise3(nEarth * 1400.0) * 0.6 + ValueNoise3(nEarth * 5200.0) * 0.4;
                albedo *= lerp(1.0, 0.72 + 0.56 * detail, lowAltitude * 0.7);

                // Cloud deck: warped fractal noise with a soft coverage threshold.
                float3 drift = float3(_PhainonCloudTime * 0.004, 0.0, _PhainonCloudTime * 0.002);
                float3 warp = float3(ValueNoise3(nEarth * 3.0 + 2.0), ValueNoise3(nEarth * 3.0 + 9.0), ValueNoise3(nEarth * 3.0 + 5.0)) - 0.5;
                float cloudBase = Fbm3(nEarth * 5.5 + warp * 1.4 + drift);
                float cloudFine = ValueNoise3(nEarth * 46.0 + drift * 4.0);
                float threshold = lerp(0.72, 0.38, _CloudCoverage);
                float cloud = smoothstep(threshold, threshold + 0.20, cloudBase + (cloudFine - 0.5) * 0.16);
                // Below the cloud tops the deck is not painted on the ground; passing through cloud is
                // shown by the wisp particles instead.
                cloud *= saturate((altitudeKm - 4.0) / 10.0);

                float ndl = dot(n, sun);
                float day = smoothstep(-0.12, 0.22, ndl);
                float diffuse = saturate(ndl) * 0.92 + 0.08;

                float3 halfVector = normalize(sun - viewDir);
                float glint = pow(saturate(dot(n, halfVector)), 110.0) * ocean * (1.0 - cloud) * 1.4;

                float3 shaded = albedo * diffuse + glint;
                shaded = lerp(shaded, float3(0.94, 0.95, 0.97) * (diffuse * 0.95 + 0.05), cloud * 0.92);
                // Sunlight grazing through the atmosphere reddens the band beside the terminator.
                float3 twilight = lerp(float3(1.0, 0.50, 0.26), float3(1.0, 1.0, 1.0), smoothstep(-0.02, 0.14, ndl));
                shaded *= twilight;

                // Night side: clustered city lights on land, hidden by cloud.
                float land = 1.0 - ocean;
                float cities = smoothstep(0.60, 0.88, ValueNoise3(nEarth * 240.0)) * smoothstep(0.45, 0.80, ValueNoise3(nEarth * 1700.0));
                // Individual cities are far below one pixel from deep space, where they would only sparkle.
                cities *= saturate(1.5 - footprintKm / 30.0);
                float3 nightSide = float3(0.0015, 0.0025, 0.006) + float3(1.0, 0.72, 0.38) * (cities * land * (1.0 - cloud) * 0.55);
                shaded = shaded * day + nightSide * (1.0 - day);

                // Aerial perspective. From orbit the haze thickens towards the limb; near the ground
                // it thickens with distance, so the horizon dissolves into the sky colour.
                float limbHaze = pow(1.0 - viewCos, 2.6) * 0.92;
                float visibilityKm = lerp(14.0, 3200.0, saturate(altitudeKm / 90.0));
                // Haze builds along the part of the sight line that is inside the air, not the empty space before it.
                float airPathKm = min(t, 120.0 / max(viewCos, 0.05));
                float distanceHaze = (1.0 - exp(-airPathKm / visibilityKm)) * (0.30 + 0.70 * _PhainonAtmosphere);
                float haze = saturate(max(limbHaze, distanceHaze));

                float3 hazeColor = _HazeColor.rgb * (0.04 + 0.96 * day);
                return lerp(shaded, hazeColor, haze * 0.88);
            }

            // Fallback gas giant used when no map is assigned: belts, zones and one red storm.
            float3 ProceduralJupiter(float3 n)
            {
                float latitude = asin(clamp(n.y, -1.0, 1.0));
                float warp = (Fbm3(n * float3(3.0, 14.0, 3.0) + 2.0) - 0.5) * 0.10;
                float band = smoothstep(0.25, 0.75, sin((latitude + warp) * 18.0) * 0.5 + 0.5);
                float3 color = lerp(float3(0.56, 0.36, 0.22), float3(0.85, 0.78, 0.66), band);
                color *= 0.85 + 0.30 * Fbm3(n * float3(6.0, 40.0, 6.0));
                color = lerp(color, float3(0.45, 0.42, 0.42), smoothstep(0.75, 1.0, abs(n.y)));
                float3 centre = float3(cos(-0.33) * cos(_PhainonFeatureLon), sin(-0.33), cos(-0.33) * sin(_PhainonFeatureLon));
                float3 east = normalize(cross(float3(0.0, 1.0, 0.0), centre));
                float3 offset = n - centre;
                // Squared by hand: pow of a negative number is undefined on some graphics APIs.
                float2 scaled = float2(dot(offset, east) / 0.16, offset.y / 0.09);
                float ellipse = dot(scaled, scaled);
                return lerp(color, float3(0.72, 0.30, 0.18), smoothstep(1.0, 0.6, ellipse) * step(0.0, dot(n, centre)));
            }

            // Jupiter: the same ray-sphere geometry as the Earth, a cloud-top map and no surface below it.
            float3 ShadeJupiter(float3 viewDir, float3 up, float3 sun, float camRadius, float altitudeKm, float cosNadir, float sinNadir)
            {
                float b = -camRadius * cosNadir;
                float c = altitudeKm * (2.0 * EARTH_RADIUS_KM + altitudeKm);
                float miss = camRadius * sinNadir;
                float disc = max(EARTH_RADIUS_KM * EARTH_RADIUS_KM - miss * miss, 0.0);
                float t = c / (-b + sqrt(disc));

                float3 n = normalize(up * camRadius + viewDir * t);
                float3 nBody = normalize(mul(RotationOrIdentity(_PhainonWorldToEarth), n));
                float viewCos = saturate(dot(n, -viewDir));

                float3 albedo;
                if (_HasJupiterTex < 0.5)
                {
                    albedo = ProceduralJupiter(nBody);
                }
                else
                {
                    float u = atan2(nBody.z, nBody.x) / (2.0 * PI_F) + 0.5;
                    float v = asin(clamp(nBody.y, -1.0, 1.0)) / PI_F + 0.5;
                    // One texel of the map as imported (about 3600 wide) covers about 125 km at Jupiter's equator.
                    float footprintKm = t * 0.0011 / max(viewCos, 0.03);
                    albedo = SAMPLE_TEXTURE2D_LOD(_JupiterTex, sampler_JupiterTex, float2(u, v), log2(max(footprintKm / 125.0, 1.0))).rgb;
                }

                // Thick cloud scatters light softly: a gentle terminator and a darkened limb.
                float ndl = dot(n, sun);
                float lit = smoothstep(-0.06, 0.30, ndl) * (0.25 + 0.75 * saturate(ndl));
                float limb = pow(viewCos, 0.35);
                // High haze: a thin pale rim where the line of sight grazes the cloud tops on the sunlit side.
                float haze = pow(1.0 - viewCos, 5.0) * smoothstep(-0.05, 0.25, ndl);
                return albedo * (lit * limb * 1.2 + 0.003) + float3(0.95, 0.86, 0.72) * (haze * 0.22);
            }

            float3 ShadeSky(float3 viewDir, float3 up, float3 sun, float camRadius, float above, float altitudeKm)
            {
                float atmosphere = _PhainonAtmosphere;
                float daylight = smoothstep(-0.22, 0.18, dot(up, sun));

                // Inside the atmosphere: a zenith-to-horizon gradient that thins with altitude.
                float horizonBlend = exp(-above * 3.6);
                float3 sky = lerp(_ZenithColor.rgb, _HorizonColor.rgb, horizonBlend) * atmosphere * daylight;

                // The atmosphere seen edge-on: a broad glow near the ground, a thin blue line from orbit.
                float tangentDistance = sqrt(max(camRadius * camRadius - EARTH_RADIUS_KM * EARTH_RADIUS_KM, 0.0));
                float3 limbNormal = normalize(up * camRadius + viewDir * tangentDistance);
                float limbLit = smoothstep(-0.14, 0.20, dot(limbNormal, sun));
                float width = lerp(0.011, 0.24, sqrt(saturate(atmosphere))) * LimbScale(tangentDistance);
                float band = exp(-above / width);
                float core = exp(-above / (width * 0.32));
                float3 limb = lerp(_LimbColor.rgb, float3(0.80, 0.90, 1.0), core) * band * limbLit * lerp(1.5, 0.55, atmosphere);
                // Near the terminator the limb turns from blue to the orange of a sunrise seen from orbit.
                limb *= lerp(float3(1.0, 0.42, 0.16), float3(1.0, 1.0, 1.0), smoothstep(-0.08, 0.15, dot(limbNormal, sun)));
                // The blue line is Earth's air. Jupiter's edge is just cloud.
                limb *= 1.0 - saturate(_PhainonBody);

                float3 color = sky + limb;

                // Sun disc with a tight corona and a wider glow that only exists where there is air.
                float sunCos = dot(viewDir, sun);
                float disc = smoothstep(0.99990, 0.99996, sunCos);
                float corona = pow(saturate(sunCos), 900.0) * 2.5 + pow(saturate(sunCos), 48.0) * 0.30 * (0.25 + atmosphere);
                color += float3(1.0, 0.96, 0.88) * (disc * _SunIntensity + corona);

                float skyLuma = dot(color, float3(0.30, 0.59, 0.11));
                float darkness = saturate(1.0 - skyLuma * 5.0);

                // Moon: a lit sphere reconstructed from the angular offset to its centre.
                float3 moonDir = DirectionOr(_PhainonMoonDir, float3(0.0, 0.2, 0.98));
                const float moonRadius = 0.0135;      // radians; a little larger than life so it reads on screen
                float moonCos = dot(viewDir, moonDir);
                float moonEdge = cos(moonRadius);
                if (moonCos > moonEdge && _PhainonBody < 0.5)
                {
                    float3 offset = (viewDir - moonDir * moonCos) / sin(moonRadius);
                    float r2 = saturate(dot(offset, offset));
                    float3 moonNormal = normalize(offset - moonDir * sqrt(1.0 - r2));
                    float maria = smoothstep(0.42, 0.62, Fbm3(moonNormal * 2.6 + 3.0));
                    float craters = ValueNoise3(moonNormal * 22.0);
                    float3 moonAlbedo = lerp(float3(0.20, 0.20, 0.21), float3(0.44, 0.43, 0.41), maria) * (0.85 + 0.30 * craters);
                    float3 moonColor = moonAlbedo * saturate(dot(moonNormal, sun)) * 1.7;
                    float edge = smoothstep(1.0, 0.92, r2);
                    color = lerp(color, moonColor + color * 0.25, edge * (1.0 - 0.75 * atmosphere * daylight));
                    darkness *= 1.0 - edge;
                }

                // Jupiter's four large moons, as small lit discs. Io is sulphur yellow, the others grey to tan.
                if (_PhainonBody > 0.5)
                {
                    for (int m = 0; m < 4; m++)
                    {
                        float radius = _PhainonMoons[m].w;
                        float3 moonAt = _PhainonMoons[m].xyz;
                        float along = dot(viewDir, moonAt);
                        if (radius > 0.0 && along > cos(radius))
                        {
                            float3 offset = (viewDir - moonAt * along) / sin(radius);
                            float r2 = saturate(dot(offset, offset));
                            float3 surface = normalize(offset - moonAt * sqrt(1.0 - r2));
                            float3 tint = m == 0 ? float3(0.80, 0.70, 0.36) : (m == 1 ? float3(0.74, 0.70, 0.62) : (m == 2 ? float3(0.50, 0.46, 0.42) : float3(0.34, 0.31, 0.28)));
                            float3 moonLit = tint * (0.8 + 0.4 * ValueNoise3(surface * 6.0 + m * 7.0)) * (saturate(dot(surface, sun)) * 1.6 + 0.01);
                            float edge = smoothstep(1.0, 0.80, r2);
                            color = lerp(color, moonLit, edge);
                            darkness *= 1.0 - edge;
                        }
                    }
                }

                float3 inertialDir = normalize(mul(RotationOrIdentity(_PhainonWorldToInertial), viewDir));
                float crowd;
                float3 milkyWay = MilkyWay(inertialDir, crowd);
                float3 starField = Stars(inertialDir, crowd) + milkyWay + DeepSky(inertialDir);
                color += starField * (_StarBrightness * _PhainonStarVisibility * darkness);
                return color;
            }

            half4 Frag(Varyings input) : SV_Target
            {
                float3 viewDir = normalize(input.viewDirWS);
                float3 up = DirectionOr(_PhainonUpDir, float3(0.0, 1.0, 0.0));
                float3 sun = DirectionOr(_PhainonSunDir, float3(0.5, 0.7, 0.5));

                float altitudeKm = max(_PhainonAltitudeKm, 0.03);
                float camRadius = EARTH_RADIUS_KM + altitudeKm;

                // Angle of this pixel above the true (curved) horizon. Negative means it looks at the Earth.
                float cosNadir = clamp(dot(viewDir, -up), -1.0, 1.0);
                float limbAngle = asin(saturate(EARTH_RADIUS_KM / camRadius));
                // atan2 of sine and cosine stays accurate looking almost straight at the Earth, where acos does not.
                float sinNadir = length(cross(viewDir, up));
                float above = atan2(sinNadir, cosNadir) - limbAngle;

                float3 color;
                if (above < 0.0)
                {
                    if (_PhainonBody > 0.5) color = ShadeJupiter(viewDir, up, sun, camRadius, altitudeKm, cosNadir, sinNadir);
                    else color = ShadeEarth(viewDir, up, sun, camRadius, altitudeKm, cosNadir, sinNadir);

                    // Blend the last sliver of surface into the atmosphere so the limb has no hard edge.
                    float3 skyAtLimb = ShadeSky(viewDir, up, sun, camRadius, 0.0, altitudeKm);
                    float tangentDistance = sqrt(max(camRadius * camRadius - EARTH_RADIUS_KM * EARTH_RADIUS_KM, 0.0));
                    color = lerp(color, max(color, skyAtLimb * 0.85), smoothstep(-0.006 * LimbScale(tangentDistance), 0.0, above));
                }
                else
                {
                    color = ShadeSky(viewDir, up, sun, camRadius, above, altitudeKm);
                }

                return half4(color, 1.0);
            }
            ENDHLSL
        }
    }
    FallBack Off
}
