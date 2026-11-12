namespace Sillfall.Core.Geometry;

/// <summary>A size in physical pixels.</summary>
public readonly record struct PixelSize(int Width, int Height)
{
    /// <summary><see langword="true"/> when the size covers no pixels.</summary>
    public bool IsEmpty => Width <= 0 || Height <= 0;

    public override string ToString() => $"{Width}x{Height}";
}
