using Weatherly.Core.Localization;
namespace Weatherly.Core.Settings;

/// <summary>What the system reports right now: power source, animation setting and the foreground app.</summary>
/// <param name="OnBattery">Running on battery power.</param>
/// <param name="AnimationsEnabled">Windows' "show animations" setting (SPI_GETCLIENTAREAANIMATION).</param>
/// <param name="ForegroundProcess">Executable name of the foreground window's process ("game.exe"), if known.</param>
public readonly record struct SystemState(bool OnBattery, bool AnimationsEnabled, string? ForegroundProcess)
{
    public static SystemState Default => new(OnBattery: false, AnimationsEnabled: true, ForegroundProcess: null);
}

/// <summary>How the app runs right now.</summary>
/// <param name="Paused">All effects off.</param>
/// <param name="Reason">Why they are paused, for the UI, or <see langword="null"/>.</param>
/// <param name="FrameRateCap">Highest frame rate, 0 for the display's rate.</param>
/// <param name="IntensityScale">Factor on every effect's intensity (1, or less on battery).</param>
public readonly record struct RuntimeDecision(bool Paused, string? Reason, int FrameRateCap, float IntensityScale);

/// <summary>
/// Combines the settings with the system's state (PLAN.md, section 7: smoothness, battery, reduced motion,
/// per-app rules). Pure and testable; the app asks it whenever either side changes.
/// </summary>
public static class RuntimePolicy
{
    public const int EnergySavingFrameRate = 30;

    public const int BalancedFrameRate = 60;

    public static RuntimeDecision Decide(AppSettings settings, SystemState state)
    {
        ArgumentNullException.ThrowIfNull(settings);

        int cap = settings.Performance.Smoothness switch
        {
            Smoothness.LeastEnergy => EnergySavingFrameRate,
            Smoothness.Balanced => BalancedFrameRate,
            Smoothness.Smoothest => 0,
            _ => state.OnBattery ? EnergySavingFrameRate : BalancedFrameRate,
        };

        float scale = 1f;
        if (state.OnBattery && settings.Performance.Battery == BatteryBehavior.Reduce)
        {
            cap = cap == 0 ? EnergySavingFrameRate : Math.Min(cap, EnergySavingFrameRate);
            scale = 0.5f;
        }

        string? reason = PauseReason(settings, state);
        return new RuntimeDecision(reason is not null, reason, cap, scale);
    }

    private static string? PauseReason(AppSettings settings, SystemState state)
    {
        if (!settings.Enabled)
        {
            return Texts.Get("Pause.Off");
        }

        switch (settings.Advanced.ReducedMotion)
        {
            case ReducedMotion.Always:
                return Texts.Get("Pause.ReducedMotion");
            case ReducedMotion.FollowSystem when !state.AnimationsEnabled:
                return Texts.Get("Pause.Animations");
        }

        if (state.OnBattery && settings.Performance.Battery == BatteryBehavior.Pause)
        {
            return Texts.Get("Pause.Battery");
        }

        if (state.ForegroundProcess is { Length: > 0 } foreground)
        {
            foreach (AppRule rule in settings.AppRules)
            {
                if (rule.Action == AppRuleAction.PauseWhenForeground && string.Equals(rule.ProcessName, foreground, StringComparison.OrdinalIgnoreCase))
                {
                    return Texts.Format("Pause.App", rule.ProcessName);
                }
            }
        }

        return null;
    }
}
