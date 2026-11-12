using Sillfall.Core.Weather;

namespace Sillfall.Core.Tests.Weather;

public class SolarPositionTests
{
    private static readonly GeoPoint s_berlin = new(52.52, 13.41);

    [Fact]
    public void Berlin_SummerNoon_IsDay_WinterMidnight_IsNight()
    {
        // Local noon in Berlin is about 11:06 UTC; at the June solstice the sun stands about 61° high.
        SolarPosition.ElevationDegrees(s_berlin, new DateTimeOffset(2026, 6, 21, 11, 6, 0, TimeSpan.Zero)).Should().BeApproximately(60.9, 1.0);
        SolarPosition.IsDay(s_berlin, new DateTimeOffset(2026, 12, 21, 23, 0, 0, TimeSpan.Zero)).Should().BeFalse();
    }

    [Fact]
    public void Berlin_DecemberMorning_TurnsDayAroundSunrise()
    {
        // Sunrise in Berlin on 21 December is about 08:15 UTC+1, so 07:15 UTC.
        SolarPosition.IsDay(s_berlin, new DateTimeOffset(2026, 12, 21, 6, 45, 0, TimeSpan.Zero)).Should().BeFalse();
        SolarPosition.IsDay(s_berlin, new DateTimeOffset(2026, 12, 21, 7, 45, 0, TimeSpan.Zero)).Should().BeTrue();
    }

    [Fact]
    public void Tromso_PolarNight_StaysDarkAtNoon()
    {
        SolarPosition.IsDay(new GeoPoint(69.65, 18.96), new DateTimeOffset(2026, 12, 21, 10, 45, 0, TimeSpan.Zero)).Should().BeFalse();
    }

    [Fact]
    public void Sydney_JanuaryNoon_IsDay_SouthernSummer()
    {
        // Local noon in Sydney is about 02:00 UTC; in January the sun stands about 80° high.
        SolarPosition.ElevationDegrees(new GeoPoint(-33.87, 151.21), new DateTimeOffset(2026, 1, 10, 2, 0, 0, TimeSpan.Zero))
            .Should().BeGreaterThan(70);
    }

    [Fact]
    public void Equator_EquinoxNoon_SunNearlyOverhead()
    {
        SolarPosition.ElevationDegrees(new GeoPoint(0, 0), new DateTimeOffset(2026, 3, 20, 12, 7, 0, TimeSpan.Zero)).Should().BeGreaterThan(88);
    }
}
