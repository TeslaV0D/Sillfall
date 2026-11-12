using System.Globalization;
using System.Text.Json;

namespace Weatherly.Core.Weather.Providers;

/// <summary>
/// Reads MET Norway's <c>locationforecast/2.0/compact</c> JSON (https://api.met.no/weatherapi/locationforecast/2.0/documentation).
/// Missing values become neutral ones (no rain, no cloud), so a sparse answer still maps to calm weather.
/// </summary>
public static class MetNorwayParser
{
    /// <exception cref="JsonException">The text is no forecast of this format.</exception>
    public static WeatherForecast Parse(string json)
    {
        ArgumentNullException.ThrowIfNull(json);
        using var document = JsonDocument.Parse(json);
        JsonElement properties = Required(document.RootElement, "properties");
        DateTimeOffset updated = properties.TryGetProperty("meta", out JsonElement meta) && meta.TryGetProperty("updated_at", out JsonElement updatedAt)
            ? ParseTime(updatedAt)
            : DateTimeOffset.MinValue;

        var steps = new List<WeatherSnapshot>();
        foreach (JsonElement entry in Required(properties, "timeseries").EnumerateArray())
        {
            JsonElement data = Required(entry, "data");
            JsonElement details = Required(Required(data, "instant"), "details");

            // The next hour where the forecast has it, else a sixth of the next six hours.
            (string symbol, float precipitation) = Period(data, "next_1_hours", hours: 1);
            if (symbol.Length == 0)
            {
                (symbol, precipitation) = Period(data, "next_6_hours", hours: 6);
            }

            steps.Add(new WeatherSnapshot
            {
                Time = ParseTime(Required(entry, "time")),
                AirTemperature = Number(details, "air_temperature", 10f),
                CloudFraction = Math.Clamp(Number(details, "cloud_area_fraction", 0f) / 100f, 0f, 1f),
                Humidity = Math.Clamp(Number(details, "relative_humidity", 50f) / 100f, 0f, 1f),
                WindSpeed = Math.Max(Number(details, "wind_speed", 0f), 0f),
                WindFromDirection = Number(details, "wind_from_direction", 0f),
                PrecipitationRate = precipitation,
                Symbol = symbol,
            });
        }

        return new WeatherForecast(steps, updated);
    }

    private static (string Symbol, float PrecipitationRate) Period(JsonElement data, string name, int hours)
    {
        if (!data.TryGetProperty(name, out JsonElement period))
        {
            return (string.Empty, 0f);
        }

        string symbol = period.TryGetProperty("summary", out JsonElement summary)
            && summary.TryGetProperty("symbol_code", out JsonElement code) && code.ValueKind == JsonValueKind.String
            ? code.GetString() ?? string.Empty
            : string.Empty;
        float amount = period.TryGetProperty("details", out JsonElement details) ? Number(details, "precipitation_amount", 0f) : 0f;
        return (symbol, Math.Max(amount, 0f) / hours);
    }

    private static float Number(JsonElement element, string name, float fallback) =>
        element.TryGetProperty(name, out JsonElement value) && value.ValueKind == JsonValueKind.Number && value.TryGetDouble(out double number)
            && double.IsFinite(number)
            ? (float)number
            : fallback;

    private static JsonElement Required(JsonElement element, string name) =>
        element.ValueKind == JsonValueKind.Object && element.TryGetProperty(name, out JsonElement value)
            ? value
            : throw new JsonException($"The forecast has no \"{name}\".");

    private static DateTimeOffset ParseTime(JsonElement element) =>
        element.ValueKind == JsonValueKind.String
        && DateTimeOffset.TryParse(element.GetString(), CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal, out DateTimeOffset time)
            ? time
            : throw new JsonException("A forecast time is not a date.");
}
