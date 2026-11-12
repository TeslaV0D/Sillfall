using System.Numerics;
using Sillfall.Core.Geometry;
using Sillfall.Core.Overlay;
using Sillfall.Core.Rendering;
using Vortice.Direct2D1;
using Vortice.Direct3D11;
using Vortice.Mathematics;

namespace Sillfall.Render.Tests;

/// <summary>Direct2D draws on the same device and target as the Direct3D pipelines (WARP).</summary>
public sealed class Direct2DCanvasTests : IDisposable
{
    private static readonly PixelSize s_size = new(64, 48);

    private readonly ID3D11Device _device = GraphicsDeviceFactory.Create(GraphicsDriver.Warp);

    [Fact]
    public void FillEllipse_DrawsIntoATransparentTarget()
    {
        using var canvas = new Direct2DCanvas(_device);
        using var target = new OffscreenTarget(_device, s_size);
        _device.ImmediateContext.ClearRenderTargetView(target.View, new Color4(0, 0, 0, 0));

        ID2D1DeviceContext context = canvas.Begin(target.Texture);
        using (ID2D1SolidColorBrush red = context.CreateSolidColorBrush(new Color4(1, 0, 0, 1)))
        {
            context.FillEllipse(new Ellipse(new Vector2(32, 24), 10, 10), red);
        }

        canvas.End();

        var image = new RenderedImage(target.ReadPixels(), s_size);
        image.SavePreview("direct2d-ellipse");
        image[32, 24].Should().Be(new Pixel(255, 0, 0, 255));
        image[2, 2].Should().Be(Pixel.Transparent);
        image[42, 24].Alpha.Should().BeLessThan(255, "Direct2D antialiases the edge");
    }

    [Fact]
    public void Direct2D_BlendsOverWhatDirect3DDrew()
    {
        using var painter = new OverlayPainter(_device);
        using var canvas = new Direct2DCanvas(_device);
        using var target = new OffscreenTarget(_device, s_size);
        var frame = new RenderFrame();
        frame.Add(new Sprite(new Vector2(32, 24), new Vector2(32, 24), 0f, SpriteShape.Rectangle, new Vector4(0, 0, 1, 1)));
        painter.Paint(target.View, s_size, new OverlayFrame(OverlayContent.None, 1f, PixelRect.FromSize(0, 0, 64, 48), Scene: frame));

        ID2D1DeviceContext context = canvas.Begin(target.Texture);
        using (ID2D1SolidColorBrush halfRed = context.CreateSolidColorBrush(new Color4(1, 0, 0, 0.5f)))
        {
            context.FillRoundedRectangle(new RoundedRectangle(new System.Drawing.RectangleF(16, 12, 32, 24), 4, 4), halfRed);
        }

        canvas.End();

        Pixel pixel = new RenderedImage(target.ReadPixels(), s_size)[32, 24];
        ((int)pixel.Red).Should().BeInRange(126, 129);
        ((int)pixel.Blue).Should().BeInRange(126, 129);
        pixel.Alpha.Should().Be(255);
    }

    [Fact]
    public void End_WithoutBegin_Throws()
    {
        using var canvas = new Direct2DCanvas(_device);

        Action end = canvas.End;

        end.Should().Throw<InvalidOperationException>();
    }

    public void Dispose() => _device.Dispose();
}
