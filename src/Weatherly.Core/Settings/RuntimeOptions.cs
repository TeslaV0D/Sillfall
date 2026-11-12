using System.Numerics;
using Weatherly.Core.Overlay;
using Weatherly.Core.Weather;

namespace Weatherly.Core.Settings;

/// <summary>
/// The overlay options the app runs with: the stored settings, the effects' current intensities (fixed or
/// random), the weather sync and the runtime decision (pause, frame rate, battery factor) together.
/// </summary>
public static class RuntimeOptions
{
    private static readonly DisplayConfig[] s_noDisplays = [];
    private static readonly string[] s_noProcesses = [];

    /// <summary>
    /// Options for <paramref name="settings"/>; everything the settings do not cover comes from <paramref name="current"/>.
    /// <paramref name="weather"/> is what the weather sync asks for (<see langword="null"/>: sync off or no forecast yet).
    /// </summary>
    public static OverlayOptions Build(
        AppSettings settings, OverlayOptions current, ReadOnlySpan<float> intensities, RuntimeDecision decision, WeatherTargets? weather = null)
    {
        ArgumentNullException.ThrowIfNull(settings);
        ArgumentNullException.ThrowIfNull(current);
        Span<float> scaled = stackalloc float[6];
        for (int i = 0; i < scaled.Length; i++)
        {
            scaled[i] = i < intensities.Length ? intensities[i] : 0f;
        }

        float windSpeed = settings.Wind.Speed;
        float gustiness = settings.Wind.Gustiness;
        WeatherSyncConfig sync = settings.WeatherSync;
        WeatherBlend.Apply(sync, weather, scaled, ref windSpeed, ref gustiness);
        for (int i = 0; i < scaled.Length; i++)
        {
            scaled[i] *= decision.IntensityScale;
        }

        bool snowStays = sync.Enabled && weather is { SnowStays: true }
            && !sync.IsPaused(SyncElement.Snow) && sync.Weight(SyncElement.Snow) > 0f;

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
            Wind = new Vector2(windSpeed, 0f),
            Gustiness = gustiness,
            SnowStays = snowStays,
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
