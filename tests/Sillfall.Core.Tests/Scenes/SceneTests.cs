using Sillfall.Core.Scenes;
using Sillfall.Core.Settings;

namespace Sillfall.Core.Tests.Scenes;

public class SceneTests
{
    private static readonly Scene s_scene = new()
    {
        Name = "Rainy afternoon",
        Emoji = "🌧️",
        Rain = new EffectConfig { Intensity = 0.6f, Color = "#AABBCC" },
        Fog = new EffectConfig { Intensity = 0.2f, Mode = IntensityMode.Random, RandomMin = 0.1f, RandomMax = 0.4f },
        RainDepth = 0.7f,
        Wind = new WindConfig { Speed = 80f, Gustiness = 0.6f },
    };

    [Fact]
    public void Json_RoundTrips()
    {
        string json = SceneCodec.ToJson(s_scene);

        SceneCodec.TryFromJson(json, out Scene scene, out string? error).Should().BeTrue(error);
        scene.Should().BeEquivalentTo(s_scene.Sanitized());
        json.Should().Contain("\"format\": \"sillfall-scene\"");
    }

    [Fact]
    public void Link_RoundTripsAndStaysShort()
    {
        string link = SceneCodec.ToLink(s_scene);

        link.Should().StartWith(SceneCodec.LinkPrefix);
        link.Length.Should().BeLessThan(700);
        link[SceneCodec.LinkPrefix.Length..].Should().NotContainAny(["+", "/", "="], "the data is Base64url without padding");
        SceneCodec.TryFromLink(link, out Scene scene, out string? error).Should().BeTrue(error);
        scene.Should().BeEquivalentTo(s_scene.Sanitized());
        SceneCodec.TryFromLink(link + "&utm=x", out _, out _).Should().BeTrue("extra query parameters are ignored");
    }

    [Theory]
    [InlineData("")]
    [InlineData("https://example.com/scene?d=abc")]
    [InlineData("sillfall://scene?d=!!!")]
    [InlineData("sillfall://scene?d=AAAA")]
    public void Link_RejectsAnythingElse(string link)
    {
        SceneCodec.TryFromLink(link, out _, out string? error).Should().BeFalse();
        error.Should().NotBeNullOrEmpty();
    }

    [Theory]
    [InlineData("{ }")]
    [InlineData("{ \"format\": \"something-else\", \"scene\": { } }")]
    [InlineData("[1, 2, 3]")]
    [InlineData("not json")]
    [InlineData("{ \"format\": \"sillfall-scene\", \"version\": 99, \"scene\": { } }")]
    public void Json_RejectsWhatIsNotAScene(string json)
    {
        SceneCodec.TryFromJson(json, out _, out string? error).Should().BeFalse();
        error.Should().NotBeNullOrEmpty();
    }

    [Fact]
    public void Json_TooLarge_IsRejected()
    {
        string json = "{ \"format\": \"sillfall-scene\", \"scene\": { \"name\": \"" + new string('x', SceneCodec.MaxSize) + "\" } }";

        SceneCodec.TryFromJson(json, out _, out _).Should().BeFalse();
    }

    [Fact]
    public void ALinkThatInflatesTooMuch_IsRejected()
    {
        // A small link that decompresses to a lot: a deflate bomb must not get through.
        string huge = "{ \"format\": \"sillfall-scene\", \"scene\": { \"name\": \"" + new string('a', 200_000) + "\" } }";
        using var compressed = new MemoryStream();
        using (var deflate = new System.IO.Compression.DeflateStream(compressed, System.IO.Compression.CompressionLevel.SmallestSize))
        {
            deflate.Write(System.Text.Encoding.UTF8.GetBytes(huge));
        }

        string link = SceneCodec.LinkPrefix + Convert.ToBase64String(compressed.ToArray()).TrimEnd('=').Replace('+', '-').Replace('/', '_');

        link.Length.Should().BeLessThan(SceneCodec.MaxSize);
        SceneCodec.TryFromLink(link, out _, out string? error).Should().BeFalse();
        error.Should().Contain("too much");
    }

    [Fact]
    public void Sanitized_CleansNamesAndValuesFromStrangers()
    {
        var scene = new Scene
        {
            Name = "Line\r\nbreak" + new string('x', 100),
            Emoji = "\u0007",
            Rain = new EffectConfig { Intensity = 5f },
            Wind = null!,
        };

        Scene clean = scene.Sanitized();

        clean.Name.Should().NotContainAny("\r", "\n");
        clean.Name.Length.Should().Be(Scene.MaxNameLength);
        clean.Emoji.Should().Be("🌦️");
        clean.Rain.Intensity.Should().Be(1f);
        clean.Wind.Should().NotBeNull();
    }

    [Fact]
    public void FromAndApplyTo_SwapOnlyTheWeather()
    {
        var settings = new AppSettings
        {
            Snow = new EffectConfig { Intensity = 1f },
            Sound = new SoundConfig { Muted = false },
            AppRules = [new AppRule { ProcessName = "game.exe" }],
        };

        AppSettings applied = s_scene.ApplyTo(settings);

        applied.Rain.Intensity.Should().Be(0.6f);
        applied.Snow.Intensity.Should().Be(0f, "the scene has no snow");
        applied.Sound.Muted.Should().BeFalse("sound is not part of a scene");
        applied.AppRules.Should().ContainSingle();
        Scene.From(applied, s_scene.Name, s_scene.Emoji).Should().BeEquivalentTo(s_scene.Sanitized());
    }

    [Fact]
    public void Presets_AreValidAndDistinct()
    {
        ScenePresets.All.Should().HaveCountGreaterThanOrEqualTo(4);
        ScenePresets.All.Select(scene => scene.Name).Should().OnlyHaveUniqueItems();
        foreach (Scene preset in ScenePresets.All)
        {
            SceneCodec.TryFromLink(SceneCodec.ToLink(preset), out Scene shared, out string? error).Should().BeTrue(error);
            shared.Should().BeEquivalentTo(preset.Sanitized(), $"{preset.Name} survives sharing");
        }
    }
}
