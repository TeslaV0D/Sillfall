using System.Text.Json.Serialization;

namespace Weatherly.Core.Settings;

/// <summary>
/// Settings file version 1 (the MVP, ADR-0008): one intensity per effect, rain depth, pause and sound. Only read
/// to migrate it to <see cref="AppSettings"/>.
/// </summary>
internal sealed record SettingsV1
{
    public int Version { get; set; } = 1;

    public bool Enabled { get; set; } = true;

    public float Rain { get; set; }

    public float RainDepth { get; set; } = 0.5f;

    public float Snow { get; set; }

    public float Leaves { get; set; }

    public float Fog { get; set; }

    public float Sun { get; set; }

    public float Grass { get; set; }

    public bool Sound { get; set; }

    public AppSettings Migrate() => new AppSettings
    {
        Enabled = Enabled,
        Rain = new EffectConfig { Intensity = Rain },
        RainDepth = RainDepth,
        Snow = new EffectConfig { Intensity = Snow },
        Leaves = new EffectConfig { Intensity = Leaves },
        Fog = new EffectConfig { Intensity = Fog },
        Sun = new EffectConfig { Intensity = Sun },
        Grass = new EffectConfig { Intensity = Grass },
        Sound = new SoundConfig { Muted = !Sound },
    }.Sanitized();
}

[JsonSourceGenerationOptions(PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase, ReadCommentHandling = System.Text.Json.JsonCommentHandling.Skip, AllowTrailingCommas = true)]
[JsonSerializable(typeof(SettingsV1))]
internal sealed partial class SettingsV1JsonContext : JsonSerializerContext;
