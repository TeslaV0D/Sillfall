namespace Weatherly.Core.Effects.Snow;

/// <summary>
/// Snow lying on one surface, as a height field: a row of bins across the surface width (PLAN.md, section 4.2).
/// Stored relative to the surface origin, so it moves with the window.
/// <para>
/// Heights are in pixels. Volumes are in square pixels (height times bin width), so snow keeps its amount
/// when bins are resampled. Every operation keeps the books: the volume on the field plus everything that
/// was shed (<see cref="LeftShed"/>, <see cref="RightShed"/>, <see cref="TakeAll"/>, <see cref="Shed"/>) or
/// melted (<see cref="Melt"/>) always equals the volume added.
/// </para>
/// </summary>
public sealed class SnowField
{
    /// <summary>How many bins to each side a full bin spills into before snow falls over the end.</summary>
    public const int SpillDistance = 8;

    private float[] _height;

    /// <param name="width">Width of the surface in pixels.</param>
    /// <param name="binWidth">Width of one bin in pixels.</param>
    /// <param name="maxHeight">Highest pile in pixels. More falls over the ends.</param>
    /// <param name="maxSlope">
    /// Angle of repose as the largest height difference between neighbouring bins, in pixels. Steeper piles
    /// slide until they are not.
    /// </param>
    public SnowField(int width, int binWidth, float maxHeight, float maxSlope)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(binWidth);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(maxHeight);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(maxSlope);
        BinWidth = binWidth;
        MaxHeight = maxHeight;
        MaxSlope = maxSlope;
        Width = Math.Max(width, 1);
        _height = new float[BinCount(Width, binWidth)];
    }

    public int BinWidth { get; }

    public float MaxHeight { get; }

    public float MaxSlope { get; }

    /// <summary>Width of the surface in pixels.</summary>
    public int Width { get; private set; }

    public int Count => _height.Length;

    /// <summary>Height of every bin in pixels.</summary>
    public ReadOnlySpan<float> Heights => _height;

    /// <summary>Snow that slid over the left end and waits to fall as clumps, in square pixels.</summary>
    public float LeftShed { get; set; }

    /// <summary>Snow that slid over the right end and waits to fall as clumps, in square pixels.</summary>
    public float RightShed { get; set; }

    /// <summary>All snow lying on the field in square pixels, not counting what was shed.</summary>
    public float Volume
    {
        get
        {
            float sum = 0f;
            foreach (float height in _height)
            {
                sum += height;
            }

            return sum * BinWidth;
        }
    }

    /// <summary>Height of the highest bin in pixels.</summary>
    public float Peak
    {
        get
        {
            float peak = 0f;
            foreach (float height in _height)
            {
                peak = MathF.Max(peak, height);
            }

            return peak;
        }
    }

    /// <summary>Whether any snow lies on the field or waits to fall off it.</summary>
    public bool IsEmpty => LeftShed <= 0f && RightShed <= 0f && Peak <= 0f;

    public int BinAt(float localX) => Math.Clamp((int)MathF.Floor(localX / BinWidth), 0, _height.Length - 1);

    /// <summary>
    /// Adds snow at a position relative to the surface origin. A full bin spills into its neighbours; only what fits
    /// nowhere near is shed at the nearer end.
    /// </summary>
    public void Add(float localX, float volume)
    {
        if (volume <= 0f)
        {
            return;
        }

        int bin = BinAt(localX);
        float height = _height[bin] + (volume / BinWidth);
        if (height <= MaxHeight)
        {
            _height[bin] = height;
            return;
        }

        _height[bin] = MaxHeight;
        float excess = height - MaxHeight;
        for (int distance = 1; distance <= SpillDistance && excess > 0f; distance++)
        {
            excess = Fill(bin + distance, excess);
            excess = Fill(bin - distance, excess);
        }

        if (excess > 0f)
        {
            ShedToNearerEnd(bin, excess * BinWidth);
        }
    }

    /// <summary>
    /// Lets steep piles slide: wherever neighbours differ by more than <see cref="MaxSlope"/>, snow moves
    /// downhill until they do not. At both ends the surface stops: snow higher than the slope allows there
    /// falls off and is added to <see cref="LeftShed"/> or <see cref="RightShed"/>.
    /// </summary>
    /// <param name="passes">Sweeps over the field, alternating direction. A few per step are plenty.</param>
    public void Relax(int passes = 2)
    {
        float[] h = _height;
        int last = h.Length - 1;
        for (int pass = 0; pass < passes; pass++)
        {
            if ((pass & 1) == 0)
            {
                for (int i = 0; i < last; i++)
                {
                    Settle(h, i, i + 1);
                }
            }
            else
            {
                for (int i = last; i > 0; i--)
                {
                    Settle(h, i, i - 1);
                }
            }

            // The ends: outside the surface there is nothing to lean on.
            if (h[0] > MaxSlope)
            {
                LeftShed += (h[0] - MaxSlope) * BinWidth;
                h[0] = MaxSlope;
            }

            if (h[last] > MaxSlope)
            {
                RightShed += (h[last] - MaxSlope) * BinWidth;
                h[last] = MaxSlope;
            }
        }
    }

    /// <summary>
    /// Melts the snow: every bin loses <paramref name="rate"/> of its height per second plus
    /// <paramref name="linear"/> pixels per second, so even thin layers vanish. Returns the melted volume.
    /// </summary>
    public float Melt(float dt, float rate, float linear)
    {
        float factor = MathF.Exp(-rate * dt);
        float loss = linear * dt;
        float melted = 0f;
        for (int i = 0; i < _height.Length; i++)
        {
            float before = _height[i];
            if (before <= 0f)
            {
                continue;
            }

            float after = MathF.Max(0f, (before * factor) - loss);
            _height[i] = after;
            melted += before - after;
        }

        return melted * BinWidth;
    }

    /// <summary>
    /// Takes <paramref name="fraction"/> of the snow off <paramref name="bins"/> bins starting at <paramref name="first"/>
    /// and returns its volume: the window lost it, it falls as a clump.
    /// </summary>
    public float Shed(int first, int bins, float fraction)
    {
        fraction = Math.Clamp(fraction, 0f, 1f);
        int end = Math.Min(first + bins, _height.Length);
        float taken = 0f;
        for (int i = Math.Max(first, 0); i < end; i++)
        {
            float part = _height[i] * fraction;
            _height[i] -= part;
            taken += part;
        }

        return taken * BinWidth;
    }

    /// <summary>Removes everything, including what waits at the ends, and returns its volume.</summary>
    public float TakeAll()
    {
        float volume = Volume + LeftShed + RightShed;
        Array.Clear(_height);
        LeftShed = 0f;
        RightShed = 0f;
        return volume;
    }

    /// <summary>
    /// Covers the field with a layer of <paramref name="height"/> pixels that already rests at the angle of repose
    /// towards both ends. For previews and the sandbox.
    /// </summary>
    public void Fill(float height)
    {
        height = Math.Clamp(height, 0f, MaxHeight);
        for (int i = 0; i < _height.Length; i++)
        {
            int fromEnd = Math.Min(i, _height.Length - 1 - i) + 1;
            _height[i] = MathF.Max(_height[i], MathF.Min(height, fromEnd * MaxSlope));
        }
    }

    /// <summary>Adapts the field to a new surface width. The snow is resampled, its volume stays the same.</summary>
    public void Resize(int width)
    {
        width = Math.Max(width, 1);
        if (width == Width)
        {
            return;
        }

        float volume = Volume;
        float[] old = _height;
        var resized = new float[BinCount(width, BinWidth)];

        // Sample the old profile at the same relative position.
        for (int i = 0; i < resized.Length; i++)
        {
            float source = ((i + 0.5f) / resized.Length * old.Length) - 0.5f;
            int left = Math.Clamp((int)MathF.Floor(source), 0, old.Length - 1);
            int right = Math.Min(left + 1, old.Length - 1);
            float t = Math.Clamp(source - left, 0f, 1f);
            resized[i] = old[left] + ((old[right] - old[left]) * t);
        }

        _height = resized;
        Width = width;

        // Rescale so the volume is exactly what it was, then cap at the maximum height.
        float sampled = Volume;
        if (sampled > 0f)
        {
            float scale = volume / sampled;
            for (int i = 0; i < _height.Length; i++)
            {
                _height[i] *= scale;
                if (_height[i] > MaxHeight)
                {
                    ShedToNearerEnd(i, (_height[i] - MaxHeight) * BinWidth);
                    _height[i] = MaxHeight;
                }
            }
        }
        else if (volume > 0f)
        {
            // Nothing to sample (the old field was too coarse): spread it evenly.
            float each = volume / BinWidth / _height.Length;
            Array.Fill(_height, Math.Min(each, MaxHeight));
            float rest = volume - Volume;
            if (rest > 0f)
            {
                LeftShed += rest / 2f;
                RightShed += rest / 2f;
            }
        }
    }

    private static int BinCount(int width, int binWidth) => Math.Max(1, (width + binWidth - 1) / binWidth);

    /// <summary>Puts as much of <paramref name="height"/> as fits into bin <paramref name="bin"/>, returns the rest.</summary>
    private float Fill(int bin, float height)
    {
        if (bin < 0 || bin >= _height.Length)
        {
            return height;
        }

        float taken = MathF.Min(MaxHeight - _height[bin], height);
        if (taken <= 0f)
        {
            return height;
        }

        _height[bin] += taken;
        return height - taken;
    }

    private void Settle(float[] h, int from, int to)
    {
        float difference = h[from] - h[to];
        if (difference > MaxSlope)
        {
            float moved = (difference - MaxSlope) / 2f;
            h[from] -= moved;
            h[to] += moved;
        }
    }

    private void ShedToNearerEnd(int bin, float volume)
    {
        if (bin < _height.Length / 2)
        {
            LeftShed += volume;
        }
        else
        {
            RightShed += volume;
        }
    }
}
