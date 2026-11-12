using System.Globalization;
using System.IO.Compression;
using System.Text;

namespace Weatherly.Core.Weather.Cities;

/// <summary>A place of the bundled city list.</summary>
public sealed record City(string Name, string Country, GeoPoint Location, int Population)
{
    /// <summary>"Berlin (DE)".</summary>
    public string DisplayName => $"{Name} ({Country})";
}

/// <summary>
/// Offline city search for the weather sync (PLAN.md, section 5): GeoNames' cities with at least 15,000 inhabitants
/// (CC BY 4.0), bundled and searched on the PC, so choosing a city sends nothing anywhere. The list is built by
/// <c>tools/cities/build_cities.py</c>, which stores ready-made search keys for every name and alternate name.
/// </summary>
public sealed class CityIndex
{
    public const string Attribution = "City list: GeoNames (CC BY 4.0)";

    private const string ResourceName = "Weatherly.Core.Weather.Cities.cities15000.tsv.gz";

    private readonly City[] _cities;

    /// <summary>Per city "|key|key|", in the same order as <see cref="_cities"/>.</summary>
    private readonly string[] _keys;

    private CityIndex(City[] cities, string[] keys)
    {
        _cities = cities;
        _keys = keys;
    }

    public int Count => _cities.Length;

    /// <summary>Reads the list that ships with the app (about 34,000 cities).</summary>
    public static CityIndex LoadBundled()
    {
        using Stream stream = typeof(CityIndex).Assembly.GetManifestResourceStream(ResourceName)
            ?? throw new InvalidOperationException($"The city list {ResourceName} is missing from the assembly.");
        using var gzip = new GZipStream(stream, CompressionMode.Decompress);
        using var reader = new StreamReader(gzip, Encoding.UTF8);
        return Parse(reader);
    }

    /// <summary>Reads the list format of <c>build_cities.py</c>. Broken lines are skipped.</summary>
    public static CityIndex Parse(TextReader reader)
    {
        ArgumentNullException.ThrowIfNull(reader);
        var cities = new List<City>();
        var keys = new List<string>();
        while (reader.ReadLine() is { } line)
        {
            string[] fields = line.Split('\t');
            if (fields.Length < 6
                || !double.TryParse(fields[2], NumberStyles.Float, CultureInfo.InvariantCulture, out double latitude)
                || !double.TryParse(fields[3], NumberStyles.Float, CultureInfo.InvariantCulture, out double longitude)
                || !int.TryParse(fields[4], NumberStyles.Integer, CultureInfo.InvariantCulture, out int population))
            {
                continue;
            }

            var location = new GeoPoint(latitude, longitude);
            if (!location.IsValid || fields[0].Length == 0)
            {
                continue;
            }

            cities.Add(new City(fields[0], fields[1], location, population));
            keys.Add(fields[5].StartsWith('|') ? fields[5] : $"|{Normalize(fields[0])}|");
        }

        return new CityIndex([.. cities], [.. keys]);
    }

    /// <summary>
    /// Cities matching <paramref name="query"/>, best first: the whole name, then the start of a name, then the start of a
    /// word in a name; bigger cities first among equals. "Name, CC" limits the search to a country (ISO code).
    /// </summary>
    public IReadOnlyList<City> Search(string query, int limit = 10)
    {
        ArgumentNullException.ThrowIfNull(query);
        string country = string.Empty;
        int comma = query.LastIndexOf(',');
        if (comma >= 0)
        {
            country = query[(comma + 1)..].Trim();
            query = query[..comma];
        }

        string key = Normalize(query);
        if (key.Length == 0 || limit <= 0)
        {
            return [];
        }

        string whole = $"|{key}|";
        string start = $"|{key}";
        string word = $" {key}";
        var matches = new List<(int Score, int Index)>();
        for (int i = 0; i < _cities.Length; i++)
        {
            if (country.Length > 0 && !_cities[i].Country.Equals(country, StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            string keys = _keys[i];
            int score = keys.Contains(whole, StringComparison.Ordinal) ? 3
                : keys.Contains(start, StringComparison.Ordinal) ? 2
                : keys.Contains(word, StringComparison.Ordinal) || keys.Contains($"-{key}", StringComparison.Ordinal) ? 1
                : 0;
            if (score > 0)
            {
                matches.Add((score, i));
            }
        }

        // The list is sorted by population, so the index breaks ties in favour of bigger cities.
        return [.. matches.OrderByDescending(match => match.Score).ThenBy(match => match.Index).Take(limit).Select(match => _cities[match.Index])];
    }

    /// <summary>
    /// The city that names <paramref name="location"/> (from the location service): the biggest city within about 15 km,
    /// so the centre of Berlin is "Berlin" and not one of its districts; far from any city the nearest one.
    /// </summary>
    public City? Nearest(GeoPoint location)
    {
        const double NearbyDegrees = 0.135;
        City? nearest = null;
        City? biggestNearby = null;
        double best = double.MaxValue;
        double cosine = Math.Cos(location.Latitude * Math.PI / 180.0);
        foreach (City city in _cities)
        {
            double dLatitude = city.Location.Latitude - location.Latitude;
            double dLongitude = Math.IEEERemainder(city.Location.Longitude - location.Longitude, 360.0) * cosine;
            double distance = (dLatitude * dLatitude) + (dLongitude * dLongitude);
            if (distance < best)
            {
                best = distance;
                nearest = city;
            }

            if (distance <= NearbyDegrees * NearbyDegrees && (biggestNearby is null || city.Population > biggestNearby.Population))
            {
                biggestNearby = city;
            }
        }

        return biggestNearby ?? nearest;
    }

    /// <summary>
    /// The search form of a name: accents dropped, lower case, a few letters spelled out, single spaces. Must match
    /// <c>normalize</c> in <c>tools/cities/build_cities.py</c>.
    /// </summary>
    public static string Normalize(string text)
    {
        ArgumentNullException.ThrowIfNull(text);
        string decomposed = text.Normalize(NormalizationForm.FormKD);
        var builder = new StringBuilder(decomposed.Length);
        bool space = false;
        foreach (char c in decomposed)
        {
            if (CharUnicodeInfo.GetUnicodeCategory(c) == UnicodeCategory.NonSpacingMark)
            {
                continue;
            }

            char lower = char.ToLowerInvariant(c);
            if (char.IsWhiteSpace(lower) || lower == '|')
            {
                space = builder.Length > 0;
                continue;
            }

            if (space)
            {
                builder.Append(' ');
                space = false;
            }

            string? spelled = lower switch
            {
                'ß' => "ss",
                'æ' => "ae",
                'œ' => "oe",
                'ø' => "o",
                'ł' => "l",
                'đ' or 'ð' => "d",
                'þ' => "th",
                'ı' => "i",
                _ => null,
            };
            if (spelled is null)
            {
                builder.Append(lower);
            }
            else
            {
                builder.Append(spelled);
            }
        }

        return builder.ToString();
    }
}
