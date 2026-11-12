namespace Sillfall.Core.Weather;

/// <summary>Where the sync stands, for the status line in the settings.</summary>
public enum WeatherSyncState
{
    /// <summary>No forecast yet.</summary>
    Waiting,

    /// <summary>A recent forecast.</summary>
    Live,

    /// <summary>The last requests failed; the effects follow the last known forecast.</summary>
    Offline,
}

/// <summary>
/// When to ask the weather service and what is known (PLAN.md, section 5: polling). Clock-free, the caller passes the
/// time, so the schedule is tested without waiting:
/// <list type="bullet">
/// <item>After an answer the next request follows the service's <c>Expires</c>, at least
/// <see cref="MinimumInterval"/> and at most <see cref="MaximumInterval"/> later.</item>
/// <item>After a failure it waits 1, 2, 4 … minutes, at most <see cref="MaximumBackoff"/>, and keeps the last forecast.</item>
/// <item>Another location starts over.</item>
/// </list>
/// </summary>
public sealed class WeatherSync
{
    public static readonly TimeSpan MinimumInterval = TimeSpan.FromMinutes(15);
    public static readonly TimeSpan MaximumInterval = TimeSpan.FromMinutes(60);
    public static readonly TimeSpan DefaultInterval = TimeSpan.FromMinutes(30);
    public static readonly TimeSpan FirstBackoff = TimeSpan.FromMinutes(1);
    public static readonly TimeSpan MaximumBackoff = TimeSpan.FromMinutes(30);

    private readonly IWeatherProvider _provider;
    private WeatherFetch? _last;
    private GeoPoint? _location;

    public WeatherSync(IWeatherProvider provider)
    {
        ArgumentNullException.ThrowIfNull(provider);
        _provider = provider;
    }

    public string Attribution => _provider.Attribution;

    /// <summary>The forecast in use (also while offline), <see langword="null"/> before the first answer.</summary>
    public WeatherForecast? Forecast => _last?.Forecast;

    /// <summary>When the service last answered.</summary>
    public DateTimeOffset? LastSuccess { get; private set; }

    /// <summary>When to ask next; <see cref="DateTimeOffset.MinValue"/> means now.</summary>
    public DateTimeOffset NextAttempt { get; private set; } = DateTimeOffset.MinValue;

    /// <summary>Failed requests in a row.</summary>
    public int Failures { get; private set; }

    public string? LastError { get; private set; }

    public WeatherSyncState State => Forecast is null ? WeatherSyncState.Waiting
        : Failures > 0 ? WeatherSyncState.Offline
        : WeatherSyncState.Live;

    public bool IsDue(DateTimeOffset now) => now >= NextAttempt;

    /// <summary>Asks the service for <paramref name="location"/> and plans the next request.</summary>
    public async Task<WeatherSyncState> RefreshAsync(GeoPoint location, DateTimeOffset now, CancellationToken cancellationToken)
    {
        GeoPoint rounded = location.RoundedForSending();
        if (_location != rounded)
        {
            Reset();
            _location = rounded;
        }

        WeatherFetch fetch = await _provider.FetchAsync(rounded, _last, cancellationToken).ConfigureAwait(false);
        if (fetch.Status == WeatherFetchStatus.Failed || fetch.Forecast is null)
        {
            Failures++;
            LastError = fetch.Error ?? "No forecast.";
            NextAttempt = now + Backoff(Failures);
            return State;
        }

        _last = fetch;
        Failures = 0;
        LastError = null;
        LastSuccess = now;
        TimeSpan interval = fetch.Expires is { } expires ? expires - now : DefaultInterval;
        NextAttempt = now + (interval < MinimumInterval ? MinimumInterval : interval > MaximumInterval ? MaximumInterval : interval);
        return State;
    }

    /// <summary>Forgets everything (sync switched off, location changed).</summary>
    public void Reset()
    {
        _last = null;
        _location = null;
        LastSuccess = null;
        NextAttempt = DateTimeOffset.MinValue;
        Failures = 0;
        LastError = null;
    }

    /// <summary>The targets for <paramref name="now"/> from the forecast, <see langword="null"/> before the first answer.</summary>
    public WeatherTargets? Targets(DateTimeOffset now, bool invert) =>
        _location is { } location && Forecast?.At(now) is { } weather ? WeatherMapper.Map(weather, location, invert) : null;

    /// <summary>The weather for <paramref name="now"/> (status line).</summary>
    public WeatherSnapshot? Current(DateTimeOffset now) => Forecast?.At(now);

    private static TimeSpan Backoff(int failures)
    {
        double minutes = FirstBackoff.TotalMinutes * Math.Pow(2, Math.Min(failures - 1, 10));
        return TimeSpan.FromMinutes(Math.Min(minutes, MaximumBackoff.TotalMinutes));
    }
}
