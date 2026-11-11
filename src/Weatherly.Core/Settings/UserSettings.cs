using System.Text.Json.Serialization;
using Weatherly.Core.Overlay;

namespace Weatherly.Core.Settings;

/// <summary>
/// What the app remembers between starts (MVP: the effects and the sound). Stored as JSON. New fields get
/// defaults, so older files keep loading; <see cref="Version"/> is for migrations once the settings grow (M9).
/// </summary>
public sealed record UserSettings
{
    public const int CurrentVersion = 1;

    public int Version { get; set; } = CurrentVersion;

    /// <summary>All effects on (false: paused from the tray).</summary>
    public bool Enabled { get; set; } = true;

    public float Rain { get; set; }

    public float RainDepth { get; set; } = 0.5f;

    public float Snow { get; set; }

    public bool Sound { get; set; }

    /// <summary>The settings as they stand in the running app.</summary>
    public static UserSettings From(OverlayOptions options, bool sound)
    {
        ArgumentNullException.ThrowIfNull(options);
        return new UserSettings
        {
            Enabled = options.Enabled,
            Rain = options.Rain.Intensity,
            RainDepth = options.RainDepth,
            Snow = options.Snow.Intensity,
            Sound = sound,
        }.Sanitized();
    }

    /// <summary>Puts the stored values into overlay options, keeping everything else of <paramref name="options"/>.</summary>
    public OverlayOptions ApplyTo(OverlayOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);
        UserSettings settings = Sanitized();
        return options with
        {
            Enabled = settings.Enabled,
            Rain = options.Rain with { Intensity = settings.Rain },
            RainDepth = settings.RainDepth,
            Snow = options.Snow with { Intensity = settings.Snow },
        };
    }

    /// <summary>Values in their valid ranges: a hand-edited or damaged file never breaks the app.</summary>
    public UserSettings Sanitized() => this with
    {
        Version = CurrentVersion,
        Rain = Clamp01(Rain),
        RainDepth = Clamp01(RainDepth, 0.5f),
        Snow = Clamp01(Snow),
    };

    private static float Clamp01(float value, float fallback = 0f) => float.IsFinite(value) ? Math.Clamp(value, 0f, 1f) : fallback;
}

[JsonSourceGenerationOptions(WriteIndented = true, PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase, ReadCommentHandling = System.Text.Json.JsonCommentHandling.Skip, AllowTrailingCommas = true)]
[JsonSerializable(typeof(UserSettings))]
internal sealed partial class UserSettingsJsonContext : JsonSerializerContext;
