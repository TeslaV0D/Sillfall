using Sillfall.Core.Localization;
using Sillfall.Core.Settings;

namespace Sillfall.Core.Scenes;

/// <summary>Built-in templates (PLAN.md, section 7: own presets), named in the current language on every read.</summary>
public static class ScenePresets
{
    public static IReadOnlyList<Scene> All =>
    [
        new Scene
        {
            Name = Texts.Get("Preset.Thunderstorm"),
            Emoji = "⛈️",
            Rain = new EffectConfig { Intensity = 1f },
            Fog = new EffectConfig { Intensity = 0.35f, Color = "#9AA3B0" },
            RainDepth = 0.7f,
            Wind = new WindConfig { Speed = 220f, Gustiness = 0.9f },
        },
        new Scene
        {
            Name = Texts.Get("Preset.WinterEvening"),
            Emoji = "❄️",
            Snow = new EffectConfig { Intensity = 0.6f },
            Fog = new EffectConfig { Intensity = 0.25f, Color = "#C8D2E6" },
            Wind = new WindConfig { Speed = 30f, Gustiness = 0.3f },
        },
        new Scene
        {
            Name = Texts.Get("Preset.AutumnWind"),
            Emoji = "🍂",
            Leaves = new EffectConfig { Intensity = 0.8f },
            Sun = new EffectConfig { Intensity = 0.4f },
            Wind = new WindConfig { Speed = 160f, Gustiness = 0.8f },
        },
        new Scene
        {
            Name = Texts.Get("Preset.Neon"),
            Emoji = "🌃",
            Rain = new EffectConfig { Intensity = 0.7f, Color = "#FF4FD8", Brightness = 1.4f },
            Fog = new EffectConfig { Intensity = 0.3f, Color = "#4FD8FF" },
            Wind = new WindConfig { Speed = -60f, Gustiness = 0.4f },
        },
        new Scene
        {
            Name = Texts.Get("Preset.SpringMorning"),
            Emoji = "🌱",
            Grass = new EffectConfig { Intensity = 1f },
            Sun = new EffectConfig { Intensity = 0.7f },
            Fog = new EffectConfig { Intensity = 0.2f },
            Wind = new WindConfig { Speed = 25f, Gustiness = 0.4f },
        },
        new Scene
        {
            Name = Texts.Get("Preset.ClearSky"),
            Emoji = "☀️",
        },
    ];
}
