using System.Globalization;
using Weatherly.Core.Overlay;

namespace Weatherly.Core.Tray;

/// <summary>What the tray menu can do. The app wires these to its window and overlay.</summary>
/// <param name="Open">Shows the main window.</param>
/// <param name="SetRain">Sets the rain intensity, 0 to 1.</param>
/// <param name="SetSnow">Sets the snow intensity, 0 to 1.</param>
/// <param name="SetSound">Switches the weather sound on or off.</param>
/// <param name="SetPaused">Pauses or resumes all effects.</param>
/// <param name="Quit">Ends the app.</param>
public sealed record TrayCommands(
    Action Open,
    Action<float> SetRain,
    Action<float> SetSnow,
    Action<bool> SetSound,
    Action<bool> SetPaused,
    Action Quit);

/// <summary>
/// The minimal tray menu of the MVP (PLAN.md, section 12: MVP cut): open the window, rain and snow in
/// four steps, sound, pause and quit. The full tray flyout follows with M9.
/// </summary>
public static class TrayMenu
{
    /// <summary>The steps offered for every effect, from off to full.</summary>
    public static IReadOnlyList<(string Name, float Intensity)> Steps { get; } =
    [
        ("Off", 0f),
        ("Light", 0.3f),
        ("Medium", 0.6f),
        ("Heavy", 1f),
    ];

    /// <summary>Builds the menu for the current state. Called every time the menu opens, so it is always up to date.</summary>
    public static IReadOnlyList<TrayMenuItem> Build(OverlayOptions options, bool soundOn, TrayCommands commands)
    {
        ArgumentNullException.ThrowIfNull(options);
        ArgumentNullException.ThrowIfNull(commands);
        bool paused = !options.Enabled;
        return
        [
            new TrayMenuItem { Text = $"Open {AppInfo.Name}", Invoke = commands.Open, IsDefault = true },
            TrayMenuItem.Separator,
            EffectMenu("Rain", options.Rain.ClampedIntensity, commands.SetRain, paused),
            EffectMenu("Snow", options.Snow.ClampedIntensity, commands.SetSnow, paused),
            new TrayMenuItem { Text = "Sound", IsChecked = soundOn, Invoke = () => commands.SetSound(!soundOn) },
            TrayMenuItem.Separator,
            new TrayMenuItem { Text = "Pause all effects", IsChecked = paused, Invoke = () => commands.SetPaused(!paused) },
            TrayMenuItem.Separator,
            new TrayMenuItem { Text = "Quit", Invoke = commands.Quit },
        ];
    }

    /// <summary>Tooltip of the tray icon: the name and what is on.</summary>
    public static string Tooltip(OverlayOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);
        if (!options.Enabled)
        {
            return $"{AppInfo.Name}: paused";
        }

        var parts = new List<string>();
        if (options.Rain.ClampedIntensity > 0f)
        {
            parts.Add($"rain {Percent(options.Rain.ClampedIntensity)}");
        }

        if (options.Snow.ClampedIntensity > 0f)
        {
            parts.Add($"snow {Percent(options.Snow.ClampedIntensity)}");
        }

        return parts.Count == 0 ? $"{AppInfo.Name}: clear sky" : $"{AppInfo.Name}: {string.Join(", ", parts)}";
    }

    private static TrayMenuItem EffectMenu(string name, float intensity, Action<float> set, bool paused)
    {
        var children = new List<TrayMenuItem>(Steps.Count);
        foreach ((string stepName, float stepIntensity) in Steps)
        {
            float value = stepIntensity;
            children.Add(new TrayMenuItem
            {
                Text = stepName,
                IsRadio = true,
                IsChecked = MathF.Abs(intensity - value) < 0.025f,
                Invoke = () => set(value),
            });
        }

        // A value set with the slider that is none of the steps is named, so the menu does not look empty.
        string label = intensity > 0f ? $"{name} ({Percent(intensity)})" : name;
        return new TrayMenuItem { Text = label, Children = children, IsEnabled = !paused };
    }

    private static string Percent(float value) => string.Create(CultureInfo.InvariantCulture, $"{MathF.Round(value * 100):0} %");
}
