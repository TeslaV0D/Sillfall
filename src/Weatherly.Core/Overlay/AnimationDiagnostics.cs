using System.Globalization;
using Weatherly.Core.Diagnostics;

namespace Weatherly.Core.Overlay;

/// <summary>Checks of the render loop: it runs smoothly while an effect is active, allocates nothing, and stops when idle.</summary>
public static class AnimationDiagnostics
{
    /// <summary>
    /// Lowest acceptable frame rate in the self-check. Deliberately modest: CI runners render with the
    /// software adapter. Real hardware is measured against the budgets in PLAN.md, section 9.
    /// </summary>
    public const double MinimumFramesPerSecond = 20;

    /// <summary>Fewer measured frames than this are not enough to call the loop allocation-free.</summary>
    public const long MinimumMeasuredFrames = 30;

    /// <summary>Whether the loop rendered steadily between two measurements <paramref name="seconds"/> apart.</summary>
    public static DiagnosticResult EvaluateAnimation(OverlayStatus before, OverlayStatus after, double seconds)
    {
        ArgumentNullException.ThrowIfNull(before);
        ArgumentNullException.ThrowIfNull(after);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(seconds);

        double fps = (after.AnimationFrames - before.AnimationFrames) / seconds;
        bool passed = after.IsAnimating && fps >= MinimumFramesPerSecond && (after.Sprites > 0 || after.HasAtmosphere);
        return new DiagnosticResult(
            "Animation",
            passed,
            string.Create(
                CultureInfo.InvariantCulture,
                $"{fps:0} fps, {after.FrameMilliseconds:0.0} ms per frame, simulation {after.SimulationMilliseconds:0.00} ms, {after.Sprites} sprite(s){(after.HasAtmosphere ? ", fog/sun layer" : string.Empty)}"));
    }

    /// <summary>Whether the render loop allocated nothing after its warm-up (PLAN.md, section 9: 0 B in the frame loop).</summary>
    public static DiagnosticResult EvaluateLoopAllocations(OverlayStatus status)
    {
        ArgumentNullException.ThrowIfNull(status);

        if (status.MeasuredLoopFrames < MinimumMeasuredFrames)
        {
            return new DiagnosticResult(
                "Loop allocations",
                false,
                string.Create(CultureInfo.InvariantCulture, $"Only {status.MeasuredLoopFrames} frame(s) measured, at least {MinimumMeasuredFrames} needed"));
        }

        return new DiagnosticResult(
            "Loop allocations",
            status.LoopAllocatedBytes == 0,
            string.Create(CultureInfo.InvariantCulture, $"{status.LoopAllocatedBytes} byte(s) in {status.MeasuredLoopFrames} measured frame(s)"));
    }

    /// <summary>Whether the loop stopped after the last effect finished.</summary>
    public static DiagnosticResult EvaluateIdleStop(OverlayStatus status, TimeSpan waited)
    {
        ArgumentNullException.ThrowIfNull(status);

        return new DiagnosticResult(
            "Idle stop",
            !status.IsAnimating,
            status.IsAnimating
                ? string.Create(CultureInfo.InvariantCulture, $"Still rendering {waited.TotalSeconds:0.0} s after the effect was switched off")
                : string.Create(CultureInfo.InvariantCulture, $"Render loop stopped {waited.TotalSeconds:0.0} s after the effect was switched off"));
    }
}
