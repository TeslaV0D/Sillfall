using Sillfall.Core.Geometry;
using Sillfall.Core.Overlay;
using Vortice.Direct3D11;

namespace Sillfall.Render.Tests;

/// <summary>
/// Renders the calibration pattern with WARP and checks the pixels against <see cref="TestPatternLayout"/>.
/// These are the invariants every overlay shader has to keep: premultiplied alpha, and full
/// transparency wherever nothing is drawn.
/// </summary>
public sealed class TestPatternTests : IDisposable
{
    private static readonly Pixel s_magenta = new(255, 0, 255, 255);
    private static readonly Pixel s_red = new(255, 0, 0, 255);
    private static readonly Pixel s_green = new(0, 255, 0, 255);
    private static readonly Pixel s_blue = new(0, 0, 255, 255);
    private static readonly Pixel s_yellow = new(255, 255, 0, 255);
    private static readonly Pixel s_cyan = new(0, 255, 255, 255);

    private readonly ID3D11Device _device = GraphicsDeviceFactory.Create(GraphicsDriver.Warp);
    private readonly OverlayPainter _painter;

    public TestPatternTests()
    {
        _painter = new OverlayPainter(_device);
    }

    /// <summary>Monitor sizes and DPI scales: 100 %, 150 % (rounds to 2 px lines), 200 % and an odd-sized target.</summary>
    public static TheoryData<int, int, float> Targets => new()
    {
        { 640, 360, 1.0f },
        { 960, 540, 1.5f },
        { 1280, 720, 2.0f },
        { 801, 451, 1.25f },
    };

    [Theory]
    [MemberData(nameof(Targets))]
    public void Frame_TouchesAllFourEdges(int width, int height, float scale)
    {
        (RenderedImage image, TestPatternLayout layout) = Render(width, height, scale);
        int middleX = width / 2;
        int middleY = height / 2;

        for (int i = 0; i < layout.Border; i++)
        {
            image[i, middleY].Should().Be(s_magenta);
            image[width - 1 - i, middleY].Should().Be(s_magenta);
            image[middleX, i].Should().Be(s_magenta);
            image[middleX, height - 1 - i].Should().Be(s_magenta);
        }

        // Directly inside the frame the color changes, so the frame is exactly as thick as the layout says.
        image[layout.Border, middleY].Should().NotBe(s_magenta);
        image[middleX, height - 1 - layout.Border].Should().NotBe(s_magenta);
    }

    [Theory]
    [MemberData(nameof(Targets))]
    public void CornerMarkers_IdentifyEachCorner(int width, int height, float scale)
    {
        (RenderedImage image, TestPatternLayout layout) = Render(width, height, scale);
        int near = layout.Border + (layout.Corner / 2);

        image[near, near].Should().Be(s_red);
        image[width - 1 - near, near].Should().Be(s_green);
        image[near, height - 1 - near].Should().Be(s_blue);
        image[width - 1 - near, height - 1 - near].Should().Be(s_yellow);
    }

    [Theory]
    [MemberData(nameof(Targets))]
    public void Cross_MarksTheCenter(int width, int height, float scale)
    {
        (RenderedImage image, _) = Render(width, height, scale);

        image[width / 2, height / 2].Should().Be(s_cyan);
    }

    [Theory]
    [MemberData(nameof(Targets))]
    public void AlphaRamp_FadesFromTransparentToOpaqueWhite(int width, int height, float scale)
    {
        (RenderedImage image, TestPatternLayout layout) = Render(width, height, scale);
        PixelRect ramp = layout.AlphaRamp;
        int y = RowOffGrid(layout, ramp);

        var alphas = new List<byte>();
        for (int x = ramp.Left; x < ramp.Right; x++)
        {
            if (IsOnGrid(layout, x, y))
            {
                continue;
            }

            Pixel pixel = image[x, y];
            pixel.Should().Be(new Pixel(pixel.Alpha, pixel.Alpha, pixel.Alpha, pixel.Alpha), "premultiplied white has equal channels");
            alphas.Add(pixel.Alpha);
        }

        alphas.Should().BeInAscendingOrder();
        alphas[0].Should().BeLessThan(4);
        alphas[^1].Should().BeGreaterThan(251);
    }

    [Theory]
    [MemberData(nameof(Targets))]
    public void Shade_IsBlackAtHalfAlpha(int width, int height, float scale)
    {
        (RenderedImage image, TestPatternLayout layout) = Render(width, height, scale);
        PixelRect shade = layout.Shade;
        int y = RowOffGrid(layout, shade);
        int x = Enumerable.Range(shade.Left, shade.Width).First(column => !IsOnGrid(layout, column, y));

        Pixel pixel = image[x, y];

        pixel.Alpha.Should().BeInRange(127, 128);
        (pixel.Red, pixel.Green, pixel.Blue).Should().Be(((byte)0, (byte)0, (byte)0));
    }

    [Theory]
    [MemberData(nameof(Targets))]
    public void AreasWithoutContent_AreFullyTransparent(int width, int height, float scale)
    {
        (RenderedImage image, TestPatternLayout layout) = Render(width, height, scale);

        // Just outside the top left marker: off the grid, away from the ramp, the shade and the cross.
        int offset = layout.Border + layout.Corner + (5 * layout.Unit);
        IsOnGrid(layout, offset, offset).Should().BeFalse("the sample point must not sit on a grid line");

        image[offset, offset].Should().Be(Pixel.Transparent);
        image[width - 1 - offset, offset].Should().Be(Pixel.Transparent);
    }

    [Theory]
    [MemberData(nameof(Targets))]
    public void EveryPixel_IsValidPremultipliedAlpha(int width, int height, float scale)
    {
        (RenderedImage image, _) = Render(width, height, scale);

        image.Pixels()
            .Where(pixel => pixel.Red > pixel.Alpha || pixel.Green > pixel.Alpha || pixel.Blue > pixel.Alpha)
            .Should().BeEmpty("a color channel above alpha would glow additively on the desktop");
    }

    [Fact]
    public void Grid_HasOneCellPerHundredDips()
    {
        (RenderedImage image, TestPatternLayout layout) = Render(960, 540, 2.0f);
        int y = layout.Border + layout.Corner + 10;

        // At 200 % a cell is 200 px wide and a line 2 px thick.
        image[200, y].Alpha.Should().BeInRange(37, 39);
        image[201, y].Alpha.Should().BeInRange(37, 39);
        image[202, y].Should().Be(Pixel.Transparent);
        image[199, y].Should().Be(Pixel.Transparent);
    }

    [Fact]
    public void ContentNone_LeavesTheTargetTransparent()
    {
        using var target = new OffscreenTarget(_device, new PixelSize(64, 48));

        // Draw the pattern first, so the test also proves that a later frame clears it.
        _painter.Paint(target.View, target.Size, new OverlayFrame(OverlayContent.TestPattern, 1f));
        _painter.Paint(target.View, target.Size, new OverlayFrame(OverlayContent.None, 1f));

        target.ReadPixels().Should().OnlyContain(channel => channel == 0);
    }

    public void Dispose()
    {
        _painter.Dispose();
        _device.Dispose();
    }

    private (RenderedImage Image, TestPatternLayout Layout) Render(int width, int height, float scale)
    {
        var size = new PixelSize(width, height);
        using var target = new OffscreenTarget(_device, size);

        _painter.Paint(target.View, size, new OverlayFrame(OverlayContent.TestPattern, scale));

        var image = new RenderedImage(target.ReadPixels(), size);
        image.SavePreview($"test-pattern-{width}x{height}-{(int)(scale * 100)}");
        return (image, TestPatternLayout.Create(size, scale));
    }

    private static bool IsOnGrid(TestPatternLayout layout, int x, int y) =>
        x % layout.GridSpacing < layout.Unit || y % layout.GridSpacing < layout.Unit;

    /// <summary>A row inside <paramref name="rect"/> that no horizontal grid line crosses.</summary>
    private static int RowOffGrid(TestPatternLayout layout, PixelRect rect) =>
        Enumerable.Range(rect.Top, rect.Height).First(row => row % layout.GridSpacing >= layout.Unit);
}
