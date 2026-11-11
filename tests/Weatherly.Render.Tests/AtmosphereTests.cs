using System.Numerics;
using Vortice.Direct3D11;
using Weatherly.Core.Geometry;
using Weatherly.Core.Overlay;
using Weatherly.Core.Rendering;

namespace Weatherly.Render.Tests;

/// <summary>Renders fog and sunlight with WARP and checks the effect of the windows on them.</summary>
public sealed class AtmosphereTests : IDisposable
{
    private static readonly PixelSize s_size = new(320, 200);
    private static readonly PixelRect s_window = PixelRect.FromSize(100, 80, 120, 80);

    private readonly ID3D11Device _device = GraphicsDeviceFactory.Create(GraphicsDriver.Warp);
    private readonly OverlayPainter _painter;

    public AtmosphereTests()
    {
        _painter = new OverlayPainter(_device);
    }

    [Fact]
    public void Fog_CoversTheScreenSoftly_AndThinnerOverWindows()
    {
        var frame = new RenderFrame { Fog = new FogLayer(1f, Vector2.Zero, 0f, new Vector3(0.86f, 0.89f, 0.93f), 0.35f, 1f) };
        frame.AddOccluder(s_window);

        RenderedImage image = Render("atmosphere-fog", frame);

        double outside = MeanAlpha(image, PixelRect.FromSize(0, 120, 90, 80));
        double over = MeanAlpha(image, PixelRect.FromSize(120, 100, 80, 50));
        outside.Should().BeInRange(5, 0.35 * 255 + 1, "visible but never thicker than the maximum");
        over.Should().BeLessThan(outside * 0.8, "windows rise out of the fog");
        MaxAlpha(image).Should().BeLessThanOrEqualTo((int)(0.35 * 255) + 20, "the window rim adds only a faint light");
    }

    [Fact]
    public void Fog_IsDenserTowardsTheBottom()
    {
        var frame = new RenderFrame { Fog = new FogLayer(1f, Vector2.Zero, 0f, Vector3.One, 0.35f, 1f) };

        RenderedImage image = Render("atmosphere-fog-height", frame);

        MeanAlpha(image, PixelRect.FromSize(0, 150, 320, 50)).Should().BeGreaterThan(MeanAlpha(image, PixelRect.FromSize(0, 0, 320, 50)));
    }

    [Fact]
    public void Sun_GlowsNearTheSun_AndTheWindowCastsAShadow()
    {
        // Sun above the top right corner; the window throws its shadow down and to the left.
        var frame = new RenderFrame { Sun = new SunLayer(1f, new Vector2(330, -40), new Vector3(1f, 0.86f, 0.58f), 1f) };
        frame.AddOccluder(s_window);

        RenderedImage image = Render("atmosphere-sun", frame);

        double nearSun = MeanRed(image, PixelRect.FromSize(270, 0, 50, 40));
        double farAway = MeanRed(image, PixelRect.FromSize(0, 160, 50, 40));
        nearSun.Should().BeGreaterThan(farAway * 1.5, "the glow is around the sun");

        // Behind the window (seen from the sun) versus the same distance beside the shadow.
        double shadow = MeanRed(image, PixelRect.FromSize(40, 170, 40, 25));
        double lit = MeanRed(image, PixelRect.FromSize(250, 170, 40, 25));
        shadow.Should().BeLessThan(lit, "the window blocks the rays");
    }

    [Fact]
    public void NoAtmosphere_LeavesTheTargetTransparent()
    {
        var frame = new RenderFrame();
        frame.AddOccluder(s_window);

        RenderedImage image = Render("atmosphere-none", frame);

        MaxAlpha(image).Should().Be(0);
    }

    public void Dispose()
    {
        _painter.Dispose();
        _device.Dispose();
    }

    private static double MeanAlpha(RenderedImage image, PixelRect area) => Mean(image, area, pixel => pixel.Alpha);

    private static double MeanRed(RenderedImage image, PixelRect area) => Mean(image, area, pixel => pixel.Red);

    private static double Mean(RenderedImage image, PixelRect area, Func<Pixel, int> channel)
    {
        double sum = 0;
        for (int y = area.Top; y < area.Bottom; y++)
        {
            for (int x = area.Left; x < area.Right; x++)
            {
                sum += channel(image[x, y]);
            }
        }

        return sum / (area.Width * area.Height);
    }

    private static int MaxAlpha(RenderedImage image)
    {
        int max = 0;
        for (int y = 0; y < s_size.Height; y++)
        {
            for (int x = 0; x < s_size.Width; x++)
            {
                max = Math.Max(max, image[x, y].Alpha);
            }
        }

        return max;
    }

    private RenderedImage Render(string previewName, RenderFrame frame)
    {
        using var target = new OffscreenTarget(_device, s_size);
        _painter.Paint(target.View, s_size, new OverlayFrame(OverlayContent.None, 1f, PixelRect.FromSize(0, 0, s_size.Width, s_size.Height), Scene: frame));
        var image = new RenderedImage(target.ReadPixels(), s_size);
        image.SavePreview(previewName);
        return image;
    }
}
