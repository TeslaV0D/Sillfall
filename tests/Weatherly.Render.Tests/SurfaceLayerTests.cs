using Vortice.Direct3D11;
using Weatherly.Core.Desktop;
using Weatherly.Core.Displays;
using Weatherly.Core.Geometry;
using Weatherly.Core.Overlay;
using Weatherly.Core.Surfaces;

namespace Weatherly.Render.Tests;

/// <summary>
/// Renders the surface debug layer of a synthetic desktop with WARP and checks that every band sits
/// exactly where <see cref="SurfaceGeometry"/> says, with nothing drawn on the windows themselves.
/// </summary>
public sealed class SurfaceLayerTests : IDisposable
{
    private static readonly Pixel s_top = new(51, 242, 77, 255);
    private static readonly Pixel s_side = new(255, 153, 26, 255);
    private static readonly Pixel s_floor = new(51, 153, 255, 255);

    /// <summary>640 x 400 monitor with a 20 px taskbar.</summary>
    private static readonly MonitorInfo s_monitor = new(
        @"\\.\DISPLAY1", PixelRect.FromSize(0, 0, 640, 400), PixelRect.FromSize(0, 0, 640, 380), MonitorInfo.DefaultDpi, IsPrimary: true);

    /// <summary>Topmost window, overlapping the top edge and the right side of <see cref="s_lower"/>.</summary>
    private static readonly TrackedWindow s_higher = new(2, PixelRect.FromSize(300, 50, 200, 150));

    private static readonly TrackedWindow s_lower = new(1, PixelRect.FromSize(100, 100, 300, 200));

    private readonly ID3D11Device _device = GraphicsDeviceFactory.Create(GraphicsDriver.Warp);
    private readonly OverlayPainter _painter;

    public SurfaceLayerTests()
    {
        _painter = new OverlayPainter(_device);
    }

    [Fact]
    public void TopEdge_IsTwoRowsDirectlyAboveTheVisiblePartOfTheWindow()
    {
        RenderedImage image = Render(s_monitor, 1f, "surfaces-two-windows-100", s_higher, s_lower);

        image[150, 98].Should().Be(s_top);
        image[150, 99].Should().Be(s_top);
        image[150, 97].Should().Be(Pixel.Transparent, "the band is exactly two rows thick");
        image[150, 100].Should().Be(Pixel.Transparent, "nothing is drawn on the window frame");
        image[299, 99].Should().Be(s_top, "the band reaches up to the higher window");
        image[300, 99].Should().Be(Pixel.Transparent, "the hidden part of the edge lies inside the higher window");
    }

    [Fact]
    public void Sides_AreTwoColumnsOutsideTheFrameWhereNothingCoversThem()
    {
        RenderedImage image = Render(s_monitor, 1f, "surfaces-two-windows-100", s_higher, s_lower);

        image[99, 200].Should().Be(s_side);
        image[98, 200].Should().Be(s_side);
        image[97, 200].Should().Be(Pixel.Transparent);
        image[100, 200].Should().Be(Pixel.Transparent);

        // The right side of the lower window is covered by the higher window down to row 200.
        image[400, 250].Should().Be(s_side);
        image[401, 250].Should().Be(s_side);
        image[402, 250].Should().Be(Pixel.Transparent);
        image[400, 150].Should().Be(Pixel.Transparent);
    }

    [Fact]
    public void Floor_IsTwoRowsAboveTheTaskbar()
    {
        RenderedImage image = Render(s_monitor, 1f, "surfaces-two-windows-100", s_higher, s_lower);

        image[10, 378].Should().Be(s_floor);
        image[10, 379].Should().Be(s_floor);
        image[10, 377].Should().Be(Pixel.Transparent);
        image[10, 380].Should().Be(Pixel.Transparent);
    }

    [Fact]
    public void EveryDrawnPixel_BelongsToABandOfTheExtractedSurfaces()
    {
        RenderedImage image = Render(s_monitor, 1f, "surfaces-two-windows-100", s_higher, s_lower);
        SurfaceSet surfaces = Extract(s_monitor, s_higher, s_lower);
        int thickness = OverlayPainter.DebugLineThickness(1f);
        var bands = new List<PixelRect>();
        foreach (SurfaceSegment segment in surfaces.Segments)
        {
            bands.Add(SurfaceGeometry.Band(segment, thickness));
        }

        var mismatches = new List<string>();
        for (int y = 0; y < image.Size.Height; y++)
        {
            for (int x = 0; x < image.Size.Width; x++)
            {
                bool inBand = bands.Exists(band => band.Contains(x, y));
                bool drawn = image[x, y] != Pixel.Transparent;
                if (drawn != inBand)
                {
                    mismatches.Add($"({x}, {y}) {(drawn ? "drawn outside" : "missing inside")} a band");
                }
            }
        }

        mismatches.Should().BeEmpty();
    }

    [Fact]
    public void Bands_ScaleWithTheDpi()
    {
        RenderedImage image = Render(s_monitor, 2f, "surfaces-two-windows-200", s_higher, s_lower);

        Enumerable.Range(96, 4).Select(y => image[150, y]).Should().AllBeEquivalentTo(s_top);
        image[150, 95].Should().Be(Pixel.Transparent);
    }

    [Fact]
    public void MonitorWithNegativeCoordinates_DrawsAtMonitorLocalPositions()
    {
        var monitor = new MonitorInfo(
            @"\\.\DISPLAY2", PixelRect.FromSize(-640, -100, 640, 400), PixelRect.FromSize(-640, -100, 640, 400), MonitorInfo.DefaultDpi, IsPrimary: false);
        var window = new TrackedWindow(5, PixelRect.FromSize(-500, 0, 200, 100));

        RenderedImage image = Render(monitor, 1f, "surfaces-negative-monitor", window);

        // Desktop row -1 above the window is monitor row 99, desktop column -400 is monitor column 240.
        image[240, 99].Should().Be(s_top);
        image[240, 98].Should().Be(s_top);
        image[240, 100].Should().Be(Pixel.Transparent);
        image[139, 150].Should().Be(s_side);
    }

    [Fact]
    public void NoSurfaces_LeavesTheTargetTransparent()
    {
        using var target = new OffscreenTarget(_device, new PixelSize(64, 48));

        _painter.Paint(target.View, target.Size, new OverlayFrame(OverlayContent.None, 1f, PixelRect.FromSize(0, 0, 64, 48), Surfaces: null));

        target.ReadPixels().Should().OnlyContain(channel => channel == 0);
    }

    public void Dispose()
    {
        _painter.Dispose();
        _device.Dispose();
    }

    private static SurfaceSet Extract(MonitorInfo monitor, params TrackedWindow[] windowsTopmostFirst)
    {
        var surfaces = new SurfaceSet();
        new SurfaceExtractor().Extract(WindowSnapshot.Of(0, windowsTopmostFirst), [monitor], surfaces);
        return surfaces;
    }

    private RenderedImage Render(MonitorInfo monitor, float scale, string previewName, params TrackedWindow[] windowsTopmostFirst)
    {
        SurfaceSet surfaces = Extract(monitor, windowsTopmostFirst);
        PixelSize size = monitor.Bounds.Size;
        using var target = new OffscreenTarget(_device, size);

        _painter.Paint(target.View, size, new OverlayFrame(OverlayContent.None, scale, monitor.Bounds, surfaces));

        var image = new RenderedImage(target.ReadPixels(), size);
        image.SavePreview(previewName);
        return image;
    }
}
