namespace Sillfall.Core.Geometry;

/// <summary>
/// A rectangle in physical pixels of the virtual desktop. <see cref="Right"/> and <see cref="Bottom"/>
/// are exclusive, as in Win32. Coordinates can be negative: monitors left of or above the primary
/// monitor have negative positions.
/// </summary>
public readonly record struct PixelRect(int Left, int Top, int Right, int Bottom)
{
    public int Width => Right - Left;

    public int Height => Bottom - Top;

    public PixelSize Size => new(Width, Height);

    /// <summary><see langword="true"/> when the rectangle covers no pixels.</summary>
    public bool IsEmpty => Width <= 0 || Height <= 0;

    public static PixelRect FromSize(int left, int top, int width, int height) =>
        new(left, top, left + width, top + height);

    /// <summary>Whether the pixel at (<paramref name="x"/>, <paramref name="y"/>) lies inside the rectangle.</summary>
    public bool Contains(int x, int y) => x >= Left && x < Right && y >= Top && y < Bottom;

    public override string ToString() => $"({Left}, {Top}) {Width}x{Height}";
}
