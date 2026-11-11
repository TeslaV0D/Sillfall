using System.Globalization;
using System.Numerics;
using Weatherly.Core.Effects;

namespace Weatherly.Core.Settings;

/// <summary>How an effect's intensity is chosen (PLAN.md, section 4.8).</summary>
public enum IntensityMode
{
    /// <summary>The intensity set by the user.</summary>
    Fixed,

    /// <summary>A smooth random walk between <see cref="EffectConfig.RandomMin"/> and <see cref="EffectConfig.RandomMax"/>.</summary>
    Random,
}

/// <summary>Stored settings of one effect: what the settings window shows for it.</summary>
public sealed record EffectConfig
{
    /// <summary>0 switches the effect off, 1 is the strongest. In random mode: the value while the walk is paused.</summary>
    public float Intensity { get; set; }

    /// <summary>Own colour as "#RRGGBB", or <see langword="null"/> for the effect's natural colours.</summary>
    public string? Color { get; set; }

    /// <summary>0.25 to 2, 1 is the default look.</summary>
    public float Brightness { get; set; } = 1f;

    public IntensityMode Mode { get; set; } = IntensityMode.Fixed;

    /// <summary>Lowest intensity of the random walk, 0 to 1.</summary>
    public float RandomMin { get; set; }

    /// <summary>Highest intensity of the random walk, 0 to 1.</summary>
    public float RandomMax { get; set; } = 1f;

    /// <summary>Minutes between the random walk's new targets: from minutes to hours.</summary>
    public float RandomMinutes { get; set; } = 10f;

    /// <summary>Shortest and longest rate the settings window offers.</summary>
    public const float MinRandomMinutes = 1f;

    public const float MaxRandomMinutes = 240f;

    /// <summary>Values in their valid ranges, an unreadable colour dropped.</summary>
    public EffectConfig Sanitized()
    {
        float min = Clamp01(RandomMin);
        float max = Clamp01(RandomMax, 1f);
        return this with
        {
            Intensity = Clamp01(Intensity),
            Color = ParseColor(Color) is { } color ? FormatColor(color) : null,
            Brightness = float.IsFinite(Brightness) ? Math.Clamp(Brightness, 0.25f, 2f) : 1f,
            Mode = Enum.IsDefined(Mode) ? Mode : IntensityMode.Fixed,
            RandomMin = Math.Min(min, max),
            RandomMax = Math.Max(min, max),
            RandomMinutes = float.IsFinite(RandomMinutes) ? Math.Clamp(RandomMinutes, MinRandomMinutes, MaxRandomMinutes) : 10f,
        };
    }

    /// <summary>What the effect runs with at <paramref name="intensity"/> (the fixed value or the random walk's current one).</summary>
    public EffectSettings ToEffectSettings(float intensity) => new()
    {
        Intensity = Clamp01(intensity),
        Color = ParseColor(Color),
        Brightness = Brightness,
    };

    /// <summary>"#RRGGBB" to linear 0..1 RGB, or <see langword="null"/>.</summary>
    public static Vector3? ParseColor(string? text)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return null;
        }

        ReadOnlySpan<char> hex = text.AsSpan().Trim().TrimStart('#');
        if (hex.Length != 6 || !uint.TryParse(hex, NumberStyles.HexNumber, CultureInfo.InvariantCulture, out uint value))
        {
            return null;
        }

        return new Vector3((value >> 16) & 0xFF, (value >> 8) & 0xFF, value & 0xFF) / 255f;
    }

    public static string FormatColor(Vector3 color)
    {
        static int Byte(float channel) => (int)MathF.Round(Math.Clamp(channel, 0f, 1f) * 255f);
        return string.Create(CultureInfo.InvariantCulture, $"#{Byte(color.X):X2}{Byte(color.Y):X2}{Byte(color.Z):X2}");
    }

    internal static float Clamp01(float value, float fallback = 0f) => float.IsFinite(value) ? Math.Clamp(value, 0f, 1f) : fallback;
}
