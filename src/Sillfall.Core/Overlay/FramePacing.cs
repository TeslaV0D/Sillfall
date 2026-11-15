namespace Sillfall.Core.Overlay;

/// <summary>
/// When the render loop starts its next frame (PLAN.md, section 3; finding F-06). The cap comes from
/// <see cref="Settings.RuntimePolicy"/>; this only turns it into waits. The swap chain then waits for the vertical
/// blank, so the frame shows on the next one.
/// <para>
/// With a cap, frames start on a fixed schedule (one period after the previous <em>scheduled</em> start, not after
/// the actual one), so timer jitter and the vsync wait do not add up: the average rate is the cap. A frame may start
/// up to <see cref="EarlyStartSeconds"/> before its slot, which absorbs the timer's jitter. A loop that fell behind by
/// more than a period starts a new schedule instead of rushing to catch up. Without a cap the swap chain paces the
/// loop at the display's rate; <see cref="UncappedMaximumRate"/> only guards against a busy loop.
/// </para>
/// Not thread-safe; no allocation.
/// </summary>
public sealed class FramePacing
{
    /// <summary>Upper frame rate without a cap.</summary>
    public const double UncappedMaximumRate = 240;

    /// <summary>How much earlier than its slot a capped frame may start.</summary>
    public const double EarlyStartSeconds = 0.001;

    private double _lastStart = double.NegativeInfinity;
    private double _nextSlot = double.NegativeInfinity;
    private int _cap = -1;

    /// <summary>Seconds to wait before the next frame may start at time <paramref name="now"/>; 0 when it may start now.</summary>
    /// <param name="now">Current time in seconds (any monotonic clock).</param>
    /// <param name="frameRateCap">Frame rate cap, 0 for the display's rate.</param>
    public double WaitBeforeNextFrame(double now, int frameRateCap)
    {
        if (frameRateCap != _cap)
        {
            return 0;
        }

        double wait = frameRateCap > 0
            ? _nextSlot - EarlyStartSeconds - now
            : _lastStart + (1.0 / UncappedMaximumRate) - now;
        return Math.Max(0, wait);
    }

    /// <summary>Records that a frame started at <paramref name="now"/>.</summary>
    public void FrameStarted(double now, int frameRateCap)
    {
        _lastStart = now;
        if (frameRateCap <= 0)
        {
            _cap = frameRateCap;
            return;
        }

        double period = 1.0 / frameRateCap;
        _nextSlot = frameRateCap == _cap && now - _nextSlot < period ? _nextSlot + period : now + period;
        _cap = frameRateCap;
    }

    /// <summary>Forgets the schedule, for a loop that starts again after a pause.</summary>
    public void Reset()
    {
        _lastStart = double.NegativeInfinity;
        _nextSlot = double.NegativeInfinity;
        _cap = -1;
    }
}
