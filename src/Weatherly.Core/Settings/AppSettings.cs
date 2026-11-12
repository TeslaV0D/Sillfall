using System.Numerics;
using System.Text.Json.Serialization;
using Weatherly.Core.Effects.Sun;
using Weatherly.Core.Overlay;

namespace Weatherly.Core.Settings;

/// <summary>The six weather effects, in the order of the settings window.</summary>
public enum EffectKind
{
    Rain,
    Snow,
    Leaves,
    Fog,
    Sun,
    Grass,
}

/// <summary>How smooth versus how frugal the animation runs (PLAN.md, section 0: Smoothness).</summary>
public enum Smoothness
{
    /// <summary>60 fps on mains power, 30 on battery.</summary>
    Auto,

    /// <summary>30 fps.</summary>
    LeastEnergy,

    /// <summary>60 fps.</summary>
    Balanced,

    /// <summary>The monitor's refresh rate.</summary>
    Smoothest,
}

/// <summary>What happens on battery power.</summary>
public enum BatteryBehavior
{
    Nothing,

    /// <summary>Half the intensity and the frame rate of <see cref="Smoothness.LeastEnergy"/>.</summary>
    Reduce,

    /// <summary>All effects pause until the power is back.</summary>
    Pause,
}

/// <summary>How the app follows the system's "show animations" setting.</summary>
public enum ReducedMotion
{
    /// <summary>Pause when Windows has animations switched off.</summary>
    FollowSystem,

    Always,

    Never,
}

/// <summary>What an app rule does while its process is involved.</summary>
public enum AppRuleAction
{
    /// <summary>All effects pause while a window of the process is in the foreground.</summary>
    PauseWhenForeground,

    /// <summary>No weather is drawn over the windows of the process.</summary>
    HideOverWindows,
}

public sealed record WindConfig
{
    /// <summary>Steady wind in pixels per second at 100 %, positive to the right. -400 to 400.</summary>
    public float Speed { get; set; }

    /// <summary>Strength of the gusts, 0 to 1.</summary>
    public float Gustiness { get; set; } = 0.5f;
}

public sealed record SoundConfig
{
    /// <summary>Default: all sound off (PLAN.md, section 8).</summary>
    public bool Muted { get; set; } = true;

    public float Master { get; set; } = 0.8f;

    public float Rain { get; set; } = 0.6f;

    public float Wind { get; set; } = 0.5f;

    public float Visitors { get; set; } = 0.7f;
}

/// <summary>Settings of one monitor, by its device name.</summary>
public sealed record DisplayConfig
{
    public string DeviceName { get; set; } = string.Empty;

    public bool Enabled { get; set; } = true;

    /// <summary>Opacity of the weather on this monitor, 0.1 to 1.</summary>
    public float Intensity { get; set; } = 1f;
}

/// <summary>A rule for the windows of one process, by its executable name (for example "obs64.exe").</summary>
public sealed record AppRule
{
    public string ProcessName { get; set; } = string.Empty;

    public AppRuleAction Action { get; set; }
}

public sealed record PerformanceConfig
{
    public Smoothness Smoothness { get; set; } = Smoothness.Auto;

    public BatteryBehavior Battery { get; set; } = BatteryBehavior.Reduce;
}

/// <summary>The visitors (PLAN.md, section 6).</summary>
public sealed record VisitorConfig
{
    public Visitors.VisitorFrequency Frequency { get; set; } = Visitors.VisitorFrequency.Sometimes;

    /// <summary>Opt-in: visitors can be picked up and dropped into the box (small hit windows follow them).</summary>
    public bool Interactive { get; set; }

    /// <summary>A notification says who came and how to collect it with the keyboard (helps with Narrator).</summary>
    public bool Announce { get; set; }
}

public sealed record AdvancedConfig
{
    public bool ExcludeFromCapture { get; set; }

    public bool Autostart { get; set; }

    public ReducedMotion ReducedMotion { get; set; } = ReducedMotion.FollowSystem;

    /// <summary>All weather pauses in a Remote Desktop session (every frame would go over the network).</summary>
    public bool PauseInRemoteSession { get; set; } = true;

    /// <summary>The weather steps aside while Start, search or a shell panel is open (it would lie on top of them).</summary>
    public bool HideOverShell { get; set; } = true;

    /// <summary>All weather pauses while Windows' high contrast mode is on (it would lower the contrast).</summary>
    public bool PauseInHighContrast { get; set; } = true;

    /// <summary>Language of the app: empty follows Windows, otherwise a code of <c>Texts.Languages</c> ("en", "de").</summary>
    public string Language { get; set; } = string.Empty;
}

/// <summary>
/// Everything the app remembers (PLAN.md, section 7), stored as versioned JSON. Version 1 (the MVP's
/// UserSettings) is migrated on load. Unknown fields are ignored and missing ones get defaults, so files of
/// other versions keep loading.
/// </summary>
public sealed record AppSettings
{
    public const int CurrentVersion = 2;

    public int Version { get; set; } = CurrentVersion;

    /// <summary>Master switch (false: paused from the tray or the flyout).</summary>
    public bool Enabled { get; set; } = true;

    public EffectConfig Rain { get; set; } = new();

    public EffectConfig Snow { get; set; } = new();

    public EffectConfig Leaves { get; set; } = new();

    public EffectConfig Fog { get; set; } = new();

    public EffectConfig Sun { get; set; } = new();

    public EffectConfig Grass { get; set; } = new();

    /// <summary>Share of rain drops that interact with windows, 0 to 1.</summary>
    public float RainDepth { get; set; } = 0.5f;

    public SunSide SunSide { get; set; } = SunSide.Right;

    public WindConfig Wind { get; set; } = new();

    public SoundConfig Sound { get; set; } = new();

    public List<DisplayConfig> Displays { get; set; } = [];

    public List<AppRule> AppRules { get; set; } = [];

    public PerformanceConfig Performance { get; set; } = new();

    public AdvancedConfig Advanced { get; set; } = new();

    public WeatherSyncConfig WeatherSync { get; set; } = new();

    public VisitorConfig Visitors { get; set; } = new();

    /// <summary>The user's own templates (presets are built in and not stored).</summary>
    public List<Scenes.Scene> Templates { get; set; } = [];

    public EffectConfig Effect(EffectKind kind) => kind switch
    {
        EffectKind.Rain => Rain,
        EffectKind.Snow => Snow,
        EffectKind.Leaves => Leaves,
        EffectKind.Fog => Fog,
        EffectKind.Sun => Sun,
        EffectKind.Grass => Grass,
        _ => throw new ArgumentOutOfRangeException(nameof(kind)),
    };

    /// <summary>A copy with one effect replaced.</summary>
    public AppSettings WithEffect(EffectKind kind, EffectConfig config) => kind switch
    {
        EffectKind.Rain => this with { Rain = config },
        EffectKind.Snow => this with { Snow = config },
        EffectKind.Leaves => this with { Leaves = config },
        EffectKind.Fog => this with { Fog = config },
        EffectKind.Sun => this with { Sun = config },
        EffectKind.Grass => this with { Grass = config },
        _ => throw new ArgumentOutOfRangeException(nameof(kind)),
    };

    /// <summary>Settings of a monitor, or the defaults if it has none yet.</summary>
    public DisplayConfig Display(string deviceName) =>
        Displays.FirstOrDefault(display => string.Equals(display.DeviceName, deviceName, StringComparison.OrdinalIgnoreCase))
        ?? new DisplayConfig { DeviceName = deviceName };

    /// <summary>Values in their valid ranges: a hand-edited or damaged file never breaks the app.</summary>
    public AppSettings Sanitized() => this with
    {
        Version = CurrentVersion,
        Rain = (Rain ?? new()).Sanitized(),
        Snow = (Snow ?? new()).Sanitized(),
        Leaves = (Leaves ?? new()).Sanitized(),
        Fog = (Fog ?? new()).Sanitized(),
        Sun = (Sun ?? new()).Sanitized(),
        Grass = (Grass ?? new()).Sanitized(),
        RainDepth = EffectConfig.Clamp01(RainDepth, 0.5f),
        SunSide = Enum.IsDefined(SunSide) ? SunSide : SunSide.Right,
        Wind = Wind is null ? new() : Wind with
        {
            Speed = float.IsFinite(Wind.Speed) ? Math.Clamp(Wind.Speed, -400f, 400f) : 0f,
            Gustiness = EffectConfig.Clamp01(Wind.Gustiness, 0.5f),
        },
        Sound = Sound is null ? new() : Sound with
        {
            Master = EffectConfig.Clamp01(Sound.Master, 0.8f),
            Rain = EffectConfig.Clamp01(Sound.Rain, 0.6f),
            Wind = EffectConfig.Clamp01(Sound.Wind, 0.5f),
            Visitors = EffectConfig.Clamp01(Sound.Visitors, 0.7f),
        },
        Displays = [.. (Displays ?? []).Where(display => display is not null && !string.IsNullOrWhiteSpace(display.DeviceName))
            .DistinctBy(display => display.DeviceName, StringComparer.OrdinalIgnoreCase)
            .Select(display => display with { Intensity = float.IsFinite(display.Intensity) ? Math.Clamp(display.Intensity, 0.1f, 1f) : 1f })],
        AppRules = [.. (AppRules ?? []).Where(rule => rule is not null && !string.IsNullOrWhiteSpace(rule.ProcessName) && Enum.IsDefined(rule.Action))
            .Select(rule => rule with { ProcessName = rule.ProcessName.Trim() })
            .DistinctBy(rule => (rule.ProcessName.ToUpperInvariant(), rule.Action))],
        Performance = Performance is null ? new() : Performance with
        {
            Smoothness = Enum.IsDefined(Performance.Smoothness) ? Performance.Smoothness : Smoothness.Auto,
            Battery = Enum.IsDefined(Performance.Battery) ? Performance.Battery : BatteryBehavior.Reduce,
        },
        Advanced = Advanced is null ? new() : Advanced with
        {
            ReducedMotion = Enum.IsDefined(Advanced.ReducedMotion) ? Advanced.ReducedMotion : ReducedMotion.FollowSystem,
            Language = Localization.Texts.Languages.Contains(Advanced.Language?.Trim().ToLowerInvariant() ?? string.Empty)
                ? Advanced.Language!.Trim().ToLowerInvariant()
                : string.Empty,
        },
        WeatherSync = (WeatherSync ?? new()).Sanitized(),
        Visitors = Visitors is null ? new() : Visitors with
        {
            Frequency = Enum.IsDefined(Visitors.Frequency) ? Visitors.Frequency : Weatherly.Core.Visitors.VisitorFrequency.Sometimes,
        },
        Templates = [.. (Templates ?? []).Where(scene => scene is not null).Select(scene => scene.Sanitized())],
    };

    /// <summary>
    /// Overlay options for these settings, with the effects at <paramref name="intensities"/> (fixed values or
    /// the random walk's current ones, see <see cref="IntensityDirector"/>). Everything the settings do not
    /// cover is taken from <paramref name="current"/>.
    /// </summary>
    public OverlayOptions ToOverlayOptions(OverlayOptions current, ReadOnlySpan<float> intensities)
    {
        ArgumentNullException.ThrowIfNull(current);
        if (intensities.Length < 6)
        {
            throw new ArgumentException("One intensity per effect is needed.", nameof(intensities));
        }

        return current with
        {
            Enabled = Enabled,
            ExcludeFromCapture = Advanced.ExcludeFromCapture,
            Rain = Rain.ToEffectSettings(intensities[(int)EffectKind.Rain]),
            Snow = Snow.ToEffectSettings(intensities[(int)EffectKind.Snow]),
            Leaves = Leaves.ToEffectSettings(intensities[(int)EffectKind.Leaves]),
            Fog = Fog.ToEffectSettings(intensities[(int)EffectKind.Fog]),
            Sun = Sun.ToEffectSettings(intensities[(int)EffectKind.Sun]),
            Grass = Grass.ToEffectSettings(intensities[(int)EffectKind.Grass]),
            RainDepth = RainDepth,
            SunSide = SunSide,
            Wind = new Vector2(Wind.Speed, 0f),
            Gustiness = Wind.Gustiness,
        };
    }

    /// <summary>
    /// The settings after the user changed something in the running app: intensities, rain depth, master switch,
    /// screenshot exclusion and sound from <paramref name="options"/>; colours, random mode and everything else stay.
    /// </summary>
    public AppSettings WithOverlay(OverlayOptions options, bool soundOn)
    {
        ArgumentNullException.ThrowIfNull(options);
        return this with
        {
            Enabled = options.Enabled,
            Rain = Rain with { Intensity = options.Rain.Intensity },
            Snow = Snow with { Intensity = options.Snow.Intensity },
            Leaves = Leaves with { Intensity = options.Leaves.Intensity },
            Fog = Fog with { Intensity = options.Fog.Intensity },
            Sun = Sun with { Intensity = options.Sun.Intensity },
            Grass = Grass with { Intensity = options.Grass.Intensity },
            RainDepth = options.RainDepth,
            Sound = Sound with { Muted = !soundOn },
            Advanced = Advanced with { ExcludeFromCapture = options.ExcludeFromCapture },
        };
    }

    /// <summary>Overlay options with every effect at its fixed intensity.</summary>
    public OverlayOptions ToOverlayOptions(OverlayOptions current)
    {
        Span<float> intensities = stackalloc float[6];
        foreach (EffectKind kind in Enum.GetValues<EffectKind>())
        {
            intensities[(int)kind] = Effect(kind).Intensity;
        }

        return ToOverlayOptions(current, intensities);
    }
}

[JsonSourceGenerationOptions(
    WriteIndented = true,
    PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase,
    UseStringEnumConverter = true,
    ReadCommentHandling = System.Text.Json.JsonCommentHandling.Skip,
    AllowTrailingCommas = true)]
[JsonSerializable(typeof(AppSettings))]
internal sealed partial class AppSettingsJsonContext : JsonSerializerContext;
