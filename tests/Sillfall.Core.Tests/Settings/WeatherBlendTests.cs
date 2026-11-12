using Sillfall.Core.Overlay;
using Sillfall.Core.Settings;
using Sillfall.Core.Weather;

namespace Sillfall.Core.Tests.Settings;

public class WeatherBlendTests
{
    private static readonly WeatherTargets s_weather = new()
    {
        Rain = 0.8f,
        Snow = 0f,
        Leaves = 0.4f,
        Fog = 0.2f,
        Sun = 0f,
        WindSpeed = 200f,
        Gustiness = 0.9f,
        SnowStays = true,
    };

    [Fact]
    public void Apply_FullWeight_TakesTheWeather()
    {
        (float[] intensities, float wind, float gusts) = Blend(new WeatherSyncConfig { Enabled = true });

        intensities.Should().Equal(0.8f, 0f, 0.4f, 0.2f, 0f, 0.5f);
        wind.Should().Be(200f);
        gusts.Should().Be(0.9f);
    }

    [Fact]
    public void Apply_CustomWeight_MixesLinearly()
    {
        WeatherSyncConfig sync = new WeatherSyncConfig { Enabled = true }.WithWeight(SyncElement.Rain, 0.5f).WithWeight(SyncElement.Wind, 0.25f);

        (float[] intensities, float wind, _) = Blend(sync);

        intensities[(int)EffectKind.Rain].Should().BeApproximately(0.5f * (0.2f + 0.8f), 1e-5f, "lerp(manual 0.2, synced 0.8, 0.5)");
        wind.Should().BeApproximately(-100f + ((200f + 100f) * 0.25f), 1e-3f);
    }

    [Fact]
    public void Apply_ZeroWeight_KeepsTheManualValue()
    {
        (float[] intensities, _, _) = Blend(new WeatherSyncConfig { Enabled = true }.WithWeight(SyncElement.Leaves, 0f));

        intensities[(int)EffectKind.Leaves].Should().Be(0.2f);
    }

    [Fact]
    public void Apply_PausedElement_KeepsTheManualValue_UntilResumed()
    {
        WeatherSyncConfig sync = new WeatherSyncConfig { Enabled = true }.WithPaused(SyncElement.Rain);

        Blend(sync).Intensities[(int)EffectKind.Rain].Should().Be(0.2f, "the user moved the rain slider");
        Blend(sync.Resumed()).Intensities[(int)EffectKind.Rain].Should().Be(0.8f);
    }

    [Fact]
    public void Apply_SyncOffOrNoForecast_ChangesNothing()
    {
        Blend(new WeatherSyncConfig { Enabled = false }).Intensities.Should().Equal(0.2f, 0.2f, 0.2f, 0.2f, 0.2f, 0.5f);
        Blend(new WeatherSyncConfig { Enabled = true }, weather: null).Intensities.Should().Equal(0.2f, 0.2f, 0.2f, 0.2f, 0.2f, 0.5f);
    }

    [Fact]
    public void Apply_NeverTouchesGrass()
    {
        Blend(new WeatherSyncConfig { Enabled = true }).Intensities[(int)EffectKind.Grass].Should().Be(0.5f);
    }

    [Fact]
    public void ElementOf_And_EffectOf_AreInverse()
    {
        foreach (SyncElement element in Enum.GetValues<SyncElement>().Where(element => element != SyncElement.Wind))
        {
            WeatherBlend.ElementOf(WeatherBlend.EffectOf(element)!.Value).Should().Be(element);
        }

        WeatherBlend.ElementOf(EffectKind.Grass).Should().BeNull();
        WeatherBlend.EffectOf(SyncElement.Wind).Should().BeNull();
    }

    [Fact]
    public void RuntimeOptions_WithWeather_SetsIntensitiesWindAndFrost()
    {
        var settings = new AppSettings { WeatherSync = new WeatherSyncConfig { Enabled = true } };
        float[] manual = [0f, 0f, 0f, 0f, 0f, 0f];

        OverlayOptions options = RuntimeOptions.Build(settings, new OverlayOptions(), manual, new RuntimeDecision(false, null, 60, 0.5f), s_weather);

        options.Rain.Intensity.Should().BeApproximately(0.4f, 1e-5f, "the battery factor applies after the mixing");
        options.Wind.X.Should().Be(200f);
        options.Gustiness.Should().Be(0.9f);
        options.SnowStays.Should().BeTrue();
    }

    [Fact]
    public void RuntimeOptions_SnowPausedByHand_LetsTheSnowMelt()
    {
        var settings = new AppSettings { WeatherSync = new WeatherSyncConfig { Enabled = true }.WithPaused(SyncElement.Snow) };

        OverlayOptions options = RuntimeOptions.Build(settings, new OverlayOptions(), new float[6], new RuntimeDecision(false, null, 60, 1f), s_weather);

        options.SnowStays.Should().BeFalse();
    }

    [Fact]
    public void Sanitized_RepairsTheSyncSettings()
    {
        var sync = new WeatherSyncConfig
        {
            Source = (LocationSource)42,
            City = new CityChoice { Name = "  ", Latitude = 200 },
            Weights = new() { [SyncElement.Rain] = 3f, [(SyncElement)99] = 0.5f, [SyncElement.Fog] = float.NaN },
            Paused = [SyncElement.Sun, SyncElement.Sun, (SyncElement)77],
        };

        WeatherSyncConfig clean = new AppSettings { WeatherSync = sync }.Sanitized().WeatherSync;

        clean.Source.Should().Be(LocationSource.City);
        clean.City.Should().BeNull();
        clean.Weights.Should().HaveCount(2);
        clean.Weight(SyncElement.Rain).Should().Be(1f);
        clean.Weight(SyncElement.Fog).Should().Be(1f);
        clean.Paused.Should().Equal(SyncElement.Sun);
    }

    private static (float[] Intensities, float Wind, float Gusts) Blend(WeatherSyncConfig sync) => Blend(sync, s_weather);

    private static (float[] Intensities, float Wind, float Gusts) Blend(WeatherSyncConfig sync, WeatherTargets? weather)
    {
        float[] intensities = [0.2f, 0.2f, 0.2f, 0.2f, 0.2f, 0.5f];
        float wind = -100f;
        float gusts = 0.5f;
        WeatherBlend.Apply(sync, weather, intensities, ref wind, ref gusts);
        return (intensities, wind, gusts);
    }
}
