using System.Numerics;
using Sillfall.Core.Geometry;
using Sillfall.Core.Overlay;
using Sillfall.Core.Rendering;
using Vortice.Direct3D11;

namespace Sillfall.Render.Tests;

/// <summary>Renders single sprites with WARP and checks shape, position and premultiplied output.</summary>
public sealed class SpriteTests : IDisposable
{
    private static readonly PixelSize s_size = new(64, 48);

    private readonly ID3D11Device _device = GraphicsDeviceFactory.Create(GraphicsDriver.Warp);
    private readonly OverlayPainter _painter;

    public SpriteTests()
    {
        _painter = new OverlayPainter(_device);
    }

    [Fact]
    public void Rectangle_CoversExactlyItsPixels()
    {
        RenderedImage image = Render("sprite-rectangle", new Sprite(new Vector2(20, 10), new Vector2(4, 2), 0f, SpriteShape.Rectangle, new Vector4(1, 0, 0, 1)));

        image[16, 8].Should().Be(new Pixel(255, 0, 0, 255));
        image[23, 11].Should().Be(new Pixel(255, 0, 0, 255));
        image[15, 8].Should().Be(Pixel.Transparent);
        image[24, 8].Should().Be(Pixel.Transparent);
        image[20, 12].Should().Be(Pixel.Transparent);
    }

    [Fact]
    public void Disc_IsOpaqueInTheMiddleAndSoftAtTheEdge()
    {
        RenderedImage image = Render("sprite-disc", new Sprite(new Vector2(32, 24), new Vector2(6, 6), 0f, SpriteShape.Disc, new Vector4(0, 0, 1, 1)));

        image[32, 24].Should().Be(new Pixel(0, 0, 255, 255));
        image[32, 15].Should().Be(Pixel.Transparent, "9 px from the center is outside a 6 px disc");
        // Pixel centers lie on half pixels: (36.5, 28.5) is 6.36 px from the center, inside the one-pixel soft edge.
        image[36, 28].Alpha.Should().BeInRange(1, 254, "the edge is antialiased");
    }

    [Fact]
    public void Streak_IsLongAlongItsRotatedAxis()
    {
        // 20 px long, 2 px radius, rotated 90 degrees: vertical.
        RenderedImage image = Render("sprite-streak", new Sprite(new Vector2(32, 24), new Vector2(10, 2), MathF.PI / 2, SpriteShape.Streak, new Vector4(1, 1, 1, 1)));

        image[32, 17].Alpha.Should().BeGreaterThan(100);
        image[32, 31].Alpha.Should().BeGreaterThan(100);
        image[40, 24].Should().Be(Pixel.Transparent);
    }

    [Fact]
    public void Flake_DrawsEveryVariantAsASixArmedOrRoundShape()
    {
        // One 9 px flake per variant in a row, white.
        var sprites = new Sprite[Sprite.FlakeVariants];
        for (int v = 0; v < sprites.Length; v++)
        {
            sprites[v] = new Sprite(new Vector2(6 + (v * 10.5f), 24), new Vector2(4.5f, 4.5f), 0f, SpriteShape.Flake, Vector4.One) { Variant = v };
        }

        RenderedImage image = Render("sprite-flake", sprites);

        image[6, 24].Alpha.Should().Be(255, "the blob is solid in the middle");
        image[27, 24].Alpha.Should().BeGreaterThan(180, "the centre of the branched star");

        // Star (variant 1): along an arm it is bright, between two arms only the faint glow remains.
        // With rotation 0 the arms point to 30, 90, 150 ... degrees; 0 degrees lies between two arms.
        // Its centre is x = 16.5; pixel 16 has its centre there.
        image[16, 21].Alpha.Should().BeGreaterThan(150, "an arm points straight up");
        image[19, 24].Alpha.Should().BeLessThan(110, "no arm points to the right");
        image[32, 5].Should().Be(Pixel.Transparent, "far above the row of flakes");
    }

    [Fact]
    public void Leaf_DrawsEveryVariantAlongItsAxis()
    {
        // One leaf per variant, 14 px long and 7 px wide, lying along x, side by side.
        var sprites = new Sprite[Sprite.LeafVariants];
        for (int v = 0; v < sprites.Length; v++)
        {
            sprites[v] = new Sprite(new Vector2(8 + (v * 16), 24), new Vector2(7f, 3.5f), 0f, SpriteShape.Leaf, new Vector4(0.9f, 0.4f, 0.1f, 1f)) { Variant = v };
        }

        RenderedImage image = Render("sprite-leaf", sprites);

        for (int v = 0; v < sprites.Length; v++)
        {
            int cx = 8 + (v * 16);
            image[cx + 2, 23].Alpha.Should().BeGreaterThan(200, $"variant {v} is solid in its blade");
            image[cx, 24 - 6].Alpha.Should().Be(0, $"variant {v} is narrower than it is long");
        }

        // The midrib is darker than the blade beside it (pointed oval).
        ((int)image[10, 24].Red).Should().BeLessThan(image[10, 22].Red);
    }

    [Fact]
    public void Blade_StandsOnItsRootAndCurvesTowardsTheTip()
    {
        // Two 30 px blades standing up from row 40: one straight, one curving right by 0.4 of its length.
        const float HalfLength = 15f;
        RenderedImage image = Render(
            "sprite-blade",
            new Sprite(new Vector2(16, 40 - HalfLength), new Vector2(HalfLength, Sprite.BladeHalfHeight(HalfLength, 0f)), -MathF.PI / 2, SpriteShape.Blade, new Vector4(0.3f, 0.6f, 0.2f, 1f)),
            new Sprite(new Vector2(40, 40 - HalfLength), new Vector2(HalfLength, Sprite.BladeHalfHeight(HalfLength, 0.4f)), -MathF.PI / 2, SpriteShape.Blade, new Vector4(0.3f, 0.6f, 0.2f, 1f)) { Variant = 0.4f });

        image[16, 38].Alpha.Should().BeGreaterThan(200, "solid at the root");
        image[16, 14].Alpha.Should().BeGreaterThan(30, "still there near the tip");
        image[16, 8].Alpha.Should().Be(0, "nothing above the tip");
        image[19, 38].Alpha.Should().Be(0, "thin");

        // The curved blade: 11.5 px up its axis the curve is 0.4 x 30 px x t^2 (t = 0.88), about 9 px to the right.
        image[40, 38].Alpha.Should().BeGreaterThan(200);
        image[40, 13].Alpha.Should().BeLessThan(40, "the tip has moved away from the axis");
        image[49, 13].Alpha.Should().BeGreaterThan(30, "to the right");
    }

    [Fact]
    public void Raindrop_HeadIsBrighterThanTail()
    {
        // 20 px long, 3 px head radius, lying along x with the head at +x.
        RenderedImage image = Render(
            "sprite-raindrop-head",
            new Sprite(new Vector2(32, 24), new Vector2(10, 3), 0f, SpriteShape.Raindrop, new Vector4(1, 1, 1, 1)) { Param = 0.5f });

        byte head = image[40, 24].Alpha;
        byte tail = image[24, 24].Alpha;
        ((int)head).Should().BeGreaterThan(2 * tail, "the head fades less than the tail");
    }

    [Fact]
    public void Raindrop_WithRim_EdgeIsDarkerThanCore()
    {
        RenderedImage image = Render(
            "sprite-raindrop-rim",
            new Sprite(new Vector2(32, 24), new Vector2(10, 4), 0f, SpriteShape.Raindrop, new Vector4(1, 1, 1, 1)) { Param = 1f });

        Pixel core = image[40, 24];
        Pixel edge = image[40, 26];
        core.Alpha.Should().BeGreaterThan(0);
        edge.Alpha.Should().BeGreaterThan(0);
        float coreRatio = core.Red / (float)core.Alpha;
        float edgeRatio = edge.Red / (float)edge.Alpha;
        edgeRatio.Should().BeLessThan(coreRatio, "the dark rim lowers rgb/a at the edge");
    }

    [Fact]
    public void Raindrop_StaysPremultiplied()
    {
        RenderedImage image = Render(
            "sprite-raindrop-premul",
            new Sprite(new Vector2(32, 24), new Vector2(10, 3), 0f, SpriteShape.Raindrop, new Vector4(0.7f, 0.8f, 0.9f, 1f)) { Param = 1f });

        for (int y = 0; y < s_size.Height; y++)
        {
            for (int x = 0; x < s_size.Width; x++)
            {
                Pixel p = image[x, y];
                p.Red.Should().BeLessThanOrEqualTo(p.Alpha, $"r <= a at ({x},{y})");
                p.Green.Should().BeLessThanOrEqualTo(p.Alpha, $"g <= a at ({x},{y})");
                p.Blue.Should().BeLessThanOrEqualTo(p.Alpha, $"b <= a at ({x},{y})");
            }
        }
    }

    [Fact]
    public void WaterFilm_FollowsLeftAndRightHeights()
    {
        // Quad from y = 20..30 (center 25, half height 5). Left height 8 px, right height 2 px.
        RenderedImage image = Render(
            "sprite-waterfilm-heights",
            new Sprite(new Vector2(32, 25), new Vector2(16, 5), 0f, SpriteShape.WaterFilm, new Vector4(0, 0, 1, 1))
            {
                Variant = 8f,
                Param = 2f,
            });

        // Left column: filled almost to the top of the quad. Right column: only the bottom 2 px.
        image[20, 23].Alpha.Should().BeGreaterThan(200, "left side is filled high");
        image[44, 23].Alpha.Should().Be(0, "right side is only 2 px tall");
        image[44, 28].Alpha.Should().BeGreaterThan(200, "right side still fills its lower height");
    }

    [Fact]
    public void WaterFilm_AdjacentBins_HaveNoSeam()
    {
        // Two abutting 8 px-wide bins of equal height: the seam pixel must match the middle (±2).
        var left = new Sprite(new Vector2(28, 24), new Vector2(4, 4), 0f, SpriteShape.WaterFilm, new Vector4(1, 1, 1, 1))
        {
            Variant = 6f,
            Param = 6f,
        };
        var right = new Sprite(new Vector2(36, 24), new Vector2(4, 4), 0f, SpriteShape.WaterFilm, new Vector4(1, 1, 1, 1))
        {
            Variant = 6f,
            Param = 6f,
        };
        RenderedImage image = Render("sprite-waterfilm-seam", left, right);

        int seam = image[32, 26].Alpha;
        int mid = image[28, 26].Alpha;
        Math.Abs(seam - mid).Should().BeLessThanOrEqualTo(2, "hard sides keep abutting bins from brightening at the seam");
    }

    [Fact]
    public void Cutouts_LeaveTheirWindowsClearOfWeather()
    {
        var frame = new RenderFrame();
        frame.Add(new Sprite(new Vector2(32, 24), new Vector2(30, 20), 0f, SpriteShape.Rectangle, new Vector4(0.5f, 0.5f, 0.5f, 0.5f)));
        frame.AddCutout(PixelRect.FromSize(10, 10, 20, 20));
        using var target = new OffscreenTarget(_device, s_size);

        _painter.Paint(target.View, s_size, new OverlayFrame(OverlayContent.None, 1f, PixelRect.FromSize(0, 0, 64, 48), Scene: frame));

        var image = new RenderedImage(target.ReadPixels(), s_size);
        image.SavePreview("sprite-cutout");
        image[15, 15].Should().Be(Pixel.Transparent, "inside the cut-out window");
        image[40, 15].Alpha.Should().BeInRange(127, 128, "the weather around it stays");
    }

    [Fact]
    public void Opacity_ScalesTheWeatherOfAMonitor()
    {
        var frame = new RenderFrame();
        frame.Add(new Sprite(new Vector2(32, 24), new Vector2(30, 20), 0f, SpriteShape.Rectangle, new Vector4(1f, 1f, 1f, 1f)));
        using var target = new OffscreenTarget(_device, s_size);

        _painter.Paint(target.View, s_size, new OverlayFrame(OverlayContent.None, 1f, PixelRect.FromSize(0, 0, 64, 48), Scene: frame, Opacity: 0.4f));

        var image = new RenderedImage(target.ReadPixels(), s_size);
        ((int)image[32, 24].Alpha).Should().BeInRange(101, 103);
    }

    [Fact]
    public void HalfTransparentSprites_BlendOverEachOther()
    {
        RenderedImage image = Render(
            "sprite-blend",
            new Sprite(new Vector2(32, 24), new Vector2(8, 8), 0f, SpriteShape.Rectangle, new Vector4(0.5f, 0, 0, 0.5f)),
            new Sprite(new Vector2(32, 24), new Vector2(8, 8), 0f, SpriteShape.Rectangle, new Vector4(0, 0, 0.5f, 0.5f)));

        // Blue over red, both at 50 %: blue 0.5, red 0.25, alpha 0.75.
        Pixel pixel = image[32, 24];
        ((int)pixel.Blue).Should().BeInRange(127, 128);
        ((int)pixel.Red).Should().BeInRange(63, 64);
        ((int)pixel.Alpha).Should().BeInRange(191, 192);
    }

    [Fact]
    public void Sprites_AreDrawnRelativeToTheMonitor()
    {
        var frame = new RenderFrame();
        frame.Add(new Sprite(new Vector2(-1000 + 10, 500 + 10), new Vector2(2, 2), 0f, SpriteShape.Rectangle, Vector4.One));
        using var target = new OffscreenTarget(_device, s_size);

        _painter.Paint(target.View, s_size, new OverlayFrame(OverlayContent.None, 1f, PixelRect.FromSize(-1000, 500, 64, 48), Scene: frame));

        var image = new RenderedImage(target.ReadPixels(), s_size);
        image[10, 10].Should().Be(new Pixel(255, 255, 255, 255));
        _painter.LastSpriteCount.Should().Be(1);
    }

    [Fact]
    public void SpritesOutsideTheMonitor_AreCulled()
    {
        var frame = new RenderFrame();
        frame.Add(new Sprite(new Vector2(500, 500), new Vector2(2, 2), 0f, SpriteShape.Disc, Vector4.One));
        frame.Add(new Sprite(new Vector2(10, 10), new Vector2(2, 2), 0f, SpriteShape.Disc, Vector4.One));
        using var target = new OffscreenTarget(_device, s_size);

        _painter.Paint(target.View, s_size, new OverlayFrame(OverlayContent.None, 1f, PixelRect.FromSize(0, 0, 64, 48), Scene: frame));

        _painter.LastSpriteCount.Should().Be(1);
    }

    public void Dispose()
    {
        _painter.Dispose();
        _device.Dispose();
    }

    private RenderedImage Render(string previewName, params Sprite[] sprites)
    {
        var frame = new RenderFrame();
        foreach (Sprite sprite in sprites)
        {
            frame.Add(sprite);
        }

        using var target = new OffscreenTarget(_device, s_size);
        _painter.Paint(target.View, s_size, new OverlayFrame(OverlayContent.None, 1f, PixelRect.FromSize(0, 0, 64, 48), Scene: frame));

        var image = new RenderedImage(target.ReadPixels(), s_size);
        image.SavePreview(previewName);
        return image;
    }
}
