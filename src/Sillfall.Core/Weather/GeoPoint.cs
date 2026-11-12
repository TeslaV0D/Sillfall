using System.Globalization;

namespace Sillfall.Core.Weather;

/// <summary>A place on Earth in degrees (WGS 84): latitude positive to the north, longitude positive to the east.</summary>
public readonly record struct GeoPoint(double Latitude, double Longitude)
{
    /// <summary>
    /// Decimals a location keeps when it leaves the PC: one, about 11 km (PLAN.md, section 5). The weather of a
    /// city district is the same, the street is not revealed.
    /// </summary>
    public const int SentDecimals = 1;

    public bool IsValid => double.IsFinite(Latitude) && double.IsFinite(Longitude)
        && Math.Abs(Latitude) <= 90 && Math.Abs(Longitude) <= 180;

    /// <summary>The location as it may be sent to a weather service: rounded to <see cref="SentDecimals"/>.</summary>
    public GeoPoint RoundedForSending() => new(
        Math.Round(Latitude, SentDecimals, MidpointRounding.AwayFromZero),
        Math.Round(Longitude, SentDecimals, MidpointRounding.AwayFromZero));

    /// <summary>Southern hemisphere: the seasons are turned around.</summary>
    public bool IsSouthern => Latitude < 0;

    public override string ToString() => string.Create(CultureInfo.InvariantCulture, $"{Latitude:0.0####}, {Longitude:0.0####}");
}
