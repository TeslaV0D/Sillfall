using System.Numerics;

namespace Sillfall.Core.Simulation;

/// <summary>
/// Turns cursor positions sampled once per frame into a smoothed velocity (PLAN.md, section 4.3: the cursor
/// gust). A single jittery sample does not make a gust, a quick flick of the mouse does.
/// </summary>
public sealed class CursorMotion
{
    /// <summary>Time constant of the smoothing in seconds.</summary>
    public const double SmoothingSeconds = 0.05;

    /// <summary>A gap this long (the render loop paused) restarts from rest instead of producing a jump.</summary>
    public const double MaxGapSeconds = 0.25;

    /// <summary>Faster than any hand: the cursor was placed, not moved (a script, a second monitor, a scene).</summary>
    public const float TeleportSpeed = 20_000f;

    private bool _hasSample;
    private double _time;

    public Vector2 Position { get; private set; }

    /// <summary>Smoothed velocity in pixels per second.</summary>
    public Vector2 Velocity { get; private set; }

    /// <summary>Adds a sample taken at <paramref name="time"/> seconds.</summary>
    public void Update(Vector2 position, double time)
    {
        double dt = time - _time;
        if (!_hasSample || dt <= 0 || dt > MaxGapSeconds)
        {
            _hasSample = true;
            _time = time;
            Position = position;
            Velocity = Vector2.Zero;
            return;
        }

        Vector2 instant = (position - Position) / (float)dt;
        if (instant.Length() > TeleportSpeed)
        {
            _time = time;
            Position = position;
            Velocity = Vector2.Zero;
            return;
        }

        float blend = (float)(1 - Math.Exp(-dt / SmoothingSeconds));
        Velocity += (instant - Velocity) * blend;
        Position = position;
        _time = time;
    }
}
