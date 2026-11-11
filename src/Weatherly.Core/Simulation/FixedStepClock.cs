namespace Weatherly.Core.Simulation;

/// <summary>
/// Turns irregular frame times into a whole number of fixed simulation steps (PLAN.md, section 2:
/// dt = 1/120 s). Leftover time is carried into the next frame, so the simulation runs at the same
/// speed at 30, 60 or 144 fps and stays deterministic.
/// </summary>
public sealed class FixedStepClock
{
    /// <summary>The default step: 120 Hz.</summary>
    public const double DefaultStep = 1.0 / 120.0;

    /// <summary>
    /// After a hitch (breakpoint, sleep, a stalled GPU) the simulation does not try to catch up on all
    /// the missed time, it drops the excess. Otherwise one slow frame would make the next one slower.
    /// </summary>
    public const int DefaultMaxStepsPerFrame = 8;

    private double _accumulator;

    public FixedStepClock(double step = DefaultStep, int maxStepsPerFrame = DefaultMaxStepsPerFrame)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(step);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(maxStepsPerFrame);

        Step = step;
        MaxStepsPerFrame = maxStepsPerFrame;
    }

    /// <summary>Length of one simulation step in seconds.</summary>
    public double Step { get; }

    public int MaxStepsPerFrame { get; }

    /// <summary>Total simulated time in seconds.</summary>
    public double SimulationTime { get; private set; }

    /// <summary>Real time that was dropped because a frame took too long.</summary>
    public double DroppedTime { get; private set; }

    /// <summary>How far the simulation is into the next step, 0 to 1. Rendering can interpolate with it.</summary>
    public float Alpha => (float)(_accumulator / Step);

    /// <summary>Adds the real time since the last frame and returns how many steps to simulate now.</summary>
    public int Advance(double elapsedSeconds)
    {
        if (elapsedSeconds > 0)
        {
            _accumulator += elapsedSeconds;
        }

        int steps = (int)Math.Floor(_accumulator / Step);
        if (steps > MaxStepsPerFrame)
        {
            DroppedTime += (steps - MaxStepsPerFrame) * Step;
            _accumulator -= (steps - MaxStepsPerFrame) * Step;
            steps = MaxStepsPerFrame;
        }

        _accumulator -= steps * Step;

        // Guards against the accumulator drifting below zero through rounding.
        if (_accumulator < 0)
        {
            _accumulator = 0;
        }

        SimulationTime += steps * Step;
        return steps;
    }

    /// <summary>Starts over, for example after the loop was idle: the idle time must not be simulated.</summary>
    public void Reset()
    {
        _accumulator = 0;
    }
}
