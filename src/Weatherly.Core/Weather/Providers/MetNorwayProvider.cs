using System.Globalization;
using System.Net;
using System.Text.Json;

namespace Weatherly.Core.Weather.Providers;

/// <summary>
/// The weather of MET Norway (PLAN.md, section 5): free for commercial use under CC BY 4.0 with credit. Their terms
/// ask for an identifying User-Agent, for no more than four decimals and for honouring <c>Expires</c> and
/// <c>If-Modified-Since</c>; we send one decimal (<see cref="GeoPoint.SentDecimals"/>).
/// </summary>
public sealed class MetNorwayProvider : IWeatherProvider, IDisposable
{
    public const string Endpoint = "https://api.met.no/weatherapi/locationforecast/2.0/compact";

    private static readonly TimeSpan s_timeout = TimeSpan.FromSeconds(20);

    private readonly HttpClient _client;

    /// <param name="handler">Message handler for tests; <see langword="null"/> uses the system's network stack.</param>
    public MetNorwayProvider(HttpMessageHandler? handler = null)
    {
        _client = handler is null ? new HttpClient() : new HttpClient(handler, disposeHandler: false);
        _client.Timeout = s_timeout;
        _client.DefaultRequestHeaders.TryAddWithoutValidation("User-Agent", UserAgent);
    }

    /// <summary>Identifies the app and where to reach its makers, as MET Norway's terms require. Nothing about the user.</summary>
    public static string UserAgent => $"{AppInfo.Name}/{AppInfo.DisplayVersion} github.com/TeslaV0D/weatherly_windows";

    public string Attribution => "Weather data: MET Norway (CC BY 4.0)";

    /// <summary>The request address for <paramref name="location"/>, already rounded.</summary>
    public static Uri RequestUri(GeoPoint location)
    {
        GeoPoint rounded = location.RoundedForSending();
        return new Uri(string.Create(CultureInfo.InvariantCulture, $"{Endpoint}?lat={rounded.Latitude:0.0}&lon={rounded.Longitude:0.0}"));
    }

    public async Task<WeatherFetch> FetchAsync(GeoPoint location, WeatherFetch? previous, CancellationToken cancellationToken)
    {
        if (!location.IsValid)
        {
            return WeatherFetch.Failure("No valid location.");
        }

        try
        {
            using var request = new HttpRequestMessage(HttpMethod.Get, RequestUri(location));
            if (previous is { Forecast: not null, LastModified: { } lastModified })
            {
                request.Headers.IfModifiedSince = lastModified;
            }

            using HttpResponseMessage response = await _client.SendAsync(request, cancellationToken).ConfigureAwait(false);
            DateTimeOffset? expires = response.Content.Headers.Expires;
            if (response.StatusCode == HttpStatusCode.NotModified && previous?.Forecast is not null)
            {
                return new WeatherFetch(WeatherFetchStatus.NotModified, previous.Forecast, expires, previous.LastModified, null);
            }

            if (!response.IsSuccessStatusCode)
            {
                return WeatherFetch.Failure(string.Create(CultureInfo.InvariantCulture, $"The weather service answered {(int)response.StatusCode}."));
            }

            string json = await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
            WeatherForecast forecast = MetNorwayParser.Parse(json);
            return new WeatherFetch(WeatherFetchStatus.Updated, forecast, expires, response.Content.Headers.LastModified, null);
        }
        catch (HttpRequestException)
        {
            return WeatherFetch.Failure("No connection to the weather service.");
        }
        catch (TaskCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            return WeatherFetch.Failure("The weather service did not answer in time.");
        }
        catch (JsonException)
        {
            return WeatherFetch.Failure("The weather service sent something unexpected.");
        }
    }

    public void Dispose() => _client.Dispose();
}
