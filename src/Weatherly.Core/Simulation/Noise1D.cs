namespace Weatherly.Core.Simulation;

/// <summary>
/// Smooth one-dimensional gradient noise, roughly in [-1, 1]. Continuous and repeatable: the same
/// seed and position always give the same value. Used for wind gusts and per-particle wobble.
/// </summary>
public static class Noise1D
{
    /// <summary>Noise value at <paramref name="x"/>. Integer positions are always 0.</summary>
    public static float Sample(float x, uint seed = 0)
    {
        float cell = MathF.Floor(x);
        float t = x - cell;
        int i = (int)cell;

        float g0 = Gradient(i, seed);
        float g1 = Gradient(i + 1, seed);

        // Quintic fade: continuous first and second derivative, no visible kinks in the gusts.
        float fade = t * t * t * ((t * ((t * 6f) - 15f)) + 10f);
        float a = g0 * t;
        float b = g1 * (t - 1f);

        // Scaled so the typical range is about [-1, 1].
        return 2f * (a + (fade * (b - a)));
    }

    /// <summary>Sum of <paramref name="octaves"/> noise layers, each at double frequency and half amplitude. Normalized to about [-1, 1].</summary>
    public static float Fractal(float x, int octaves, uint seed = 0)
    {
        float sum = 0f;
        float amplitude = 1f;
        float total = 0f;
        float frequency = 1f;
        for (int octave = 0; octave < octaves; octave++)
        {
            sum += amplitude * Sample(x * frequency, seed + (uint)octave);
            total += amplitude;
            amplitude *= 0.5f;
            frequency *= 2f;
        }

        return total > 0f ? sum / total : 0f;
    }

    /// <summary>Pseudo-random gradient in [-1, 1] for an integer lattice point.</summary>
    private static float Gradient(int i, uint seed)
    {
        uint h = (uint)i * 0x9E3779B1u;
        h ^= seed * 0x85EBCA77u;
        h ^= h >> 15;
        h *= 0x2C1B3C6Du;
        h ^= h >> 12;
        h *= 0x297A2D39u;
        h ^= h >> 15;
        return ((h & 0xFFFF) / 32767.5f) - 1f;
    }
}
