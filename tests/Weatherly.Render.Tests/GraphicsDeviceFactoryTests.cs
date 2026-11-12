using Vortice.Direct3D;
using Vortice.Direct3D11;
using Vortice.DXGI;

namespace Weatherly.Render.Tests;

public class GraphicsDeviceFactoryTests
{
    [Fact]
    public void Create_Warp_ReturnsDirect3D11Device()
    {
        using ID3D11Device device = GraphicsDeviceFactory.Create(GraphicsDriver.Warp);

        device.FeatureLevel.Should().BeOneOf(FeatureLevel.Level_11_0, FeatureLevel.Level_11_1);
    }

    [Fact]
    public void Create_Warp_SupportsBgraRenderTargets()
    {
        using ID3D11Device device = GraphicsDeviceFactory.Create(GraphicsDriver.Warp);

        device.CreationFlags.Should().HaveFlag(DeviceCreationFlags.BgraSupport);
        device.CheckFormatSupport(Format.B8G8R8A8_UNorm).Should().HaveFlag(FormatSupport.RenderTarget);
    }

    [Fact]
    public void Create_Warp_RendersToAnOffscreenTarget()
    {
        using ID3D11Device device = GraphicsDeviceFactory.Create(GraphicsDriver.Warp);
        using ID3D11Texture2D target = device.CreateTexture2D(new Texture2DDescription(
            Format.B8G8R8A8_UNorm,
            width: 4,
            height: 4,
            arraySize: 1,
            mipLevels: 1,
            BindFlags.RenderTarget));
        using ID3D11RenderTargetView view = device.CreateRenderTargetView(target);
        using ID3D11Texture2D readback = device.CreateTexture2D(new Texture2DDescription(
            Format.B8G8R8A8_UNorm,
            width: 4,
            height: 4,
            arraySize: 1,
            mipLevels: 1,
            BindFlags.None,
            ResourceUsage.Staging,
            CpuAccessFlags.Read));

        // Premultiplied half-transparent white, the pixel format every overlay shader outputs.
        device.ImmediateContext.ClearRenderTargetView(view, new Vortice.Mathematics.Color4(0.5f, 0.5f, 0.5f, 0.5f));
        device.ImmediateContext.CopyResource(readback, target);

        MappedSubresource mapped = device.ImmediateContext.Map(readback, 0, MapMode.Read);
        try
        {
            ReadOnlySpan<byte> firstPixel = mapped.AsSpan(4);
            firstPixel.ToArray().Should().AllSatisfy(channel => channel.Should().BeInRange(127, 128));
        }
        finally
        {
            device.ImmediateContext.Unmap(readback, 0);
        }
    }
}
