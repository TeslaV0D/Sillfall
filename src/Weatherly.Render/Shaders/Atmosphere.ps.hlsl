// Fog and sunlight at a quarter of the resolution (PLAN.md, sections 4.4 and 4.5). Premultiplied output.
// Mask: the windows of this monitor, 0 where a window is, 1 elsewhere (also a quarter of the resolution).

Texture2D Mask : register(t0);
SamplerState Linear : register(s0);

cbuffer AtmosphereConstants : register(b0)
{
    float2 QuarterSize;   // size of this target in pixels
    float2 MonitorOrigin; // desktop pixels of the monitor's top left corner
    float2 MonitorSize;   // monitor size in desktop pixels
    float FogDensity;
    float FogMaxAlpha;
    float4 FogColor;      // rgb straight, w: pixel scale
    float2 FogScroll;
    float FogPhase;
    float SunStrength;
    float2 SunPosition;   // desktop pixels
    float SunScale;
    float Padding;
    float4 SunColor;      // rgb straight
};

static const int RaySamples = 48;
static const float RayDecay = 0.965;

float Hash(float2 p)
{
    p = frac(p * float2(123.34, 456.21));
    p += dot(p, p + 45.32);
    return frac(p.x * p.y);
}

float Noise(float2 p)
{
    float2 i = floor(p);
    float2 f = frac(p);
    float2 u = f * f * (3.0 - 2.0 * f);
    float a = Hash(i);
    float b = Hash(i + float2(1.0, 0.0));
    float c = Hash(i + float2(0.0, 1.0));
    float d = Hash(i + float2(1.0, 1.0));
    return lerp(lerp(a, b, u.x), lerp(c, d, u.x), u.y);
}

// Fractal noise, 4 octaves.
float Fbm(float2 p)
{
    float value = 0.0;
    float amplitude = 0.5;
    [unroll]
    for (int octave = 0; octave < 4; octave++)
    {
        value += amplitude * Noise(p);
        p = p * 2.03 + float2(17.1, 9.2);
        amplitude *= 0.5;
    }

    return value;
}

// 1 where the sky is open at a desktop position, 0 behind a window. Outside this monitor: open.
float OpenSky(float2 desktop)
{
    float2 uv = (desktop - MonitorOrigin) / MonitorSize;
    if (any(uv < 0.0) || any(uv > 1.0))
    {
        return 1.0;
    }

    return Mask.SampleLevel(Linear, uv, 0).r;
}

float4 main(float4 position : SV_Position) : SV_Target
{
    float2 uv = position.xy / QuarterSize;
    float2 desktop = MonitorOrigin + uv * MonitorSize;
    float open = Mask.SampleLevel(Linear, uv, 0).r;
    float4 result = float4(0.0, 0.0, 0.0, 0.0);

    if (SunStrength > 0.0)
    {
        // God rays: march towards the sun and count how much of the way is open sky.
        float2 toSun = SunPosition - desktop;
        float2 stepToSun = toSun * (0.9 / RaySamples);
        float2 marcher = desktop; // 'sample' is a keyword in HLSL
        float decay = 1.0;
        float lit = 0.0;
        float total = 0.0;
        [loop]
        for (int i = 0; i < RaySamples; i++)
        {
            marcher += stepToSun;
            lit += OpenSky(marcher) * decay;
            total += decay;
            decay *= RayDecay;
        }

        float distance = length(toSun);
        float shafts = pow(lit / total, 2.0) * exp(-distance / (MonitorSize.y * 1.1));
        float glow = exp(-pow(distance / (170.0 * SunScale), 2.0)) + 0.35 * exp(-distance / (650.0 * SunScale));

        // Windows catch less light than the open desktop: the content stays readable.
        float light = SunStrength * (0.26 * shafts + 0.5 * glow) * lerp(0.35, 1.0, open);
        result = float4(SunColor.rgb * light, light * 0.3);
    }

    if (FogDensity > 0.0)
    {
        float scale = FogColor.w;
        float2 p = (desktop - FogScroll) / (380.0 * scale);
        float2 warp = float2(Fbm(p + FogPhase), Fbm(p + float2(5.2, 1.3) - FogPhase));
        float shape = Fbm(p + 1.6 * warp + FogPhase * 0.5);

        // Denser towards the bottom of the screen.
        float height = saturate(0.45 + 0.75 * uv.y);
        float alpha = saturate((shape - 0.28) * 1.7) * height * FogDensity;

        // Windows rise out of the fog: thinner over them, a faint light rim just above their top edges.
        float below = Mask.SampleLevel(Linear, uv + float2(0.0, 2.0 / QuarterSize.y), 0).r;
        float rim = open * (1.0 - below);
        alpha = min(alpha * lerp(0.45, 1.0, open), FogMaxAlpha);
        float4 fog = float4(FogColor.rgb * alpha, alpha);
        fog += float4(1.0, 1.0, 1.0, 1.0) * (rim * 0.06 * FogDensity);

        // Fog in front of the light.
        result = fog + result * (1.0 - fog.a);
    }

    return result;
}
