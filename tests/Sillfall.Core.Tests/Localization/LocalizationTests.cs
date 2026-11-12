using System.Text.RegularExpressions;
using Sillfall.Core.Localization;
using Sillfall.Core.Tests.Support;
using Sillfall.Core.Visitors;

namespace Sillfall.Core.Tests.Localization;

/// <summary>
/// The app speaks English and German (PLAN.md, M12). These tests keep both <c>.resw</c> files complete and in step, and
/// make sure every text the code asks for exists. They read the files and never switch the global language, so they
/// cannot disturb tests that run at the same time.
/// </summary>
public partial class LocalizationTests
{
    private static readonly IReadOnlyDictionary<string, string> s_english = Texts.All("en-US");
    private static readonly IReadOnlyDictionary<string, string> s_german = Texts.All("de-DE");
    private static readonly string[] s_directions = ["N", "NE", "E", "SE", "S", "SW", "W", "NW"];

    [Fact]
    public void BothLanguages_HaveTheSameKeys()
    {
        s_english.Should().NotBeEmpty();
        s_german.Keys.Should().BeEquivalentTo(s_english.Keys);
    }

    [Fact]
    public void NoText_IsEmpty()
    {
        s_english.Where(pair => string.IsNullOrWhiteSpace(pair.Value)).Should().BeEmpty();
        s_german.Where(pair => string.IsNullOrWhiteSpace(pair.Value)).Should().BeEmpty();
    }

    [Fact]
    public void Translations_KeepTheirPlaceholders()
    {
        foreach ((string key, string english) in s_english)
        {
            Placeholders(s_german[key]).Should().BeEquivalentTo(Placeholders(english), $"\"{key}\" fills in the same values in both languages");
        }
    }

    [Fact]
    public void German_IsReallyTranslated()
    {
        int same = s_english.Count(pair => pair.Value == s_german[pair.Key]);

        // Some words are the same in both languages (Sound, Apps, Neon, Name, Emoji, Link, Wind) and some keys are
        // pure formats ("{0} (%)"). Everything else differs.
        same.Should().BeLessThan(s_english.Count / 10);
        s_german["Tray.Quit"].Should().Be("Beenden");
    }

    [Fact]
    public void EveryKeyTheCodeUses_Exists()
    {
        var missing = new List<string>();
        foreach (string file in Repo.ProductSourceFiles().Where(file => file.EndsWith(".cs", StringComparison.Ordinal)))
        {
            foreach (Match match in KeyPattern().Matches(File.ReadAllText(Repo.PathOf(file))))
            {
                string key = match.Groups["key"].Value;
                if (!s_english.ContainsKey(key))
                {
                    missing.Add($"{file}: {key}");
                }
            }
        }

        missing.Should().BeEmpty("every Texts.Get/Format key needs an English and a German text");
    }

    [Fact]
    public void KeysBuiltFromEnums_Exist()
    {
        string[] expected =
        [
            .. Enum.GetValues<VisitorKind>().Select(kind => $"Visitor.{kind}"),
            .. Enum.GetValues<Achievement>().SelectMany(achievement => new[] { $"Achievement.{achievement}", $"Achievement.{achievement}.Text" }),
            .. Enum.GetValues<Rarity>().Select(rarity => $"Rarity.{rarity}"),
            .. Enum.GetValues<WeatherAffinity>().Select(affinity => $"Affinity.{affinity}"),
            .. Core.Tray.TrayMenu.Steps.Select(step => $"Tray.Step.{step.Name}"),
            .. s_directions.Select(direction => $"Direction.{direction}"),
        ];

        expected.Where(key => !s_english.ContainsKey(key)).Should().BeEmpty();
    }

    [Fact]
    public void Get_UnknownKey_ShowsTheKey()
    {
        Texts.Get("No.Such.Key").Should().Be("No.Such.Key", "a missing text is visible instead of silently empty");
    }

    [Fact]
    public void Read_ParsesTheResxFormat()
    {
        const string Resw = """
            <root>
              <data name="A" xml:space="preserve"><value>One &amp; two</value></data>
              <data name="B"><value>{0} left</value><comment>ignored</comment></data>
            </root>
            """;

        Dictionary<string, string> texts = Texts.Read(new StringReader(Resw));

        texts.Should().BeEquivalentTo(new Dictionary<string, string> { ["A"] = "One & two", ["B"] = "{0} left" });
    }

    private static string[] Placeholders(string text) =>
        [.. PlaceholderPattern().Matches(text).Select(match => match.Groups[1].Value).Distinct().Order()];

    [GeneratedRegex(@"\{(\d+)[^}]*\}")]
    private static partial Regex PlaceholderPattern();

    [GeneratedRegex(@"Texts\.(?:Get|Format)\(\s*""(?<key>[A-Za-z][A-Za-z0-9.]*)""")]
    private static partial Regex KeyPattern();
}
