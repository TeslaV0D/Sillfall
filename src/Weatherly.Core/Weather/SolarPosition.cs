namespace Weatherly.Core.Weather;

/// <summary>
/// Height of the sun over the horizon, from the usual low-precision formulas of the astronomical almanac (good to
/// about a degree, plenty for day or night). The compact forecast has no day flag, so the sync computes it.
/// </summary>
public static class SolarPosition
{
    /// <summary>The sun counts as up from this height on: its upper edge plus refraction (sunrise and sunset).</summary>
    public const double HorizonDegrees = -0.833;

    private static readonly DateTimeOffset s_j2000 = new(2000, 1, 1, 12, 0, 0, TimeSpan.Zero);

    public static double ElevationDegrees(GeoPoint location, DateTimeOffset time)
    {
        double days = (time.ToUniversalTime() - s_j2000).TotalDays;
        double meanAnomaly = Radians(357.529 + (0.98560028 * days));
        double meanLongitude = 280.459 + (0.98564736 * days);
        double eclipticLongitude = Radians(meanLongitude + (1.915 * Math.Sin(meanAnomaly)) + (0.020 * Math.Sin(2 * meanAnomaly)));
        double obliquity = Radians(23.439 - (0.00000036 * days));

        double rightAscension = Math.Atan2(Math.Cos(obliquity) * Math.Sin(eclipticLongitude), Math.Cos(eclipticLongitude));
        double declination = Math.Asin(Math.Sin(obliquity) * Math.Sin(eclipticLongitude));
        double siderealDegrees = (18.697374558 + (24.06570982441908 * days)) * 15.0;
        double hourAngle = Radians(siderealDegrees + location.Longitude) - rightAscension;

        double latitude = Radians(location.Latitude);
        double sine = (Math.Sin(latitude) * Math.Sin(declination)) + (Math.Cos(latitude) * Math.Cos(declination) * Math.Cos(hourAngle));
        return Math.Asin(Math.Clamp(sine, -1.0, 1.0)) * 180.0 / Math.PI;
    }

    public static bool IsDay(GeoPoint location, DateTimeOffset time) => ElevationDegrees(location, time) > HorizonDegrees;

    private static double Radians(double degrees) => degrees * Math.PI / 180.0;
}
