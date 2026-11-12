// Draws axis-aligned rectangles, one instance per rectangle, 4 vertices as a triangle strip.
// Rectangles are given in pixels of the render target, so edges land exactly on pixel boundaries.

struct RectInstance
{
    float4 Rect;  // left, top, right, bottom in pixels
    float4 Color; // premultiplied
};

StructuredBuffer<RectInstance> Instances : register(t0);

cbuffer RectConstants : register(b0)
{
    float2 TargetSize;
    float2 Padding;
};

struct VertexOutput
{
    float4 Position : SV_Position;
    float4 Color : COLOR0;
};

VertexOutput main(uint vertexId : SV_VertexID, uint instanceId : SV_InstanceID)
{
    RectInstance instance = Instances[instanceId];
    float2 corner = float2(vertexId & 1, vertexId >> 1);
    float2 pixel = lerp(instance.Rect.xy, instance.Rect.zw, corner);

    VertexOutput output;
    output.Position = float4(pixel / TargetSize * float2(2.0, -2.0) + float2(-1.0, 1.0), 0.0, 1.0);
    output.Color = instance.Color;
    return output;
}
