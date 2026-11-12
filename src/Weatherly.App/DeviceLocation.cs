using System.Runtime.InteropServices;
using Weatherly.Core.Weather;
using Windows.Devices.Geolocation;

namespace Weatherly.App;

/// <summary>
/// The PC's location from the Windows location service, for the weather sync (PLAN.md, section 5). Windows asks the
/// user for permission; the result is rounded to <see cref="GeoPoint.SentDecimals"/> right away and kept in memory only.
/// </summary>
internal static class DeviceLocation
{
    /// <summary>A rough fix is enough: the weather is the same a few kilometres away, and it saves energy.</summary>
    private const uint AccuracyInMeters = 5000;

    private static readonly TimeSpan s_maximumAge = TimeSpan.FromHours(1);
    private static readonly TimeSpan s_timeout = TimeSpan.FromSeconds(20);

    public static async Task<(GeoPoint? Location, string? Error)> GetAsync()
    {
        try
        {
            GeolocationAccessStatus access = await Geolocator.RequestAccessAsync();
            if (access != GeolocationAccessStatus.Allowed)
            {
                return (null, "Windows does not let the app use the location. Allow it under Settings > Privacy & security > Location, or choose a city.");
            }

            var locator = new Geolocator { DesiredAccuracyInMeters = AccuracyInMeters };
            Geoposition position = await locator.GetGeopositionAsync(s_maximumAge, s_timeout);
            BasicGeoposition point = position.Coordinate.Point.Position;
            return (new GeoPoint(point.Latitude, point.Longitude).RoundedForSending(), null);
        }
        catch (Exception exception) when (exception is UnauthorizedAccessException or COMException or InvalidOperationException or TaskCanceledException)
        {
            return (null, "The location service did not answer. Try again later or choose a city.");
        }
    }
}
