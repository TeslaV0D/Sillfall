using System.Text.Json;
using Weatherly.Core.Weather;
using Weatherly.Core.Weather.Providers;

namespace Weatherly.Core.Tests.Weather;

public class MetNorwayParserTests
{
    [Fact]
    public void Parse_ReadsEveryStepWithItsValues()
    {
        WeatherForecast forecast = MetNorwayParser.Parse(MetNorwaySample.Json);

        forecast.UpdatedAt.Should().Be(new DateTimeOffset(2026, 11, 12, 5, 19, 48, TimeSpan.Zero));
        forecast.Steps.Should().HaveCount(4);
        WeatherSnapshot first = forecast.Steps[0];
        first.Time.Should().Be(MetNorwaySample.FirstStep);
        first.AirTemperature.Should().BeApproximately(3.2f, 1e-4f);
        first.CloudFraction.Should().BeApproximately(1f, 1e-4f, "percent becomes a fraction");
        first.Humidity.Should().BeApproximately(0.968f, 1e-4f);
        first.WindSpeed.Should().BeApproximately(1.7f, 1e-4f);
        first.WindFromDirection.Should().BeApproximately(243.9f, 1e-3f);
        first.Symbol.Should().Be("lightrain", "the next hour comes before the next six hours");
        first.PrecipitationRate.Should().BeApproximately(0.4f, 1e-4f);
    }

    [Fact]
    public void Parse_WithoutTheNextHour_UsesASixthOfTheNextSixHours()
    {
        WeatherSnapshot noon = MetNorwayParser.Parse(MetNorwaySample.Json).Steps[2];

        noon.Symbol.Should().Be("clearsky_day");
        noon.PrecipitationRate.Should().BeApproximately(0.1f, 1e-4f);
    }

    [Fact]
    public void Parse_LateStepsWithoutPeriods_AreCalm()
    {
        WeatherSnapshot last = MetNorwayParser.Parse(MetNorwaySample.Json).Steps[3];

        last.Symbol.Should().BeEmpty();
        last.PrecipitationRate.Should().Be(0f);
    }

    [Theory]
    [InlineData("")]
    [InlineData("{}")]
    [InlineData("""{"properties":{}}""")]
    [InlineData("""{"properties":{"timeseries":[{"time":"tomorrow","data":{"instant":{"details":{}}}}]}}""")]
    [InlineData("<html>maintenance</html>")]
    public void Parse_SomethingElse_Throws(string text)
    {
        Action parse = () => MetNorwayParser.Parse(text);

        parse.Should().Throw<JsonException>();
    }

    [Fact]
    public void At_PicksTheStepThatCoversTheTime()
    {
        WeatherForecast forecast = MetNorwayParser.Parse(MetNorwaySample.Json);

        forecast.At(MetNorwaySample.FirstStep.AddMinutes(30))!.AirTemperature.Should().BeApproximately(3.2f, 1e-4f);
        forecast.At(MetNorwaySample.FirstStep.AddHours(1))!.AirTemperature.Should().BeApproximately(-1.5f, 1e-4f);
        forecast.At(MetNorwaySample.FirstStep.AddHours(-3))!.Time.Should().Be(MetNorwaySample.FirstStep, "before the forecast: its first step");
        forecast.At(MetNorwaySample.FirstStep.AddDays(30))!.AirTemperature.Should().BeApproximately(5.3f, 1e-4f, "after it: its last step");
        new WeatherForecast([], DateTimeOffset.MinValue).At(MetNorwaySample.FirstStep).Should().BeNull();
    }
}
