using System.Runtime.InteropServices;

namespace Weatherly.Core.Surfaces;

/// <summary>
/// All visible surface pieces of the desktop. Reusable: <see cref="SurfaceExtractor"/> refills one
/// instance on every update, so tracking a window drag does not allocate.
/// </summary>
public sealed class SurfaceSet
{
    private readonly List<SurfaceSegment> _segments = new(256);

    public int Count => _segments.Count;

    /// <summary>Segments in a stable order: per window from top to bottom of the z-order, then the floors.</summary>
    public ReadOnlySpan<SurfaceSegment> Segments => CollectionsMarshal.AsSpan(_segments);

    public void Clear() => _segments.Clear();

    public void Add(in SurfaceSegment segment) => _segments.Add(segment);

    public bool HasSameSegments(SurfaceSet other)
    {
        ArgumentNullException.ThrowIfNull(other);
        return Segments.SequenceEqual(other.Segments);
    }

    public void CopyFrom(SurfaceSet source)
    {
        ArgumentNullException.ThrowIfNull(source);
        _segments.Clear();
        _segments.AddRange(source.Segments);
    }
}
