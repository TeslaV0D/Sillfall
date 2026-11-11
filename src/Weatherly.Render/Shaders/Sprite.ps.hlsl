// Shades a sprite quad: solid rectangle, soft disc, streak (capsule), ring, snow flake or leaf. Premultiplied output.

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

// Signed distance to a leaf in units of the half length. n is the position in the quad, -1..1 on both
// axes; the blade spans x = -0.8..1, the stem the rest towards -1.
float LeafDistance(float2 n, uint variant)
{
    float2 b = float2((n.x - 0.1) / 0.9, n.y); // blade: -1..1 along its length
    float d;
    if (variant == 1)
    {
        // Maple: five pointed lobes around the stem end.
        float2 p = b - float2(-0.25, 0.0);
        float angle = atan2(p.y, p.x);
        float lobes = pow(abs(cos(2.5 * angle)), 0.5);
        d = length(p * float2(0.85, 1.0)) - (0.45 + 0.55 * lobes) * 0.95;
    }
    else if (variant == 2)
    {
        // Oak: an oval with a wavy edge.
        float angle = atan2(b.y, b.x);
        float r = length(float2(b.x, b.y * 1.25));
        d = r - (0.92 + 0.08 * cos(9.0 * angle));
    }
    else
    {
        // Pointed oval (0) or narrow (3): the lens between two circles, tips at x = -1 and 1.
        float h = variant == 3 ? 0.35 : 0.62;
        float c = (1.0 - h * h) / (2.0 * h);
        float2 q = float2(b.x, b.y * h);
        d = max(length(q - float2(0.0, c)), length(q + float2(0.0, c))) - (c + h);
        d /= h;
    }

    // Stem: a thin line from the blade to the end of the quad.
    float stem = max(abs(n.y) - 0.07, max(n.x - (-0.6), -1.0 - n.x));
    return min(d, stem);
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

    if (input.Shape == 5)
    {
        // Leaf: blade with a darker midrib. Distances in units of the half width, so the edge stays one pixel soft.
        float2 n = input.Local / max(input.HalfSize, 0.001);
        float scale = max(min(input.HalfSize.x, input.HalfSize.y), 0.5);
        float distance = LeafDistance(n, input.Variant) * scale;
        float rib = abs(input.Local.y) < 0.6 && n.x > -0.6 && n.x < 0.85 ? 0.72 : 1.0;
        float4 color = input.Color;
        color.rgb *= rib;
        return color * Coverage(distance);
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
