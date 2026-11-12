using System.Text.Json.Serialization;
using Weatherly.Core.Weather;

namespace Weatherly.Core.Settings;

/// <summary>What the weather sync can drive; grass has no weather of its own (it grows faster in rain anyway).</summary>
public enum SyncElement
{
    Rain,
    Snow,
    Leaves,
    Fog,
    Sun,
    Wind,
}

/// <summary>Where the sync takes the weather from.</summary>
public enum LocationSource
{
    /// <summary>A city from the bundled list, chosen offline.</summary>
    City,

    /// <summary>The PC's location service (Windows asks for permission).</summary>
    Device,
}

/// <summary>A chosen city: its name for the settings and its location.</summary>
public sealed record CityChoice
{
    public string Name { get; set; } = string.Empty;

    /// <summary>ISO 3166 country code, for example "DE".</summary>
    public string Country { get; set; } = string.Empty;

    public double Latitude { get; set; }

    public double Longitude { get; set; }

    [JsonIgnore]
    public GeoPoint Location => new(Latitude, Longitude);
}

/// <summary>
/// The weather sync (PLAN.md, section 5): off by default, because it is the only feature that uses the network.
/// </summary>
public sealed record WeatherSyncConfig
{
    public bool Enabled { get; set; }

    public LocationSource Source { get; set; } = LocationSource.City;

    public CityChoice? City { get; set; }

    /// <summary>The weather turned around (<see cref="WeatherMapper.Invert"/>).</summary>
    public bool Invert { get; set; }

    /// <summary>
    /// Custom sync: how much of each element follows the weather, 0 (the manual value) to 1 (the weather). Elements
    /// that are missing follow the weather fully.
    /// </summary>
    public Dictionary<SyncElement, float> Weights { get; set; } = [];

    /// <summary>Elements the user changed by hand while the sync ran: they keep the manual value until "Resume sync".</summary>
    public List<SyncElement> Paused { get; set; } = [];

    public float Weight(SyncElement element) => Weights.TryGetValue(element, out float weight) ? weight : 1f;

    public bool IsPaused(SyncElement element) => Paused.Contains(element);

    public WeatherSyncConfig WithWeight(SyncElement element, float weight) =>
        this with { Weights = new Dictionary<SyncElement, float>(Weights) { [element] = Math.Clamp(weight, 0f, 1f) } };

    /// <summary>The user moved this element's slider: the sync leaves it alone.</summary>
    public WeatherSyncConfig WithPaused(SyncElement element) =>
        IsPaused(element) ? this : this with { Paused = [.. Paused, element] };

    public WeatherSyncConfig Resumed() => this with { Paused = [] };

    public WeatherSyncConfig Sanitized() => this with
    {
        Source = Enum.IsDefined(Source) ? Source : LocationSource.City,
        City = City is { } city && new GeoPoint(city.Latitude, city.Longitude).IsValid && !string.IsNullOrWhiteSpace(city.Name)
            ? city with { Name = city.Name.Trim(), Country = (city.Country ?? string.Empty).Trim() }
            : null,
        Weights = (Weights ?? []).Where(pair => Enum.IsDefined(pair.Key))
            .ToDictionary(pair => pair.Key, pair => EffectConfig.Clamp01(pair.Value, 1f)),
        Paused = [.. (Paused ?? []).Where(Enum.IsDefined).Distinct()],
    };
}

/// <summary>
/// Mixes the weather into the manual values (PLAN.md, section 5: custom sync): <c>final = lerp(manual, synced, weight)</c>
/// per element, except for paused elements, which keep the manual value.
/// </summary>
public static class WeatherBlend
{
    /// <summary>
    /// Mixes <paramref name="targets"/> into <paramref name="intensities"/> (one per <see cref="EffectKind"/>) and the wind.
    /// Without targets (sync off, no forecast yet) nothing changes.
    /// </summary>
    public static void Apply(WeatherSyncConfig sync, WeatherTargets? targets, Span<float> intensities, ref float windSpeed, ref float gustiness)
    {
        ArgumentNullException.ThrowIfNull(sync);
        if (!sync.Enabled || targets is null)
        {
            return;
        }

        if (intensities.Length < 6)
        {
            throw new ArgumentException("One intensity per effect is needed.", nameof(intensities));
        }

        Mix(sync, SyncElement.Rain, targets.Rain, ref intensities[(int)EffectKind.Rain]);
        Mix(sync, SyncElement.Snow, targets.Snow, ref intensities[(int)EffectKind.Snow]);
        Mix(sync, SyncElement.Leaves, targets.Leaves, ref intensities[(int)EffectKind.Leaves]);
        Mix(sync, SyncElement.Fog, targets.Fog, ref intensities[(int)EffectKind.Fog]);
        Mix(sync, SyncElement.Sun, targets.Sun, ref intensities[(int)EffectKind.Sun]);
        Mix(sync, SyncElement.Wind, targets.WindSpeed, ref windSpeed);
        Mix(sync, SyncElement.Wind, targets.Gustiness, ref gustiness);
    }

    /// <summary>The effect a sync element stands for (<see langword="null"/> for the wind).</summary>
    public static EffectKind? EffectOf(SyncElement element) => element switch
    {
        SyncElement.Rain => EffectKind.Rain,
        SyncElement.Snow => EffectKind.Snow,
        SyncElement.Leaves => EffectKind.Leaves,
        SyncElement.Fog => EffectKind.Fog,
        SyncElement.Sun => EffectKind.Sun,
        _ => null,
    };

    /// <summary>The sync element of an effect (<see langword="null"/> for grass).</summary>
    public static SyncElement? ElementOf(EffectKind kind) => kind switch
    {
        EffectKind.Rain => SyncElement.Rain,
        EffectKind.Snow => SyncElement.Snow,
        EffectKind.Leaves => SyncElement.Leaves,
        EffectKind.Fog => SyncElement.Fog,
        EffectKind.Sun => SyncElement.Sun,
        _ => null,
    };

    private static void Mix(WeatherSyncConfig sync, SyncElement element, float synced, ref float value)
    {
        if (sync.IsPaused(element))
        {
            return;
        }

        float weight = sync.Weight(element);
        value += (synced - value) * weight;
    }
}
