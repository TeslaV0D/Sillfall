using Vortice.Direct3D11;
using Weatherly.Core.Geometry;
using Weatherly.Core.Overlay;

namespace Weatherly.Render.Tests;

public sealed class CompositionSwapChainTests : IDisposable
{
    private readonly ID3D11Device _device = GraphicsDeviceFactory.Create(GraphicsDriver.Warp);

    [Fact]
    public void Create_KeepsTheRequestedSize()
    {
        using var swapChain = new CompositionSwapChain(_device, new PixelSize(320, 200));

        swapChain.Size.Should().Be(new PixelSize(320, 200));
    }

    [Fact]
    public void Present_AcceptsSeveralFramesInARow()
    {
        using var painter = new OverlayPainter(_device);
        using var swapChain = new CompositionSwapChain(_device, new PixelSize(320, 200));

        for (int frame = 0; frame < 3; frame++)
        {
            swapChain.WaitForFrame();
            painter.Paint(swapChain.BackBuffer, swapChain.Size, new OverlayFrame(OverlayContent.TestPattern, 1f));
            swapChain.Present().Should().Be(PresentResult.Presented);
        }
    }

    [Theory]
    [InlineData(0, 100)]
    [InlineData(100, 0)]
    public void Create_RejectsEmptySizes(int width, int height)
    {
        Action create = () => _ = new CompositionSwapChain(_device, new PixelSize(width, height));

        create.Should().Throw<ArgumentOutOfRangeException>();
    }

    public void Dispose()
    {
        _device.Dispose();
    }
}
