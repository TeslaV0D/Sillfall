using Sillfall.Core.Settings;

namespace Sillfall.Core.Scenes;

/// <summary>
/// A template (PLAN.md, section 7, page "Templates"): a named weather mix with an emoji. Applying it replaces
/// the effects and the wind; everything else (sound, displays, rules) stays as it is.
/// </summary>
public sealed record Scene
{
    /// <summary>Longest name accepted, in characters.</summary>
    public const int MaxNameLength = 40;

    public string Name { get; set; } = "Scene";

    /// <summary>One emoji (or any short text) shown next to the name.</summary>
    public string Emoji { get; set; } = "🌦️";

    public EffectConfig Rain { get; set; } = new();

    public EffectConfig Snow { get; set; } = new();

    public EffectConfig Leaves { get; set; } = new();

    public EffectConfig Fog { get; set; } = new();

    public EffectConfig Sun { get; set; } = new();

    public EffectConfig Grass { get; set; } = new();

    public float RainDepth { get; set; } = 0.5f;

    public WindConfig Wind { get; set; } = new();

    /// <summary>The current weather of <paramref name="settings"/> as a template.</summary>
    public static Scene From(AppSettings settings, string name, string emoji)
    {
        ArgumentNullException.ThrowIfNull(settings);
        return new Scene
        {
            Name = name,
            Emoji = emoji,
            Rain = settings.Rain,
            Snow = settings.Snow,
            Leaves = settings.Leaves,
            Fog = settings.Fog,
            Sun = settings.Sun,
            Grass = settings.Grass,
            RainDepth = settings.RainDepth,
            Wind = settings.Wind,
        }.Sanitized();
    }

    /// <summary>The settings with this template's weather.</summary>
    public AppSettings ApplyTo(AppSettings settings)
    {
        ArgumentNullException.ThrowIfNull(settings);
        Scene scene = Sanitized();
        return settings with
        {
            Rain = scene.Rain,
            Snow = scene.Snow,
            Leaves = scene.Leaves,
            Fog = scene.Fog,
            Sun = scene.Sun,
            Grass = scene.Grass,
            RainDepth = scene.RainDepth,
            Wind = scene.Wind,
        };
    }

    /// <summary>Valid values, a short single-line name and a short emoji. Scenes may come from links: never trust them.</summary>
    public Scene Sanitized()
    {
        string name = new string([.. (Name ?? string.Empty).Where(c => !char.IsControl(c))]).Trim();
        string emoji = new string([.. (Emoji ?? string.Empty).Where(c => !char.IsControl(c))]).Trim();
        AppSettings checkedValues = new AppSettings
        {
            Rain = Rain ?? new(),
            Snow = Snow ?? new(),
            Leaves = Leaves ?? new(),
            Fog = Fog ?? new(),
            Sun = Sun ?? new(),
            Grass = Grass ?? new(),
            RainDepth = RainDepth,
            Wind = Wind ?? new(),
        }.Sanitized();

        return this with
        {
            Name = name.Length == 0 ? "Scene" : name[..Math.Min(name.Length, MaxNameLength)],
            Emoji = emoji.Length == 0 ? "🌦️" : emoji[..Math.Min(emoji.Length, 8)],
            Rain = checkedValues.Rain,
            Snow = checkedValues.Snow,
            Leaves = checkedValues.Leaves,
            Fog = checkedValues.Fog,
            Sun = checkedValues.Sun,
            Grass = checkedValues.Grass,
            RainDepth = checkedValues.RainDepth,
            Wind = checkedValues.Wind,
        };
    }
}
