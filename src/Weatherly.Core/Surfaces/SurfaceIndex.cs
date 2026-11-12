using System.Runtime.InteropServices;

namespace Weatherly.Core.Surfaces;

/// <summary>Where a falling particle meets a surface.</summary>
/// <param name="Segment">Index of the segment in the indexed <see cref="SurfaceSet"/>.</param>
/// <param name="Row">Row of the surface: the particle rests at y just above it.</param>
public readonly record struct SurfaceHit(int Segment, int Row);

/// <summary>
/// Speeds up "where does this falling particle land?". Horizontal segments (top edges and floors) are
/// sorted into vertical columns of <see cref="BucketWidth"/> pixels, so a query looks at a handful of
/// segments instead of all of them. Rebuilt whenever the surfaces change, without allocating once warm.
/// </summary>
public sealed class SurfaceIndex
{
    public const int BucketWidth = 64;

    private readonly List<int> _entries = new(512);
    private int[] _bucketStart = new int[64];
    private int[] _bucketCount = new int[64];
    private SurfaceSet? _surfaces;
    private int _left;
    private int _buckets;

    /// <summary>Indexes the horizontal segments of <paramref name="surfaces"/>.</summary>
    public void Build(SurfaceSet surfaces)
    {
        ArgumentNullException.ThrowIfNull(surfaces);
        _surfaces = surfaces;
        _entries.Clear();

        ReadOnlySpan<SurfaceSegment> segments = surfaces.Segments;
        int left = int.MaxValue;
        int right = int.MinValue;
        foreach (SurfaceSegment segment in segments)
        {
            if (segment.IsHorizontal)
            {
                left = Math.Min(left, segment.Start);
                right = Math.Max(right, segment.End);
            }
        }

        if (left >= right)
        {
            _buckets = 0;
            return;
        }

        _left = left;
        _buckets = ((right - left) / BucketWidth) + 1;
        EnsureCapacity(_buckets);
        Array.Clear(_bucketCount, 0, _buckets);

        // Two passes over the segments: count per bucket, then fill a flat array in bucket order.
        for (int i = 0; i < segments.Length; i++)
        {
            if (segments[i].IsHorizontal)
            {
                for (int bucket = BucketOf(segments[i].Start); bucket <= BucketOf(segments[i].End - 1); bucket++)
                {
                    _bucketCount[bucket]++;
                }
            }
        }

        int total = 0;
        for (int bucket = 0; bucket < _buckets; bucket++)
        {
            _bucketStart[bucket] = total;
            total += _bucketCount[bucket];
            _bucketCount[bucket] = 0;
        }

        CollectionsMarshal.SetCount(_entries, total);
        Span<int> entries = CollectionsMarshal.AsSpan(_entries);
        for (int i = 0; i < segments.Length; i++)
        {
            if (segments[i].IsHorizontal)
            {
                for (int bucket = BucketOf(segments[i].Start); bucket <= BucketOf(segments[i].End - 1); bucket++)
                {
                    entries[_bucketStart[bucket] + _bucketCount[bucket]++] = i;
                }
            }
        }
    }

    /// <summary>
    /// Finds the first horizontal surface a particle crosses when it moves down from <paramref name="fromY"/>
    /// to <paramref name="toY"/> at column <paramref name="x"/>. A surface at row r is crossed when
    /// fromY &lt; r &lt;= toY: the particle reaches the line on top of the surface.
    /// </summary>
    public bool TryFindLanding(float x, float fromY, float toY, out SurfaceHit hit)
    {
        hit = default;
        if (_surfaces is null || _buckets == 0 || toY <= fromY)
        {
            return false;
        }

        int column = (int)MathF.Floor(x);
        int bucket = BucketOf(column);
        if (bucket < 0 || bucket >= _buckets)
        {
            return false;
        }

        ReadOnlySpan<SurfaceSegment> segments = _surfaces.Segments;
        ReadOnlySpan<int> entries = CollectionsMarshal.AsSpan(_entries).Slice(_bucketStart[bucket], _bucketCount[bucket]);
        int bestRow = int.MaxValue;
        int best = -1;
        foreach (int index in entries)
        {
            SurfaceSegment segment = segments[index];
            if (column >= segment.Start && column < segment.End
                && segment.Position > fromY && segment.Position <= toY
                && segment.Position < bestRow)
            {
                bestRow = segment.Position;
                best = index;
            }
        }

        if (best < 0)
        {
            return false;
        }

        hit = new SurfaceHit(best, bestRow);
        return true;
    }

    private int BucketOf(int x) => (int)Math.Floor((x - (double)_left) / BucketWidth);

    private void EnsureCapacity(int buckets)
    {
        if (_bucketStart.Length < buckets)
        {
            int size = Math.Max(buckets, _bucketStart.Length * 2);
            _bucketStart = new int[size];
            _bucketCount = new int[size];
        }
    }
}
