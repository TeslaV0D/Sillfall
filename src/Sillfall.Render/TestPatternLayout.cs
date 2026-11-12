using Sillfall.Core.Geometry;

namespace Sillfall.Render;

/// <summary>
/// Positions of the calibration pattern in pixels of the render target. The pixel shader receives
/// exactly these values, so tests can check rendered pixels against the same definition.
/// </summary>
public readonly record struct TestPatternLayout
{
    private TestPatternLayout(PixelSize size, int unit)
    {
        Size = size;
        Unit = unit;
    }

    public PixelSize Size { get; }

    /// <summary>Thickness of a thin line: one pixel at 100 % scaling.</summary>
    public int Unit { get; }

    /// <summary>Thickness of the magenta frame along the edges.</summary>
    public int Border => 2 * Unit;

    /// <summary>Edge length of the colored corner markers, measured inside the frame.</summary>
    public int Corner => 24 * Unit;

    /// <summary>Distance between grid lines: 100 device-independent pixels.</summary>
    public int GridSpacing => 100 * Unit;

    /// <summary>Half length of the cross in the center.</summary>
    public int CrossLength => 40 * Unit;

    /// <summary>White with alpha rising from 0 (left) to 1 (right), centered in the upper half.</summary>
    public PixelRect AlphaRamp => Centered(Size.Width / 2, Size.Height / 4, Math.Min(Size.Width / 2, 512 * Unit), 32 * Unit);

    /// <summary>Black at 50 % alpha, centered in the lower half.</summary>
    public PixelRect Shade => Centered(Size.Width / 2, Size.Height * 3 / 4, Math.Min(Size.Width / 4, 256 * Unit), 32 * Unit);

    /// <summary>Creates the layout for a render target of <paramref name="size"/> on a monitor with the given DPI scale.</summary>
    public static TestPatternLayout Create(PixelSize size, float scaleFactor) =>
        new(size, Math.Max(1, (int)MathF.Round(scaleFactor, MidpointRounding.AwayFromZero)));

    private static PixelRect Centered(int centerX, int centerY, int width, int height) =>
        PixelRect.FromSize(centerX - (width / 2), centerY - (height / 2), width, height);
}
