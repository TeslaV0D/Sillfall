using System.Globalization;
using Sillfall.Core.Localization;
using Sillfall.Core.Overlay;

namespace Sillfall.Core.Tray;

/// <summary>What the tray menu can do. The app wires these to its window and overlay.</summary>
/// <param name="Open">Shows the main window.</param>
/// <param name="SetRain">Sets the rain intensity, 0 to 1.</param>
/// <param name="SetSnow">Sets the snow intensity, 0 to 1.</param>
/// <param name="SetLeaves">Sets the leaf intensity, 0 to 1.</param>
/// <param name="SetFog">Sets the fog intensity, 0 to 1.</param>
/// <param name="SetSun">Sets the sunlight intensity, 0 to 1.</param>
/// <param name="SetGrass">Sets the grass intensity, 0 to 1.</param>
/// <param name="SetSound">Switches the weather sound on or off.</param>
/// <param name="SetPaused">Pauses or resumes all effects.</param>
/// <param name="Quit">Ends the app.</param>
/// <param name="CollectVisitor">Collects the visitor that came first (the keyboard alternative), if any.</param>
/// <param name="VisitorsOnScreen">How many visitors are on screen right now.</param>
public sealed record TrayCommands(
    Action Open,
    Action<float> SetRain,
    Action<float> SetSnow,
    Action<float> SetLeaves,
    Action<float> SetFog,
    Action<float> SetSun,
    Action<float> SetGrass,
    Action<bool> SetSound,
    Action<bool> SetPaused,
    Action Quit,
    Action? CollectVisitor = null,
    int VisitorsOnScreen = 0);

/// <summary>
/// The minimal tray menu of the MVP (PLAN.md, section 12: MVP cut): open the window, every effect in
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
            new TrayMenuItem { Text = Texts.Format("Tray.Open", AppInfo.Name), Invoke = commands.Open, IsDefault = true },
            TrayMenuItem.Separator,
            EffectMenu(Texts.Get("Tray.Rain"), options.Rain.ClampedIntensity, commands.SetRain, paused),
            EffectMenu(Texts.Get("Tray.Snow"), options.Snow.ClampedIntensity, commands.SetSnow, paused),
            EffectMenu(Texts.Get("Tray.Leaves"), options.Leaves.ClampedIntensity, commands.SetLeaves, paused),
            EffectMenu(Texts.Get("Tray.Fog"), options.Fog.ClampedIntensity, commands.SetFog, paused),
            EffectMenu(Texts.Get("Tray.Sun"), options.Sun.ClampedIntensity, commands.SetSun, paused),
            EffectMenu(Texts.Get("Tray.Grass"), options.Grass.ClampedIntensity, commands.SetGrass, paused),
            new TrayMenuItem { Text = Texts.Get("Tray.Sound"), IsChecked = soundOn, Invoke = () => commands.SetSound(!soundOn) },
            .. VisitorItems(commands),
            TrayMenuItem.Separator,
            new TrayMenuItem { Text = Texts.Get("Tray.Pause"), IsChecked = paused, Invoke = () => commands.SetPaused(!paused) },
            TrayMenuItem.Separator,
            new TrayMenuItem { Text = Texts.Get("Tray.Quit"), Invoke = commands.Quit },
        ];
    }

    /// <summary>"Collect visitor", only while one is on screen: the keyboard and Narrator way to collect (PLAN.md, section 6).</summary>
    private static IEnumerable<TrayMenuItem> VisitorItems(TrayCommands commands)
    {
        if (commands.CollectVisitor is { } collect && commands.VisitorsOnScreen > 0)
        {
            yield return TrayMenuItem.Separator;
            yield return new TrayMenuItem
            {
                Text = commands.VisitorsOnScreen == 1 ? Texts.Get("Tray.CollectOne") : Texts.Format("Tray.CollectMany", commands.VisitorsOnScreen),
                Invoke = collect,
            };
        }
    }

    /// <summary>Tooltip of the tray icon: the name and what is on.</summary>
    public static string Tooltip(OverlayOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);
        if (!options.Enabled)
        {
            return Texts.Format("Tray.TooltipPaused", AppInfo.Name);
        }

        var parts = new List<string>();
        if (options.Rain.ClampedIntensity > 0f)
        {
            parts.Add(Texts.Format("Tray.TooltipRain", Percent(options.Rain.ClampedIntensity)));
        }

        if (options.Snow.ClampedIntensity > 0f)
        {
            parts.Add(Texts.Format("Tray.TooltipSnow", Percent(options.Snow.ClampedIntensity)));
        }

        if (options.Leaves.ClampedIntensity > 0f)
        {
            parts.Add(Texts.Format("Tray.TooltipLeaves", Percent(options.Leaves.ClampedIntensity)));
        }

        if (options.Fog.ClampedIntensity > 0f)
        {
            parts.Add(Texts.Format("Tray.TooltipFog", Percent(options.Fog.ClampedIntensity)));
        }

        if (options.Sun.ClampedIntensity > 0f)
        {
            parts.Add(Texts.Format("Tray.TooltipSun", Percent(options.Sun.ClampedIntensity)));
        }

        if (options.Grass.ClampedIntensity > 0f)
        {
            parts.Add(Texts.Format("Tray.TooltipGrass", Percent(options.Grass.ClampedIntensity)));
        }

        return parts.Count == 0 ? Texts.Format("Tray.TooltipClear", AppInfo.Name) : Texts.Format("Tray.TooltipReason", AppInfo.Name, string.Join(", ", parts));
    }

    private static TrayMenuItem EffectMenu(string name, float intensity, Action<float> set, bool paused)
    {
        var children = new List<TrayMenuItem>(Steps.Count);
        foreach ((string stepName, float stepIntensity) in Steps)
        {
            float value = stepIntensity;
            children.Add(new TrayMenuItem
            {
                Text = Texts.Get($"Tray.Step.{stepName}"),
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
