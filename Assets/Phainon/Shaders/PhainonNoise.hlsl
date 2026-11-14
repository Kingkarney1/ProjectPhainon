#ifndef PHAINON_NOISE_INCLUDED
#define PHAINON_NOISE_INCLUDED

// Small hash and value-noise library shared by the Phainon environment shaders.

float Hash13(float3 p)
{
    p = frac(p * 0.1031);
    p += dot(p, p.zyx + 31.32);
    return frac((p.x + p.y) * p.z);
}

float3 Hash33(float3 p)
{
    p = frac(p * float3(0.1031, 0.1030, 0.0973));
    p += dot(p, p.yxz + 33.33);
    return frac((p.xxy + p.yxx) * p.zyx);
}

float Hash12(float2 p)
{
    float3 p3 = frac(float3(p.xyx) * 0.1031);
    p3 += dot(p3, p3.yzx + 33.33);
    return frac((p3.x + p3.y) * p3.z);
}

// Trilinear value noise, 0..1.
float ValueNoise3(float3 p)
{
    float3 i = floor(p);
    float3 f = frac(p);
    f = f * f * (3.0 - 2.0 * f);

    float n000 = Hash13(i);
    float n100 = Hash13(i + float3(1, 0, 0));
    float n010 = Hash13(i + float3(0, 1, 0));
    float n110 = Hash13(i + float3(1, 1, 0));
    float n001 = Hash13(i + float3(0, 0, 1));
    float n101 = Hash13(i + float3(1, 0, 1));
    float n011 = Hash13(i + float3(0, 1, 1));
    float n111 = Hash13(i + float3(1, 1, 1));

    return lerp(lerp(lerp(n000, n100, f.x), lerp(n010, n110, f.x), f.y),
                lerp(lerp(n001, n101, f.x), lerp(n011, n111, f.x), f.y), f.z);
}

float ValueNoise2(float2 p)
{
    float2 i = floor(p);
    float2 f = frac(p);
    f = f * f * (3.0 - 2.0 * f);
    return lerp(lerp(Hash12(i), Hash12(i + float2(1, 0)), f.x),
                lerp(Hash12(i + float2(0, 1)), Hash12(i + float2(1, 1)), f.x), f.y);
}

// Fractal sums. Octave counts are fixed so the loops unroll on every target.
float Fbm3(float3 p)
{
    float sum = 0.0;
    float amplitude = 0.5;
    for (int i = 0; i < 4; i++)
    {
        sum += ValueNoise3(p) * amplitude;
        p = p * 2.03 + 17.1;
        amplitude *= 0.5;
    }
    return sum / 0.9375;
}

float Fbm2(float2 p)
{
    float sum = 0.0;
    float amplitude = 0.5;
    for (int i = 0; i < 4; i++)
    {
        sum += ValueNoise2(p) * amplitude;
        p = p * 2.03 + 11.7;
        amplitude *= 0.5;
    }
    return sum / 0.9375;
}

#endif
