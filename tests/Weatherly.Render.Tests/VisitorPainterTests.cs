using Vortice.Direct3D11;
using Vortice.Mathematics;
using Weatherly.Core.Geometry;
using Weatherly.Core.Overlay;
using Weatherly.Core.Rendering;
using Weatherly.Core.Visitors;

namespace Weatherly.Render.Tests;

/// <summary>The visitors drawn with Direct2D's SVG renderer on WARP. The sheet goes to the previews for a look.</summary>
public sealed class VisitorPainterTests : IDisposable
{
    private const int Cell = 96;
    private const float Height = 64f;

    private static readonly VisitorProp[] s_props =
        [VisitorProp.None, VisitorProp.Umbrella, VisitorProp.Snowball, VisitorProp.Sunglasses, VisitorProp.Lantern, VisitorProp.Leaf];

    private readonly ID3D11Device _device = GraphicsDeviceFactory.Create(GraphicsDriver.Warp);

    [Fact]
    public void Sheet_EveryVisitorInEveryPose_IsDrawn()
    {
        // Rows: the eight visitors. Columns: calm, walking left, blinking, happy, sulky, then each prop.
        int columns = 5 + s_props.Length - 1;
        var size = new PixelSize(Cell * columns, Cell * 8);
        var poses = new List<VisitorPose>();
        for (int row = 0; row < 8; row++)
        {
            for (int column = 0; column < columns; column++)
            {
                poses.Add(Pose((VisitorKind)row, column, row));
            }
        }

        using var target = new OffscreenTarget(_device, size);
        _device.ImmediateContext.ClearRenderTargetView(target.View, new Color4(0, 0, 0, 0));
        using (var canvas = new Direct2DCanvas(_device))
        using (var painter = new VisitorPainter(canvas.Context))
        {
            canvas.Begin(target.Texture);
            painter.Draw(poses.ToArray(), PixelRect.FromSize(0, 0, size.Width, size.Height), 1f);
            canvas.End();
        }

        var image = new RenderedImage(target.ReadPixels(), size);
        image.SavePreview("visitors-sheet");

        foreach (VisitorPose pose in poses)
        {
            int column = (int)(pose.X / Cell);
            int row = (int)pose.Kind;
            int opaque = 0;
            for (int y = row * Cell; y < (row + 1) * Cell; y += 2)
            {
                for (int x = column * Cell; x < (column + 1) * Cell; x += 2)
                {
                    if (image[x, y].Alpha > 200)
                    {
                        opaque++;
                    }
                }
            }

            opaque.Should().BeGreaterThan(80, $"{pose.Kind} in column {column} is drawn");
        }

        image[2, 2].Should().Be(Pixel.Transparent, "nothing is drawn outside the visitors");
    }

    [Fact]
    public void Visitor_FacingLeftAndRight_IsMirrored()
    {
        var size = new PixelSize(Cell, Cell);
        Pixel[] left = Draw(size, Pose(VisitorKind.Bristle, column: 0, row: 0) with { X = Cell / 2f, Y = Cell - 16, Facing = -1f });
        Pixel[] right = Draw(size, Pose(VisitorKind.Bristle, column: 0, row: 0) with { X = Cell / 2f, Y = Cell - 16, Facing = 1f });

        // Bristle's nose points right in the drawing: the dark nose is on the right half when facing right.
        DarkPixels(right, size, leftHalf: false).Should().BeGreaterThan(DarkPixels(right, size, leftHalf: true));
        DarkPixels(left, size, leftHalf: true).Should().BeGreaterThan(DarkPixels(left, size, leftHalf: false));
    }

    [Fact]
    public void Visitor_HalfFaded_IsHalfTransparent()
    {
        var size = new PixelSize(Cell, Cell);
        VisitorPose pose = Pose(VisitorKind.Tuck, column: 0, row: 0) with { X = Cell / 2f, Y = Cell - 16 };

        Pixel solid = Draw(size, pose)[(40 * Cell) + 48];
        Pixel faded = Draw(size, pose with { Alpha = 0.5f })[(40 * Cell) + 48];

        solid.Alpha.Should().Be(255);
        ((int)faded.Alpha).Should().BeInRange(120, 136);
    }

    [Fact]
    public void Part_SplitsTheDrawingIntoBodyEyesAndFeet()
    {
        string svg = VisitorArt.LoadSvg("tuck");

        string body = VisitorArt.Part(svg, null)!;
        string eyes = VisitorArt.Part(svg, "eyes")!;

        body.Should().NotContain("id=\"eyes\"").And.NotContain("foot-left").And.Contain("#E05A47", "the scarf is part of the body");
        eyes.Should().Contain("id=\"eyes\"").And.NotContain("#E05A47");
        VisitorArt.Part(VisitorArt.LoadSvg("pip"), "foot-left").Should().BeNull("Pip has no feet");
    }

    [Fact]
    public void Art_HasADrawingForEveryVisitorAndProp()
    {
        foreach (VisitorKind kind in Enum.GetValues<VisitorKind>())
        {
            VisitorArt.LoadSvg(kind.ToString().ToLowerInvariant()).Should().Contain("<svg");
            VisitorArt.Get(kind).Kind.Should().Be(kind);
        }

        foreach (VisitorProp prop in s_props.Skip(1).Where(prop => prop != VisitorProp.Sunglasses))
        {
            VisitorArt.LoadSvg(prop.ToString().ToLowerInvariant()).Should().Contain("<svg");
        }
    }

    public void Dispose() => _device.Dispose();

    private static VisitorPose Pose(VisitorKind kind, int column, int row)
    {
        var pose = new VisitorPose
        {
            Id = (row * 100) + column,
            Kind = kind,
            X = (column * Cell) + (Cell / 2f),
            Y = ((row + 1) * Cell) - 12,
            Height = Height,
            Facing = 1f,
            Stretch = 1f,
            Alpha = 1f,
            Scale = 1f,
        };

        return column switch
        {
            0 => pose,
            1 => pose with { Facing = -1f, Walking = true, Step = 0.25f, Bob = 2f },
            2 => pose with { Blink = 1f },
            3 => pose with { Expression = VisitorExpression.Happy },
            4 => pose with { Expression = VisitorExpression.Sulky },
            _ => pose with { Prop = s_props[column - 4], PropAmount = 1f },
        };
    }

    private Pixel[] Draw(PixelSize size, VisitorPose pose)
    {
        using var target = new OffscreenTarget(_device, size);
        using var painter = new OverlayPainter(_device);
        var frame = new RenderFrame();
        frame.AddVisitor(pose);
        painter.Paint(target.View, size, new OverlayFrame(OverlayContent.None, 1f, PixelRect.FromSize(0, 0, size.Width, size.Height), Scene: frame));
        var image = new RenderedImage(target.ReadPixels(), size);
        return [.. image.Pixels()];
    }

    private static int DarkPixels(Pixel[] pixels, PixelSize size, bool leftHalf)
    {
        int count = 0;
        for (int y = 0; y < size.Height; y++)
        {
            for (int x = leftHalf ? 0 : size.Width / 2; x < (leftHalf ? size.Width / 2 : size.Width); x++)
            {
                Pixel pixel = pixels[(y * size.Width) + x];
                if (pixel.Alpha > 200 && pixel.Red < 70 && pixel.Green < 70 && pixel.Blue < 80)
                {
                    count++;
                }
            }
        }

        return count;
    }
}
