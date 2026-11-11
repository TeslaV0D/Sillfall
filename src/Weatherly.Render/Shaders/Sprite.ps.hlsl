// Shades a sprite quad: solid rectangle, soft disc, streak (capsule), ring or snow flake. Premultiplied output.

struct PixelInput
{
    float4 Position : SV_Position;
    float2 Local : TEXCOORD0;
    float2 HalfSize : TEXCOORD1;
    nointerpolation uint Shape : SHAPE;
    nointerpolation uint Variant : VARIANT;
    float4 Color : COLOR0;
};

// Coverage from a signed distance in pixels: 1 inside, 0 outside, a one-pixel soft edge.
float Coverage(float distance)
{
    return saturate(0.5 - distance);
}

static const float Pi = 3.14159265;

// Distance from p to the segment a-b.
float SegmentDistance(float2 p, float2 a, float2 b)
{
    float2 ab = b - a;
    float t = saturate(dot(p - a, ab) / max(dot(ab, ab), 0.0001));
    return length(p - a - ab * t);
}

// Signed distance in pixels to a snow flake of radius r, centred at the origin. Six-fold symmetric:
// the point is folded into one 30 degree wedge, where every arm lies along the x axis.
float FlakeDistance(float2 p, float r, uint variant)
{
    float radius = length(p);
    if (variant == 0)
    {
        return radius - r; // soft blob
    }

    if (variant == 5)
    {
        // Cluster: three blobs stuck together.
        float d = length(p - float2(0.0, -0.35 * r)) - 0.55 * r;
        d = min(d, length(p - float2(-0.38 * r, 0.25 * r)) - 0.5 * r);
        return min(d, length(p - float2(0.4 * r, 0.22 * r)) - 0.45 * r);
    }

    float angle = atan2(p.y, p.x) + Pi;
    float sector = Pi / 3.0;
    float folded = abs(fmod(angle, sector) - sector * 0.5);
    float2 q = radius * float2(cos(folded), sin(folded));

    float width = max(variant == 4 ? 0.2 * r : 0.11 * r, 0.45);
    float d = SegmentDistance(q, float2(0.0, 0.0), float2(r, 0.0)) - width;
    d = min(d, radius - 0.22 * r); // centre

    if (variant == 2)
    {
        // Branches leaning outwards from the middle of each arm.
        float2 start = float2(0.5 * r, 0.0);
        d = min(d, SegmentDistance(q, start, start + 0.32 * r * float2(0.5, 0.866)) - width * 0.8);
    }
    else if (variant == 3)
    {
        // Plate: a hexagon with short tips.
        float hexagon = q.x - 0.62 * r;
        d = min(d, hexagon);
    }

    return d;
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

    if (input.Shape == 3)
    {
        // Ring: the outline of the ellipse, about 1.2 px thick.
        float2 normalized = input.Local / max(input.HalfSize, 0.001);
        float distance = abs(length(normalized) - 1.0) * min(input.HalfSize.x, input.HalfSize.y) - 0.6;
        return input.Color * Coverage(distance);
    }

    if (input.Shape == 4)
    {
        // Flake: the shape plus a faint glow around it, so small flakes stay soft.
        float r = max(min(input.HalfSize.x, input.HalfSize.y), 0.5);
        float distance = FlakeDistance(input.Local, r, input.Variant);
        float glow = saturate(1.0 - length(input.Local) / (r + 1.0)) * 0.25;
        return input.Color * max(Coverage(distance), glow);
    }

    return input.Color;
}
