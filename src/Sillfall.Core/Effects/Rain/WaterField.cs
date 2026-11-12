namespace Sillfall.Core.Effects.Rain;

/// <summary>
/// Water standing on one top edge, as a row of bins across the window width (PLAN.md, section 4.1).
/// Stored relative to the window, so it moves with it. Hits add water, neighbouring bins even out
/// (diffusion), and the water creeps towards both ends of the edge, where it runs off as drips.
/// Volumes are in "bin units": 1 is a bin filled to its maximum film height.
/// </summary>
public sealed class WaterField
{
    /// <summary>Most water a bin holds. More runs off immediately.</summary>
    public const float MaxPerBin = 1f;

    private float[] _water;
    private float[] _scratch;

    public WaterField(int width, int binWidth)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(binWidth);
        BinWidth = binWidth;
        Width = Math.Max(width, 1);
        _water = new float[BinCount(Width, binWidth)];
        _scratch = new float[_water.Length];
    }

    /// <summary>Width of a bin in pixels.</summary>
    public int BinWidth { get; }

    /// <summary>Width of the edge in pixels.</summary>
    public int Width { get; private set; }

    public int Count => _water.Length;

    public ReadOnlySpan<float> Bins => _water;

    /// <summary>Water that reached the left end and waits to drip off, in bin units.</summary>
    public float LeftRunoff { get; set; }

    /// <summary>Water that reached the right end and waits to drip off, in bin units.</summary>
    public float RightRunoff { get; set; }

    /// <summary>All water on the edge, not counting runoff.</summary>
    public float Total
    {
        get
        {
            float sum = 0f;
            foreach (float water in _water)
            {
                sum += water;
            }

            return sum;
        }
    }

    public int BinAt(float localX) => Math.Clamp((int)(localX / BinWidth), 0, _water.Length - 1);

    /// <summary>Adds water at a position relative to the window's left edge.</summary>
    public void Add(float localX, float amount)
    {
        if (amount <= 0f)
        {
            return;
        }

        int bin = BinAt(localX);
        float filled = _water[bin] + amount;
        if (filled > MaxPerBin)
        {
            // Overflow runs towards the nearer end.
            float overflow = filled - MaxPerBin;
            filled = MaxPerBin;
            if (bin < _water.Length / 2)
            {
                LeftRunoff += overflow;
            }
            else
            {
                RightRunoff += overflow;
            }
        }

        _water[bin] = filled;
    }

    /// <summary>
    /// One simulation step: diffusion between neighbours, a slow creep outwards (the left half to the left
    /// end, the right half to the right end), evaporation. Water leaving the ends becomes runoff.
    /// Mass is conserved apart from the evaporated part.
    /// </summary>
    /// <param name="dt">Step length in seconds.</param>
    /// <param name="diffusion">Diffusion rate per second, 0 to about 20.</param>
    /// <param name="creep">Fraction of a bin's water that moves one bin outwards per second.</param>
    /// <param name="evaporation">Fraction of the water that evaporates per second.</param>
    public void Step(float dt, float diffusion = 6f, float creep = 0.8f, float evaporation = 0.02f)
    {
        int n = _water.Length;
        if (n == 0)
        {
            return;
        }

        // Explicit diffusion, closed ends. Clamped so it stays stable for any dt.
        float k = MathF.Min(diffusion * dt, 0.45f);
        for (int i = 0; i < n; i++)
        {
            float left = i > 0 ? _water[i - 1] : _water[i];
            float right = i < n - 1 ? _water[i + 1] : _water[i];
            _scratch[i] = _water[i] + (k * (left + right - (2f * _water[i])));
        }

        (_water, _scratch) = (_scratch, _water);

        // Creep outwards: each bin hands a share of its water to its outer neighbour, or off the edge.
        float share = MathF.Min(creep * dt, 0.5f);
        int middle = n / 2;
        for (int i = 0; i < middle; i++)
        {
            float moved = _water[i] * share;
            _water[i] -= moved;
            if (i == 0)
            {
                LeftRunoff += moved;
            }
            else
            {
                _water[i - 1] += moved;
            }
        }

        for (int i = n - 1; i >= middle; i--)
        {
            float moved = _water[i] * share;
            _water[i] -= moved;
            if (i == n - 1)
            {
                RightRunoff += moved;
            }
            else
            {
                _water[i + 1] += moved;
            }
        }

        float keep = 1f - MathF.Min(evaporation * dt, 1f);
        for (int i = 0; i < n; i++)
        {
            _water[i] *= keep;
        }
    }

    /// <summary>
    /// Adapts the field to a new window width. Water is resampled proportionally, so the total stays the same.
    /// </summary>
    public void Resize(int width)
    {
        width = Math.Max(width, 1);
        int count = BinCount(width, BinWidth);
        if (count == _water.Length)
        {
            Width = width;
            return;
        }

        float total = Total;
        float[] resized = new float[count];
        for (int i = 0; i < count; i++)
        {
            // Nearest source bin, then rescale to keep the total.
            int source = Math.Min((int)((i + 0.5f) * _water.Length / count), _water.Length - 1);
            resized[i] = _water[source];
        }

        float resizedTotal = 0f;
        foreach (float water in resized)
        {
            resizedTotal += water;
        }

        if (resizedTotal > 0f)
        {
            float scale = total / resizedTotal;
            for (int i = 0; i < count; i++)
            {
                resized[i] *= scale;
            }
        }

        _water = resized;
        _scratch = new float[count];
        Width = width;
    }

    /// <summary>Removes all water and returns how much there was (for slide-off effects).</summary>
    public float Drain()
    {
        float total = Total + LeftRunoff + RightRunoff;
        Array.Clear(_water);
        LeftRunoff = 0f;
        RightRunoff = 0f;
        return total;
    }

    private static int BinCount(int width, int binWidth) => Math.Max(1, (width + binWidth - 1) / binWidth);
}
