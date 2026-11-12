// Scales a quarter-resolution layer up to the full target with bilinear filtering. Premultiplied in and out.

Texture2D Layer : register(t0);
SamplerState Linear : register(s0);

cbuffer UpsampleConstants : register(b0)
{
    float2 TargetSize;
    float Opacity;
    float Padding;
};

float4 main(float4 position : SV_Position) : SV_Target
{
    return Layer.SampleLevel(Linear, position.xy / TargetSize, 0) * Opacity;
}
