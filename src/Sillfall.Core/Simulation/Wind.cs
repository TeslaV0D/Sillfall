using System.Numerics;

namespace Sillfall.Core.Simulation;

/// <summary>
/// Global wind that every effect reads (PLAN.md, section 4): a base vector plus gusts from smooth noise
/// over time. Velocities are in pixels per second at 100 % scaling.
/// </summary>
public sealed class Wind
{
    /// <summary>How quickly gusts change: gust periods of a few seconds.</summary>
    private const float GustFrequency = 0.35f;

    private readonly uint _seed;
    private float _time;

    public Wind(uint seed = 0)
    {
        _seed = seed;
    }

    /// <summary>Steady part of the wind. Positive x blows to the right.</summary>
    public Vector2 Base { get; set; }

    /// <summary>Strength of the gusts relative to the base wind speed, 0 for none.</summary>
    public float Gustiness { get; set; } = 0.5f;

    /// <summary>Gust factor right now, about -1 to 1.</summary>
    public float Gust { get; private set; }

    /// <summary>Wind right now: base wind scaled up and down by the gusts.</summary>
    public Vector2 Current => Base * (1f + (Gustiness * Gust));

    public void Step(float dt)
    {
        _time += dt;
        Gust = Noise1D.Fractal(_time * GustFrequency, 3, _seed);
    }
}
