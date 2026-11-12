using Weatherly.Core.Weather;

namespace Weatherly.Core.Tests.Weather;

public class GeoPointTests
{
    [Theory]
    [InlineData(52.520008, 13.404954, 52.5, 13.4)]
    [InlineData(48.137154, 11.576124, 48.1, 11.6)]
    [InlineData(-33.868820, 151.209290, -33.9, 151.2)]
    [InlineData(0.05, -0.05, 0.1, -0.1)]
    public void RoundedForSending_KeepsOneDecimal(double latitude, double longitude, double expectedLatitude, double expectedLongitude)
    {
        GeoPoint rounded = new GeoPoint(latitude, longitude).RoundedForSending();

        rounded.Latitude.Should().Be(expectedLatitude);
        rounded.Longitude.Should().Be(expectedLongitude);
    }

    [Theory]
    [InlineData(0, 0, true)]
    [InlineData(90, 180, true)]
    [InlineData(-90.1, 0, false)]
    [InlineData(0, 180.5, false)]
    [InlineData(double.NaN, 0, false)]
    public void IsValid_ChecksTheRange(double latitude, double longitude, bool valid)
    {
        new GeoPoint(latitude, longitude).IsValid.Should().Be(valid);
    }
}
