namespace Sillfall.Core.Visitors;

/// <summary>The app's own characters (PLAN.md, section 6). Order matters: it is stored in the collection.</summary>
public enum VisitorKind
{
    /// <summary>Snail with a leaf hat. Loves rain.</summary>
    Pip,

    /// <summary>Penguin with a scarf. Loves snow.</summary>
    Tuck,

    /// <summary>Hedgehog with a leaf on its spines. Loves autumn leaves.</summary>
    Bristle,

    /// <summary>Firefly. Loves fog. Hovers instead of walking.</summary>
    Lumen,

    /// <summary>Cat with sunglasses. Loves sunshine.</summary>
    Sol,

    /// <summary>A small cloud on legs. Loves wind.</summary>
    Drift,

    /// <summary>A walking plant pot. Loves growing grass.</summary>
    Sprout,

    /// <summary>Mouse with an umbrella. Comes in any weather.</summary>
    Nim,
}

/// <summary>The weather a visitor likes best: it comes more often then and is worth more.</summary>
public enum WeatherAffinity
{
    Any,
    Rain,
    Snow,
    Leaves,
    Fog,
    Sun,
    Wind,
    Grass,
}

public enum Rarity
{
    Common,
    Uncommon,
    Rare,
}

/// <param name="Kind">Which character.</param>
/// <param name="Name">Display name.</param>
/// <param name="Description">One line for the collection.</param>
/// <param name="Affinity">Favourite weather.</param>
/// <param name="Rarity">How seldom it comes.</param>
/// <param name="Size">Height relative to the standard visitor height.</param>
/// <param name="WalkSpeed">Walking speed in device-independent pixels per second.</param>
/// <param name="Hovers">Flies a little above the surface instead of walking on it.</param>
public sealed record VisitorProfile(
    VisitorKind Kind,
    string Name,
    string Description,
    WeatherAffinity Affinity,
    Rarity Rarity,
    float Size,
    float WalkSpeed,
    bool Hovers = false)
{
    /// <summary>Points for collecting it, before bonuses.</summary>
    public int BasePoints => Rarity switch
    {
        Rarity.Rare => 50,
        Rarity.Uncommon => 25,
        _ => 10,
    };

    /// <summary>Relative chance to be the next visitor, before the weather.</summary>
    public float BaseWeight => Rarity switch
    {
        Rarity.Rare => 0.25f,
        Rarity.Uncommon => 0.6f,
        _ => 1f,
    };
}

/// <summary>The eight visitors (designs: <c>src/Sillfall.Render/Visitors/*.svg</c>, see ASSETS.md).</summary>
public static class VisitorCatalog
{
    /// <summary>Standard height of a visitor in device-independent pixels.</summary>
    public const float StandardHeight = 40f;

    public static IReadOnlyList<VisitorProfile> All { get; } =
    [
        new(VisitorKind.Pip, "Pip", "A snail who never leaves home without its leaf hat.", WeatherAffinity.Rain, Rarity.Common, 0.9f, 14f),
        new(VisitorKind.Tuck, "Tuck", "A penguin who knits its own scarves.", WeatherAffinity.Snow, Rarity.Uncommon, 1f, 30f),
        new(VisitorKind.Bristle, "Bristle", "A hedgehog who collects autumn leaves.", WeatherAffinity.Leaves, Rarity.Common, 0.9f, 34f),
        new(VisitorKind.Lumen, "Lumen", "A firefly who lights the way through the fog.", WeatherAffinity.Fog, Rarity.Rare, 0.85f, 40f, Hovers: true),
        new(VisitorKind.Sol, "Sol", "A cat who is always ready for the sun.", WeatherAffinity.Sun, Rarity.Uncommon, 1f, 36f),
        new(VisitorKind.Drift, "Drift", "A little cloud that goes where the wind goes.", WeatherAffinity.Wind, Rarity.Common, 1f, 44f),
        new(VisitorKind.Sprout, "Sprout", "A plant pot that went for a walk.", WeatherAffinity.Grass, Rarity.Uncommon, 0.95f, 22f),
        new(VisitorKind.Nim, "Nim", "A mouse with an umbrella for every weather.", WeatherAffinity.Any, Rarity.Common, 0.9f, 38f),
    ];

    public static VisitorProfile Get(VisitorKind kind) => All[(int)kind];
}
