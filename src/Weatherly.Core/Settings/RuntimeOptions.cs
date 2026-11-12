using Weatherly.Core.Overlay;

namespace Weatherly.Core.Settings;

/// <summary>
/// The overlay options the app runs with: the stored settings, the effects' current intensities (fixed or
/// random) and the runtime decision (pause, frame rate, battery factor) together.
/// </summary>
public static class RuntimeOptions
{
    private static readonly DisplayConfig[] s_noDisplays = [];
    private static readonly string[] s_noProcesses = [];

    /// <summary>Options for <paramref name="settings"/>; everything the settings do not cover comes from <paramref name="current"/>.</summary>
    public static OverlayOptions Build(AppSettings settings, OverlayOptions current, ReadOnlySpan<float> intensities, RuntimeDecision decision)
    {
        ArgumentNullException.ThrowIfNull(settings);
        ArgumentNullException.ThrowIfNull(current);
        Span<float> scaled = stackalloc float[6];
        for (int i = 0; i < scaled.Length; i++)
        {
            scaled[i] = (i < intensities.Length ? intensities[i] : 0f) * decision.IntensityScale;
        }

        List<string> hidden = [];
        foreach (AppRule rule in settings.AppRules)
        {
            if (rule.Action == AppRuleAction.HideOverWindows)
            {
                hidden.Add(rule.ProcessName);
            }
        }

        return settings.ToOverlayOptions(current, scaled) with
        {
            Enabled = settings.Enabled && !decision.Paused,
            FrameRateCap = decision.FrameRateCap,
            Displays = [.. settings.Displays],
            HideOverProcesses = hidden,
        };
    }

    /// <summary>Whether two options are the same, comparing the lists by content (records compare them by reference).</summary>
    public static bool Same(OverlayOptions? a, OverlayOptions? b)
    {
        if (a is null || b is null)
        {
            return ReferenceEquals(a, b);
        }

        // The same empty lists on both sides, so the record comparison checks everything else.
        return a with { Displays = s_noDisplays, HideOverProcesses = s_noProcesses } == b with { Displays = s_noDisplays, HideOverProcesses = s_noProcesses }
            && a.Displays.SequenceEqual(b.Displays)
            && a.HideOverProcesses.SequenceEqual(b.HideOverProcesses, StringComparer.OrdinalIgnoreCase);
    }
}
