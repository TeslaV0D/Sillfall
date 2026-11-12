using Weatherly.Core.Weather;

namespace Weatherly.Core.Tests.Weather;

/// <summary>One test per row of the mapping table (PLAN.md, section 5; docs/weather-mapping.md).</summary>
public class WeatherMapperTests
{
    private static readonly GeoPoint s_berlin = new(52.5, 13.4);
    private static readonly GeoPoint s_sydney = new(-33.9, 151.2);

    /// <summary>Midday in Berlin (11:00 UTC), so the sun is up unless a test says otherwise.</summary>
    private static readonly DateTimeOffset s_julyNoon = new(2026, 7, 15, 11, 0, 0, TimeSpan.Zero);
    private static readonly DateTimeOffset s_julyMidnight = new(2026, 7, 15, 23, 0, 0, TimeSpan.Zero);

    [Theory]
    [InlineData(0f, 0f)]
    [InlineData(0.03f, 0f)]
    [InlineData(0.1f, 0.15f)]
    [InlineData(8f, 1f)]
    [InlineData(50f, 1f)]
    public void PrecipitationIntensity_FollowsTheLogScale(float millimetresPerHour, float expected)
    {
        WeatherMapper.PrecipitationIntensity(millimetresPerHour).Should().BeApproximately(expected, 1e-4f);
    }

    [Fact]
    public void PrecipitationIntensity_GrowsWithTheAmount()
    {
        float light = WeatherMapper.PrecipitationIntensity(0.5f);
        float moderate = WeatherMapper.PrecipitationIntensity(2f);
        float heavy = WeatherMapper.PrecipitationIntensity(5f);

        light.Should().BeGreaterThan(0.15f).And.BeLessThan(moderate);
        moderate.Should().BeLessThan(heavy);
        heavy.Should().BeLessThan(1f);
    }

    [Fact]
    public void Rain_ComesFromTheAmountAndTheSymbol()
    {
        WeatherTargets targets = Map(Weather() with { Symbol = "rain", PrecipitationRate = 2f, AirTemperature = 12f });

        targets.Rain.Should().BeApproximately(WeatherMapper.PrecipitationIntensity(2f), 1e-4f);
        targets.Snow.Should().Be(0f);
        targets.Sun.Should().Be(0f, "no sunshine while it rains");
    }

    [Fact]
    public void Showers_WithoutAnAmount_StillRain()
    {
        // MET often reports 0.0 mm for shower symbols: the symbol stands for at least a little rain.
        Map(Weather() with { Symbol = "lightrainshowers_day", PrecipitationRate = 0f }).Rain.Should().BeGreaterThan(0.15f);
        Map(Weather() with { Symbol = "heavyrainandthunder", PrecipitationRate = 0f }).Rain.Should().BeGreaterThan(0.8f);
    }

    [Fact]
    public void Snow_FromTheSnowSymbol_EvenAboveFreezing()
    {
        WeatherTargets targets = Map(Weather() with { Symbol = "snow", PrecipitationRate = 1f, AirTemperature = 1.5f });

        targets.Snow.Should().BeGreaterThan(0.5f);
        targets.Rain.Should().Be(0f);
    }

    [Fact]
    public void Sleet_IsHalfRainHalfSnow()
    {
        WeatherTargets targets = Map(Weather() with { Symbol = "sleet", PrecipitationRate = 2f, AirTemperature = 1f });

        targets.Rain.Should().BeApproximately(targets.Snow, 1e-4f);
        (targets.Rain + targets.Snow).Should().BeApproximately(WeatherMapper.PrecipitationIntensity(2f), 1e-4f);
    }

    [Theory]
    [InlineData(-3f, true)]
    [InlineData(1f, true)]
    [InlineData(3f, false)]
    public void PrecipitationWithoutASymbol_IsSnowOnlyInFrost(float temperature, bool snow)
    {
        WeatherTargets targets = Map(Weather() with { Symbol = string.Empty, PrecipitationRate = 1f, AirTemperature = temperature });

        (targets.Snow > 0f).Should().Be(snow);
        (targets.Rain > 0f).Should().Be(!snow);
    }

    [Theory]
    [InlineData(-5f, true)]
    [InlineData(2f, true)]
    [InlineData(2.5f, false)]
    public void LyingSnow_StaysInFrost_MeltsAboveTwoDegrees(float temperature, bool stays)
    {
        Map(Weather() with { AirTemperature = temperature }).SnowStays.Should().Be(stays);
    }

    [Fact]
    public void FogSymbol_IsThickFog()
    {
        WeatherTargets targets = Map(Weather() with { Symbol = "fog" });

        targets.Fog.Should().BeApproximately(0.75f, 1e-4f);
        targets.Sun.Should().Be(0f, "the fog hides the sun");
    }

    [Fact]
    public void HumidCalmOvercastAir_IsMist()
    {
        Map(Weather() with { Humidity = 0.98f, WindSpeed = 1f, CloudFraction = 1f }).Fog.Should().BeApproximately(0.3f, 1e-4f);
        Map(Weather() with { Humidity = 0.98f, WindSpeed = 6f, CloudFraction = 1f }).Fog.Should().Be(0f, "wind blows mist away");
        Map(Weather() with { Humidity = 0.80f, WindSpeed = 1f, CloudFraction = 1f }).Fog.Should().Be(0f);
    }

    [Theory]
    [InlineData(0f, 1f)]
    [InlineData(0.6f, 0.4f)]
    [InlineData(1f, 0f)]
    public void Sun_ByDay_GrowsAsTheCloudsGo(float clouds, float sun)
    {
        Map(Weather() with { CloudFraction = clouds }).Sun.Should().BeApproximately(sun, 1e-4f);
    }

    [Fact]
    public void Sun_AtNight_IsOff()
    {
        WeatherTargets targets = Map(Weather() with { Time = s_julyMidnight, CloudFraction = 0f });

        targets.Sun.Should().Be(0f);
        targets.IsDay.Should().BeFalse();
    }

    [Theory]
    [InlineData(270f, 15f, 400f)] // west wind blows to the right (east)
    [InlineData(90f, 15f, -400f)] // east wind blows to the left
    [InlineData(270f, 7.5f, 200f)]
    [InlineData(270f, 40f, 400f)] // a storm does not leave the slider's range
    [InlineData(0f, 10f, 0f)] // north wind blows into the screen: no sideways part
    public void Wind_IsProjectedOnTheScreensXAxis(float from, float speed, float pixels)
    {
        Map(Weather() with { WindFromDirection = from, WindSpeed = speed }).WindSpeed.Should().BeApproximately(pixels, 0.5f);
    }

    [Fact]
    public void Gustiness_GrowsWithTheWind()
    {
        Map(Weather() with { WindSpeed = 0f }).Gustiness.Should().BeApproximately(0.3f, 1e-4f);
        Map(Weather() with { WindSpeed = 20f }).Gustiness.Should().Be(1f);
    }

    [Fact]
    public void Leaves_BlowInAutumnWind_OnBothHemispheres()
    {
        var october = new DateTimeOffset(2026, 10, 15, 11, 0, 0, TimeSpan.Zero);
        var april = new DateTimeOffset(2026, 4, 15, 2, 0, 0, TimeSpan.Zero);

        Map(Weather() with { Time = october, WindSpeed = 8f }).Leaves.Should().BeApproximately(0.7f, 1e-4f);
        Map(Weather() with { Time = october, WindSpeed = 3f }).Leaves.Should().Be(0f, "too calm");
        Map(Weather() with { Time = s_julyNoon, WindSpeed = 8f }).Leaves.Should().Be(0f, "summer");
        Map(Weather() with { Time = october, WindSpeed = 8f }, s_sydney).Leaves.Should().Be(0f, "spring in the south");
        Map(Weather() with { Time = april, WindSpeed = 8f }, s_sydney).Leaves.Should().BeGreaterThan(0f, "autumn in the south");
    }

    [Fact]
    public void Invert_SunnyDay_BecomesRain()
    {
        WeatherTargets targets = Map(Weather() with { CloudFraction = 0f, AirTemperature = 10f }, invert: true);

        targets.Rain.Should().BeApproximately(1f, 1e-4f);
        targets.Sun.Should().Be(0f);
        targets.Snow.Should().Be(0f);
    }

    [Fact]
    public void Invert_RainyDay_BecomesSunshine()
    {
        WeatherTargets targets = Map(Weather() with { Symbol = "heavyrain", PrecipitationRate = 8f, CloudFraction = 1f, AirTemperature = 12f }, invert: true);

        targets.Sun.Should().BeApproximately(1f, 1e-4f);
        targets.Rain.Should().Be(0f, "an overcast sky has no clear part to turn into rain");
    }

    [Fact]
    public void Invert_Heat_BringsSnow_AndFrostMeltsIt()
    {
        WeatherTargets hot = Map(Weather() with { AirTemperature = 30f, CloudFraction = 0f }, invert: true);
        WeatherTargets cold = Map(Weather() with { AirTemperature = -5f }, invert: true);

        hot.Snow.Should().BeApproximately(1f, 1e-4f);
        hot.Rain.Should().Be(0f, "the heat makes it snow instead");
        hot.SnowStays.Should().BeTrue();
        cold.SnowStays.Should().BeFalse();
    }

    [Fact]
    public void Invert_LiftsTheFog_AndKeepsTheWind()
    {
        WeatherSnapshot weather = Weather() with { Symbol = "fog", WindFromDirection = 270f, WindSpeed = 6f };

        WeatherTargets inverted = Map(weather, invert: true);

        inverted.Fog.Should().Be(0f);
        inverted.WindSpeed.Should().BeApproximately(Map(weather).WindSpeed, 1e-4f);
    }

    [Fact]
    public void Map_IsDeterministic()
    {
        WeatherSnapshot weather = Weather() with { Symbol = "lightsnowshowers_day", PrecipitationRate = 0.7f, WindSpeed = 5f };

        Map(weather).Should().Be(Map(weather));
    }

    private static WeatherSnapshot Weather() => new()
    {
        Time = s_julyNoon,
        AirTemperature = 18f,
        CloudFraction = 0.5f,
        Humidity = 0.6f,
        WindSpeed = 2f,
        WindFromDirection = 270f,
        PrecipitationRate = 0f,
        Symbol = "partlycloudy_day",
    };

    private static WeatherTargets Map(WeatherSnapshot weather, GeoPoint? location = null, bool invert = false) =>
        WeatherMapper.Map(weather, location ?? s_berlin, invert);
}
