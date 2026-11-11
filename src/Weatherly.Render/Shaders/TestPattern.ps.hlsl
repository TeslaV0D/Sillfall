// Calibration pattern of the overlay. All positions come from TestPatternLayout (C#), so the
// shader and the tests share one definition. Output is premultiplied alpha like every overlay shader.

cbuffer TestPatternConstants : register(b0)
{
    float2 Size;        // render target size in pixels
    float Unit;         // thickness of a thin line in pixels
    float GridSpacing;  // distance between grid lines in pixels
    float4 Ramp;        // left, top, right, bottom of the alpha ramp
    float4 Shade;       // left, top, right, bottom of the half-transparent black block
    float Border;       // thickness of the frame along the monitor edges
    float Corner;       // edge length of the corner markers
    float CrossLength;  // half length of the center cross
    float Padding;
};

bool Inside(float2 p, float4 rect)
{
    return p.x >= rect.x && p.x < rect.z && p.y >= rect.y && p.y < rect.w;
}

// "Source over destination" for premultiplied colors.
float4 Over(float4 destination, float4 source)
{
    return source + destination * (1.0 - source.a);
}

float4 main(float4 position : SV_Position) : SV_Target
{
    float2 p = floor(position.xy);
    float2 fromEnd = Size - 1.0 - p;
    float4 color = float4(0.0, 0.0, 0.0, 0.0);

    // Faint grid, shows the DPI scale: one cell is 100 device-independent pixels.
    float2 cell = fmod(p, GridSpacing);
    if (cell.x < Unit || cell.y < Unit)
    {
        color = Over(color, float4(0.15, 0.15, 0.15, 0.15));
    }

    // Black at 50 %: must darken the desktop. Invisible if blending were additive.
    if (Inside(p, Shade))
    {
        color = Over(color, float4(0.0, 0.0, 0.0, 0.5));
    }

    // White from alpha 0 to 1: must fade smoothly without dark fringes.
    if (Inside(p, Ramp))
    {
        float alpha = (p.x - Ramp.x + 0.5) / (Ramp.z - Ramp.x);
        color = Over(color, float4(alpha, alpha, alpha, alpha));
    }

    // Cross in the monitor center.
    float2 fromCenter = abs(p + 0.5 - Size * 0.5);
    if ((fromCenter.x < Unit && fromCenter.y < CrossLength) || (fromCenter.y < Unit && fromCenter.x < CrossLength))
    {
        color = float4(0.0, 1.0, 1.0, 1.0);
    }

    // Corner markers: red top left, green top right, blue bottom left, yellow bottom right.
    float markerEdge = Border + Corner;
    bool left = p.x < markerEdge;
    bool right = fromEnd.x < markerEdge;
    bool top = p.y < markerEdge;
    bool bottom = fromEnd.y < markerEdge;
    if (left && top)
    {
        color = float4(1.0, 0.0, 0.0, 1.0);
    }
    if (right && top)
    {
        color = float4(0.0, 1.0, 0.0, 1.0);
    }
    if (left && bottom)
    {
        color = float4(0.0, 0.0, 1.0, 1.0);
    }
    if (right && bottom)
    {
        color = float4(1.0, 1.0, 0.0, 1.0);
    }

    // Magenta frame: must touch all four monitor edges exactly.
    if (min(min(p.x, p.y), min(fromEnd.x, fromEnd.y)) < Border)
    {
        color = float4(1.0, 0.0, 1.0, 1.0);
    }

    return color;
}
