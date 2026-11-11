using System.Numerics;
using Vortice.Direct3D11;
using Weatherly.Core.Geometry;
using Weatherly.Core.Overlay;
using Weatherly.Core.Rendering;

namespace Weatherly.Render.Tests;

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
        image[63, 24].Should().Be(Pixel.Transparent);
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
