using System.Text.Json;
using System.Text.Json.Serialization;

namespace Weatherly.Core.Visitors;

/// <summary>The achievements (PLAN.md, section 6). Stored by name, so new ones can be added.</summary>
public enum Achievement
{
    /// <summary>Collected the first visitor.</summary>
    FirstGuest,

    /// <summary>Collected ten visitors.</summary>
    Regular,

    /// <summary>Collected all eight.</summary>
    FullHouse,

    /// <summary>Collected the rare one.</summary>
    RareFind,

    /// <summary>Collected a visitor in its favourite weather.</summary>
    InTheirElement,

    /// <summary>Collected visitors in rain, snow, leaves, fog and sunshine.</summary>
    EveryWeather,

    /// <summary>Collected three visitors in a row, each within a minute of the one before.</summary>
    ThreeInARow,

    /// <summary>Reached 100 points.</summary>
    Hundred,
}

/// <summary>Names and descriptions of the achievements, for the collection page.</summary>
public static class Achievements
{
    public static (string Title, string Description) Describe(Achievement achievement) => achievement switch
    {
        Achievement.FirstGuest => ("First guest", "Collect your first visitor."),
        Achievement.Regular => ("Regulars", "Collect ten visitors."),
        Achievement.FullHouse => ("Full house", "Collect all eight visitors."),
        Achievement.RareFind => ("Rare find", "Collect Lumen, the rarest visitor."),
        Achievement.InTheirElement => ("In their element", "Collect a visitor in its favourite weather."),
        Achievement.EveryWeather => ("Every weather", "Collect visitors in rain, snow, falling leaves, fog and sunshine."),
        Achievement.ThreeInARow => ("Three in a row", "Collect three visitors, each within a minute of the one before."),
        Achievement.Hundred => ("A hundred points", "Reach 100 points."),
        _ => (achievement.ToString(), string.Empty),
    };
}

/// <summary>How often one visitor was collected.</summary>
public sealed record VisitorTally
{
    public VisitorKind Kind { get; set; }

    public int Count { get; set; }

    public DateTimeOffset FirstCollected { get; set; }
}

/// <summary>What one collect brought.</summary>
/// <param name="Kind">Who was collected.</param>
/// <param name="Points">Points for it, bonuses included.</param>
/// <param name="Combo">How many in a row (1 for a single one).</param>
/// <param name="FavouriteWeather">Collected in its favourite weather (double points).</param>
/// <param name="NewAchievements">Achievements reached with it.</param>
public sealed record CollectReward(VisitorKind Kind, int Points, int Combo, bool FavouriteWeather, IReadOnlyList<Achievement> NewAchievements);

/// <summary>
/// The user's collection (PLAN.md, section 6): who was collected how often, points, the best combo and achievements.
/// Stored locally only, in <c>collection.json</c> next to the settings.
/// </summary>
public sealed record VisitorAlbum
{
    public const int CurrentVersion = 1;

    /// <summary>Collects within this time continue a combo.</summary>
    public static readonly TimeSpan ComboWindow = TimeSpan.FromMinutes(1);

    /// <summary>A combo multiplies the points by 1 + 0.5 per extra visitor, at most this much.</summary>
    public const float MaxComboFactor = 3f;

    public int Version { get; set; } = CurrentVersion;

    public int Points { get; set; }

    public int BestCombo { get; set; }

    public int Combo { get; set; }

    public DateTimeOffset? LastCollected { get; set; }

    public List<VisitorTally> Visitors { get; set; } = [];

    public List<Achievement> Achievements { get; set; } = [];

    /// <summary>Kinds of weather a visitor was collected in, for <see cref="Achievement.EveryWeather"/>.</summary>
    public List<WeatherAffinity> Weathers { get; set; } = [];

    [JsonIgnore]
    public int TotalCollected => Visitors.Sum(tally => tally.Count);

    public int CountOf(VisitorKind kind) => Visitors.FirstOrDefault(tally => tally.Kind == kind)?.Count ?? 0;

    public bool Has(Achievement achievement) => Achievements.Contains(achievement);

    /// <summary>
    /// The collection after collecting <paramref name="kind"/> at <paramref name="now"/> in <paramref name="mood"/>, and
    /// what it brought: base points by rarity, doubled in the favourite weather, times the combo factor.
    /// </summary>
    public (VisitorAlbum Album, CollectReward Reward) Collect(VisitorKind kind, WeatherMood mood, DateTimeOffset now)
    {
        VisitorProfile profile = VisitorCatalog.Get(kind);
        int combo = LastCollected is { } last && now - last <= ComboWindow && now >= last ? Combo + 1 : 1;
        bool favourite = profile.Affinity != WeatherAffinity.Any && mood.Of(profile.Affinity) >= WeatherMood.Threshold;
        float factor = Math.Min(1f + (0.5f * (combo - 1)), MaxComboFactor);
        int points = (int)MathF.Round(profile.BasePoints * (favourite ? 2f : 1f) * factor);

        List<VisitorTally> visitors = [.. Visitors.Select(tally => tally with { })];
        VisitorTally? existing = visitors.FirstOrDefault(tally => tally.Kind == kind);
        if (existing is null)
        {
            visitors.Add(new VisitorTally { Kind = kind, Count = 1, FirstCollected = now });
        }
        else
        {
            existing.Count++;
        }

        List<WeatherAffinity> weathers = [.. Weathers];
        WeatherAffinity weather = mood.Dominant;
        if (weather is not (WeatherAffinity.Any or WeatherAffinity.Wind or WeatherAffinity.Grass) && !weathers.Contains(weather))
        {
            weathers.Add(weather);
        }

        var next = this with
        {
            Points = Points + points,
            Combo = combo,
            BestCombo = Math.Max(BestCombo, combo),
            LastCollected = now,
            Visitors = visitors,
            Weathers = weathers,
            Achievements = [.. Achievements],
        };

        var reached = new List<Achievement>();
        void Check(Achievement achievement, bool condition)
        {
            if (condition && !next.Achievements.Contains(achievement))
            {
                next.Achievements.Add(achievement);
                reached.Add(achievement);
            }
        }

        Check(Achievement.FirstGuest, true);
        Check(Achievement.Regular, next.TotalCollected >= 10);
        Check(Achievement.FullHouse, Enum.GetValues<VisitorKind>().All(k => next.CountOf(k) > 0));
        Check(Achievement.RareFind, profile.Rarity == Rarity.Rare);
        Check(Achievement.InTheirElement, favourite);
        Check(Achievement.EveryWeather, next.Weathers.Count >= 5);
        Check(Achievement.ThreeInARow, combo >= 3);
        Check(Achievement.Hundred, next.Points >= 100);

        return (next, new CollectReward(kind, points, combo, favourite, reached));
    }

    /// <summary>Repairs a hand-edited or damaged file.</summary>
    public VisitorAlbum Sanitized() => this with
    {
        Version = CurrentVersion,
        Points = Math.Max(Points, 0),
        BestCombo = Math.Max(BestCombo, 0),
        Combo = Math.Max(Combo, 0),
        Visitors = [.. (Visitors ?? []).Where(tally => tally is not null && Enum.IsDefined(tally.Kind) && tally.Count > 0)
            .GroupBy(tally => tally.Kind)
            .Select(group => new VisitorTally { Kind = group.Key, Count = group.Sum(tally => tally.Count), FirstCollected = group.Min(tally => tally.FirstCollected) })],
        Achievements = [.. (Achievements ?? []).Where(Enum.IsDefined).Distinct()],
        Weathers = [.. (Weathers ?? []).Where(Enum.IsDefined).Distinct()],
    };
}

/// <summary>Reads and writes the collection, like <c>SettingsStore</c>: a damaged file never stops the app.</summary>
public sealed class AlbumStore
{
    public AlbumStore(string path)
    {
        ArgumentException.ThrowIfNullOrEmpty(path);
        Path = path;
    }

    public static string DefaultPath => System.IO.Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), AppInfo.Name, "collection.json");

    public string Path { get; }

    public string? LastError { get; private set; }

    public VisitorAlbum Load()
    {
        LastError = null;
        if (!File.Exists(Path))
        {
            return new VisitorAlbum();
        }

        try
        {
            VisitorAlbum? collection = JsonSerializer.Deserialize(File.ReadAllText(Path), VisitorAlbumJsonContext.Default.VisitorAlbum);
            return (collection ?? new VisitorAlbum()).Sanitized();
        }
        catch (Exception exception) when (exception is JsonException or IOException or UnauthorizedAccessException or InvalidOperationException)
        {
            LastError = $"{exception.GetType().Name}: {exception.Message}";
            return new VisitorAlbum();
        }
    }

    /// <summary>Writes the collection through a temporary file, so a crash never leaves half a file.</summary>
    public void Save(VisitorAlbum collection)
    {
        ArgumentNullException.ThrowIfNull(collection);
        string? directory = System.IO.Path.GetDirectoryName(Path);
        if (!string.IsNullOrEmpty(directory))
        {
            Directory.CreateDirectory(directory);
        }

        string temporary = Path + ".tmp";
        File.WriteAllText(temporary, JsonSerializer.Serialize(collection.Sanitized(), VisitorAlbumJsonContext.Default.VisitorAlbum));
        File.Move(temporary, Path, overwrite: true);
    }
}

[JsonSourceGenerationOptions(
    WriteIndented = true,
    PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase,
    UseStringEnumConverter = true,
    AllowTrailingCommas = true)]
[JsonSerializable(typeof(VisitorAlbum))]
internal sealed partial class VisitorAlbumJsonContext : JsonSerializerContext;
