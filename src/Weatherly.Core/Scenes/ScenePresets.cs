using Weatherly.Core.Settings;

namespace Weatherly.Core.Scenes;

/// <summary>Built-in templates (PLAN.md, section 7: own presets). Names are translated with the UI in M12.</summary>
public static class ScenePresets
{
    public static IReadOnlyList<Scene> All { get; } =
    [
        new Scene
        {
            Name = "Thunderstorm",
            Emoji = "⛈️",
            Rain = new EffectConfig { Intensity = 1f },
            Fog = new EffectConfig { Intensity = 0.35f, Color = "#9AA3B0" },
            RainDepth = 0.7f,
            Wind = new WindConfig { Speed = 220f, Gustiness = 0.9f },
        },
        new Scene
        {
            Name = "Winter evening",
            Emoji = "❄️",
            Snow = new EffectConfig { Intensity = 0.6f },
            Fog = new EffectConfig { Intensity = 0.25f, Color = "#C8D2E6" },
            Wind = new WindConfig { Speed = 30f, Gustiness = 0.3f },
        },
        new Scene
        {
            Name = "Autumn wind",
            Emoji = "🍂",
            Leaves = new EffectConfig { Intensity = 0.8f },
            Sun = new EffectConfig { Intensity = 0.4f },
            Wind = new WindConfig { Speed = 160f, Gustiness = 0.8f },
        },
        new Scene
        {
            Name = "Neon",
            Emoji = "🌃",
            Rain = new EffectConfig { Intensity = 0.7f, Color = "#FF4FD8", Brightness = 1.4f },
            Fog = new EffectConfig { Intensity = 0.3f, Color = "#4FD8FF" },
            Wind = new WindConfig { Speed = -60f, Gustiness = 0.4f },
        },
        new Scene
        {
            Name = "Spring morning",
            Emoji = "🌱",
            Grass = new EffectConfig { Intensity = 1f },
            Sun = new EffectConfig { Intensity = 0.7f },
            Fog = new EffectConfig { Intensity = 0.2f },
            Wind = new WindConfig { Speed = 25f, Gustiness = 0.4f },
        },
        new Scene
        {
            Name = "Clear sky",
            Emoji = "☀️",
        },
    ];
}
