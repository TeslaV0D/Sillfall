// Shades a sprite quad: solid rectangle, soft disc or streak (capsule). Premultiplied output.

struct PixelInput
{
    float4 Position : SV_Position;
    float2 Local : TEXCOORD0;
    float2 HalfSize : TEXCOORD1;
    nointerpolation uint Shape : SHAPE;
    float4 Color : COLOR0;
};

// Coverage from a signed distance in pixels: 1 inside, 0 outside, a one-pixel soft edge.
float Coverage(float distance)
{
    return saturate(0.5 - distance);
}

float4 main(PixelInput input) : SV_Target
{
    if (input.Shape == 1)
    {
        // Disc: an ellipse when the half sizes differ. Approximate distance to the edge in pixels.
        float2 normalized = input.Local / max(input.HalfSize, 0.001);
        float radius = length(normalized);
        float distance = (radius - 1.0) * min(input.HalfSize.x, input.HalfSize.y);
        return input.Color * Coverage(distance);
    }

    if (input.Shape == 2)
    {
        // Streak: capsule along x with radius HalfSize.y, brightest along its axis.
        float radius = max(input.HalfSize.y, 0.001);
        float halfLength = max(input.HalfSize.x - radius, 0.0);
        float2 p = float2(max(abs(input.Local.x) - halfLength, 0.0), input.Local.y);
        float distance = length(p) - radius;
        float core = 1.0 - saturate(abs(input.Local.y) / radius) * 0.5;
        return input.Color * Coverage(distance) * core;
    }

    return input.Color;
}
