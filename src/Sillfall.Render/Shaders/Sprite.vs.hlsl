// Draws sprites from a structured buffer: one instance per sprite, 4 vertices as a triangle strip.
// Positions are desktop pixels, shifted into the render target by Offset.

struct Sprite
{
    float2 Center;
    float2 HalfSize;
    float Rotation;
    uint Shape;
    float4 Color; // premultiplied
    float Variant;
    float Padding;
};

StructuredBuffer<Sprite> Sprites : register(t0);

cbuffer SpriteConstants : register(b0)
{
    float2 TargetSize;
    float2 Offset; // subtracted from desktop positions: the top left corner of the monitor
    float Opacity; // how strongly the weather shows on this monitor
    float3 Padding2;
};

struct VertexOutput
{
    float4 Position : SV_Position;
    float2 Local : TEXCOORD0; // -1..1 across the quad, before rotation
    float2 HalfSize : TEXCOORD1;
    nointerpolation uint Shape : SHAPE;
    nointerpolation uint Variant : VARIANT;
    nointerpolation float Bend : BEND;
    float4 Color : COLOR0;
};

VertexOutput main(uint vertexId : SV_VertexID, uint instanceId : SV_InstanceID)
{
    Sprite sprite = Sprites[instanceId];

    // One pixel of margin around soft shapes so their antialiased edge is not cut off.
    float margin = sprite.Shape == 0 ? 0.0 : 1.0;
    float2 extent = sprite.HalfSize + margin;
    float2 corner = float2(vertexId & 1, vertexId >> 1) * 2.0 - 1.0;
    float2 local = corner * extent;

    float s = sin(sprite.Rotation);
    float c = cos(sprite.Rotation);
    float2 rotated = float2(local.x * c - local.y * s, local.x * s + local.y * c);
    float2 pixel = sprite.Center + rotated - Offset;

    VertexOutput output;
    output.Position = float4(pixel / TargetSize * float2(2.0, -2.0) + float2(-1.0, 1.0), 0.0, 1.0);
    output.Local = local;
    output.HalfSize = sprite.HalfSize;
    output.Shape = sprite.Shape;
    output.Variant = (uint)max(sprite.Variant, 0.0);
    output.Bend = sprite.Variant;
    output.Color = sprite.Color * Opacity;
    return output;
}
