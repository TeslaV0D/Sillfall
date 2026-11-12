using System.Globalization;
using System.Xml.Linq;

namespace Weatherly.Core.Localization;

/// <summary>
/// The app's texts in the user's language (PLAN.md, M12). They live in <c>.resw</c> files next to this class
/// (<c>Strings/en-US</c>, <c>Strings/de-DE</c>), embedded in the assembly and read by this small reader instead of the
/// Windows resource system, so they work the same packaged, unpacked and in tests. English is the base: a key missing
/// in another language shows the English text. <c>LocalizationTests</c> keeps both files complete and in step.
/// </summary>
public static class Texts
{
    /// <summary>Languages with a full translation, by their two-letter code.</summary>
    public static IReadOnlyList<string> Languages { get; } = ["en", "de"];

    private static readonly Dictionary<string, string> s_english = Load("en-US");
    private static Dictionary<string, string> s_current = s_english;

    /// <summary>Two-letter code of the language in use.</summary>
    public static string Language { get; private set; } = "en";

    /// <summary>
    /// Picks the language: a two-letter code from <see cref="Languages"/>, or empty for the language Windows shows
    /// (<see cref="CultureInfo.CurrentUICulture"/>). Anything else is English.
    /// </summary>
    public static void Use(string? language)
    {
        string code = string.IsNullOrWhiteSpace(language) ? CultureInfo.CurrentUICulture.TwoLetterISOLanguageName : language.Trim();
        Language = code.Equals("de", StringComparison.OrdinalIgnoreCase) ? "de" : "en";
        s_current = Language == "de" ? Merge(s_english, Load("de-DE")) : s_english;
    }

    /// <summary>The text for <paramref name="key"/>; the key itself when no language has it (a missing text shows up).</summary>
    public static string Get(string key)
    {
        ArgumentNullException.ThrowIfNull(key);
        return s_current.TryGetValue(key, out string? text) ? text : key;
    }

    /// <summary>The text for <paramref name="key"/> with its <c>{0}</c>, <c>{1}</c> … filled in (current culture).</summary>
    public static string Format(string key, params object?[] arguments) =>
        string.Format(CultureInfo.CurrentCulture, Get(key), arguments);

    /// <summary>All texts of a language file (for tests and tools).</summary>
    public static IReadOnlyDictionary<string, string> All(string folder) => Load(folder);

    /// <summary>Reads the name/value pairs of a <c>.resw</c> (the ResX format).</summary>
    public static Dictionary<string, string> Read(TextReader reader)
    {
        ArgumentNullException.ThrowIfNull(reader);
        var texts = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (XElement data in XDocument.Load(reader).Root!.Elements("data"))
        {
            string? name = (string?)data.Attribute("name");
            string? value = (string?)data.Element("value");
            if (!string.IsNullOrEmpty(name) && value is not null)
            {
                texts[name] = value;
            }
        }

        return texts;
    }

    private static Dictionary<string, string> Load(string folder)
    {
        string resource = $"Weatherly.Core.Localization.Strings.{folder}.Resources.resw";
        using Stream? stream = typeof(Texts).Assembly.GetManifestResourceStream(resource);
        if (stream is null)
        {
            return new Dictionary<string, string>(StringComparer.Ordinal);
        }

        using var reader = new StreamReader(stream);
        return Read(reader);
    }

    private static Dictionary<string, string> Merge(Dictionary<string, string> baseTexts, Dictionary<string, string> overlay)
    {
        var merged = new Dictionary<string, string>(baseTexts, StringComparer.Ordinal);
        foreach ((string key, string value) in overlay)
        {
            merged[key] = value;
        }

        return merged;
    }
}
