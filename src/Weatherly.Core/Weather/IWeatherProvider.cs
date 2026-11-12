namespace Weatherly.Core.Weather;

/// <summary>What a request to the weather service brought.</summary>
public enum WeatherFetchStatus
{
    /// <summary>A new forecast.</summary>
    Updated,

    /// <summary>The service has nothing newer than the forecast of the previous request.</summary>
    NotModified,

    /// <summary>No answer or an unusable one; <see cref="WeatherFetch.Error"/> says why.</summary>
    Failed,
}

/// <param name="Status">What happened.</param>
/// <param name="Forecast">The forecast (the previous one for <see cref="WeatherFetchStatus.NotModified"/>), <see langword="null"/> on failure.</param>
/// <param name="Expires">Until when the service asks not to be asked again.</param>
/// <param name="LastModified">Version of the forecast, sent back with the next request so an unchanged one is not transferred.</param>
/// <param name="Error">Short reason of a failure, for the status line.</param>
public sealed record WeatherFetch(
    WeatherFetchStatus Status,
    WeatherForecast? Forecast,
    DateTimeOffset? Expires,
    DateTimeOffset? LastModified,
    string? Error)
{
    public static WeatherFetch Failure(string error) => new(WeatherFetchStatus.Failed, null, null, null, error);
}

/// <summary>
/// A weather service (PLAN.md, section 5). Implementations live in <c>Weather/Providers</c>, the only place that may
/// talk to the network (rule 4 of the project rules, enforced by <c>PrivacyInvariantTests</c>).
/// </summary>
public interface IWeatherProvider
{
    /// <summary>The credit the service asks for, shown in the sync settings and the about page.</summary>
    string Attribution { get; }

    /// <summary>
    /// Fetches the forecast for <paramref name="location"/>. The provider rounds the location to
    /// <see cref="GeoPoint.SentDecimals"/> itself before anything is sent. With <paramref name="previous"/> it asks only for
    /// changes. Never throws for network or service errors: they come back as <see cref="WeatherFetchStatus.Failed"/>.
    /// </summary>
    Task<WeatherFetch> FetchAsync(GeoPoint location, WeatherFetch? previous, CancellationToken cancellationToken);
}
