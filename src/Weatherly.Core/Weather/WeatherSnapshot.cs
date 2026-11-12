namespace Weatherly.Core.Weather;

/// <summary>
/// The weather at one moment, independent of the provider: what the mapping (<see cref="WeatherMapper"/>) needs.
/// </summary>
public sealed record WeatherSnapshot
{
    /// <summary>Start of the forecast step (UTC).</summary>
    public DateTimeOffset Time { get; init; }

    /// <summary>Air temperature in °C.</summary>
    public float AirTemperature { get; init; }

    /// <summary>Cloud cover, 0 to 1.</summary>
    public float CloudFraction { get; init; }

    /// <summary>Relative humidity, 0 to 1.</summary>
    public float Humidity { get; init; }

    /// <summary>Wind speed in m/s.</summary>
    public float WindSpeed { get; init; }

    /// <summary>Direction the wind comes from, in degrees clockwise from north (meteorological convention).</summary>
    public float WindFromDirection { get; init; }

    /// <summary>Expected precipitation in mm per hour.</summary>
    public float PrecipitationRate { get; init; }

    /// <summary>
    /// Weather symbol of the step (MET Norway codes such as "lightrain", "snowshowers_day", "fog"), empty when the
    /// forecast has none.
    /// </summary>
    public string Symbol { get; init; } = string.Empty;
}

/// <summary>A forecast: snapshots in time order, as the provider delivered them.</summary>
public sealed class WeatherForecast
{
    public WeatherForecast(IReadOnlyList<WeatherSnapshot> steps, DateTimeOffset updatedAt)
    {
        ArgumentNullException.ThrowIfNull(steps);
        Steps = [.. steps.OrderBy(step => step.Time)];
        UpdatedAt = updatedAt;
    }

    public IReadOnlyList<WeatherSnapshot> Steps { get; }

    /// <summary>When the provider computed the forecast.</summary>
    public DateTimeOffset UpdatedAt { get; }

    /// <summary>
    /// The step that covers <paramref name="time"/>: the last one that started before it. Before the first step it is
    /// the first one; <see langword="null"/> only for an empty forecast. A forecast that ran out (offline for days)
    /// keeps its last step.
    /// </summary>
    public WeatherSnapshot? At(DateTimeOffset time)
    {
        WeatherSnapshot? current = null;
        foreach (WeatherSnapshot step in Steps)
        {
            if (step.Time > time)
            {
                return current ?? step;
            }

            current = step;
        }

        return current;
    }
}
