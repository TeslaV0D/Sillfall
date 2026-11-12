using Sillfall.Core.Geometry;

namespace Sillfall.Core.Surfaces;

/// <summary>Where a surface sits in pixels, for drawing it and for placing things on it.</summary>
public static class SurfaceGeometry
{
    /// <summary>
    /// The band of <paramref name="thickness"/> pixels right outside the window (or above the floor)
    /// that a segment stands for. Top edges and floors: the rows directly above <see cref="SurfaceSegment.Position"/>.
    /// Left sides: the columns ending at <see cref="SurfaceSegment.Position"/>. Right sides: the columns
    /// starting at it. The band never overlaps the window itself.
    /// </summary>
    public static PixelRect Band(in SurfaceSegment segment, int thickness)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(thickness, 1);

        return segment.Key.Edge switch
        {
            SurfaceEdge.Top or SurfaceEdge.Floor => new PixelRect(segment.Start, segment.Position - thickness, segment.End, segment.Position),
            SurfaceEdge.Left => new PixelRect(segment.Position - thickness + 1, segment.Start, segment.Position + 1, segment.End),
            SurfaceEdge.Right => new PixelRect(segment.Position, segment.Start, segment.Position + thickness, segment.End),
            _ => throw new ArgumentOutOfRangeException(nameof(segment), segment.Key.Edge, null),
        };
    }

    /// <summary>Moves <paramref name="rect"/> from desktop coordinates into the coordinates of a monitor at <paramref name="monitor"/>.</summary>
    public static PixelRect ToMonitor(PixelRect rect, PixelRect monitor) =>
        new(rect.Left - monitor.Left, rect.Top - monitor.Top, rect.Right - monitor.Left, rect.Bottom - monitor.Top);
}
