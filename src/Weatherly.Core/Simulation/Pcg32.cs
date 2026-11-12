namespace Weatherly.Core.Simulation;

/// <summary>
/// Small, fast, seedable random number generator (PCG-XSH-RR). Same seed, same sequence, on every
/// platform and .NET version, which <see cref="Random"/> does not promise. A struct: no allocation.
/// </summary>
public struct Pcg32
{
    private const ulong Multiplier = 6364136223846793005UL;
    private const ulong Increment = 1442695040888963407UL;

    private ulong _state;

    public Pcg32(ulong seed)
    {
        _state = 0;
        NextUInt();
        _state += seed;
        NextUInt();
    }

    /// <summary>Uniformly distributed 32-bit value.</summary>
    public uint NextUInt()
    {
        ulong old = _state;
        _state = (old * Multiplier) + Increment;
        uint xorShifted = (uint)(((old >> 18) ^ old) >> 27);
        int rotation = (int)(old >> 59);
        return (xorShifted >> rotation) | (xorShifted << ((-rotation) & 31));
    }

    /// <summary>Uniform float in [0, 1).</summary>
    public float NextFloat() => (NextUInt() >> 8) * (1f / 16777216f);

    /// <summary>Uniform float in [<paramref name="min"/>, <paramref name="max"/>).</summary>
    public float NextFloat(float min, float max) => min + ((max - min) * NextFloat());

    /// <summary>Uniform integer in [0, <paramref name="maxExclusive"/>).</summary>
    public int NextInt(int maxExclusive)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(maxExclusive);
        return (int)((NextUInt() * (ulong)maxExclusive) >> 32);
    }
}
