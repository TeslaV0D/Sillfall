namespace Sillfall.Core.Weather;

/// <summary>What the weather asks of each effect, from <see cref="WeatherMapper"/>.</summary>
public sealed record WeatherTargets
{
    /// <summary>Intensities, 0 to 1.</summary>
    public float Rain { get; init; }

    public float Snow { get; init; }

    public float Leaves { get; init; }

    public float Fog { get; init; }

    public float Sun { get; init; }

    /// <summary>Wind in pixels per second at 100 % (the wind slider's unit), positive to the right.</summary>
    public float WindSpeed { get; init; }

    /// <summary>Gustiness, 0 to 1.</summary>
    public float Gustiness { get; init; }

    /// <summary>Frost: lying snow stays (see <c>SnowEffect.Frozen</c>).</summary>
    public bool SnowStays { get; init; }

    public bool IsDay { get; init; }
}

/// <summary>
/// The weather mapping of PLAN.md, section 5, documented in <c>docs/weather-mapping.md</c>. Pure and deterministic,
/// so every row of the table is a unit test.
/// </summary>
public static class WeatherMapper
{
    /// <summary>Precipitation that just shows (0.1 mm/h) and that is heavy rain (8 mm/h): the log scale's ends.</summary>
    public const float LightPrecipitation = 0.1f;

    public const float HeavyPrecipitation = 8f;

    /// <summary>Intensity at <see cref="LightPrecipitation"/>.</summary>
    public const float LightIntensity = 0.15f;

    /// <summary>At most this warm (°C) it snows when the symbol does not say, and lying snow stays.</summary>
    public const float FrostLimit = 1f;

    /// <summary>Above this (°C) lying snow melts.</summary>
    public const float MeltLimit = 2f;

    /// <summary>Wind speed (m/s) that makes the full wind slider (<see cref="MaxWindPixels"/>).</summary>
    public const float FullWindSpeed = 15f;

    public const float MaxWindPixels = 400f;

    /// <summary>From this wind speed (m/s) autumn leaves blow.</summary>
    public const float LeafWindSpeed = 4f;

    /// <summary>Inverted: from this temperature (°C) it starts to snow, fully at <see cref="InvertedFullSnow"/>.</summary>
    public const float InvertedSnowStart = 15f;

    public const float InvertedFullSnow = 30f;

    public static WeatherTargets Map(WeatherSnapshot weather, GeoPoint location, bool invert = false)
    {
        ArgumentNullException.ThrowIfNull(weather);
        string symbol = BaseSymbol(weather.Symbol);
        bool isDay = SolarPosition.IsDay(location, weather.Time);

        // Precipitation: the symbol says what falls and at least how much; the amount says how much more.
        bool symbolSnow = symbol.Contains("snow", StringComparison.Ordinal);
        bool symbolSleet = symbol.Contains("sleet", StringComparison.Ordinal);
        bool symbolRain = symbol.Contains("rain", StringComparison.Ordinal) || symbol.Contains("drizzle", StringComparison.Ordinal);
        float rate = Math.Max(weather.PrecipitationRate, symbolSnow || symbolSleet || symbolRain ? SymbolMinimum(symbol) : 0f);
        float precipitation = PrecipitationIntensity(rate);

        float snowShare = symbolSnow ? 1f
            : symbolSleet ? 0.5f
            : symbolRain ? 0f
            : weather.AirTemperature <= FrostLimit ? 1f : 0f;

        float rain = precipitation * (1f - snowShare);
        float snow = precipitation * snowShare;
        float fog = Fog(symbol, weather);
        float clear = 1f - Math.Clamp(weather.CloudFraction, 0f, 1f);
        float sun = isDay && precipitation <= 0f && fog < 0.5f ? clear : 0f;

        // Wind blows towards from + 180°; the screen's x axis is east (a map with north up).
        float towards = (weather.WindFromDirection + 180f) * MathF.PI / 180f;
        float windSpeed = Math.Clamp(weather.WindSpeed * MathF.Sin(towards) * MaxWindPixels / FullWindSpeed, -MaxWindPixels, MaxWindPixels);
        float gustiness = Math.Clamp(0.3f + (weather.WindSpeed / 20f), 0.3f, 1f);
        float leaves = IsLeafSeason(weather.Time, location) && weather.WindSpeed > LeafWindSpeed
            ? Math.Clamp(0.3f + ((weather.WindSpeed - LeafWindSpeed) / 10f), 0.3f, 1f)
            : 0f;

        var targets = new WeatherTargets
        {
            Rain = rain,
            Snow = snow,
            Leaves = leaves,
            Fog = fog,
            Sun = sun,
            WindSpeed = windSpeed,
            Gustiness = gustiness,
            SnowStays = weather.AirTemperature <= MeltLimit,
            IsDay = isDay,
        };

        return invert ? Invert(targets, weather) : targets;
    }

    /// <summary>
    /// The world turned around (PLAN.md, section 5): a clear sky rains, wet weather brings sunshine (by day), heat brings
    /// snow and frost melts it. Fog lifts. Wind and leaves stay as they are.
    /// </summary>
    public static WeatherTargets Invert(WeatherTargets targets, WeatherSnapshot weather)
    {
        ArgumentNullException.ThrowIfNull(targets);
        ArgumentNullException.ThrowIfNull(weather);
        float clear = 1f - Math.Clamp(weather.CloudFraction, 0f, 1f);
        float wet = Math.Max(targets.Rain, targets.Snow);
        float heat = Math.Clamp((weather.AirTemperature - InvertedSnowStart) / (InvertedFullSnow - InvertedSnowStart), 0f, 1f);
        return targets with
        {
            Rain = clear * (1f - heat),
            Snow = heat,
            Sun = targets.IsDay ? Math.Max(wet, targets.Fog) : 0f,
            Fog = 0f,
            SnowStays = weather.AirTemperature > MeltLimit,
        };
    }

    /// <summary>0.1 mm/h → 0.15 … 8 mm/h and more → 1, on a log scale. Less than half of 0.1 mm/h is a trace: 0.</summary>
    public static float PrecipitationIntensity(float millimetresPerHour)
    {
        if (!float.IsFinite(millimetresPerHour) || millimetresPerHour < LightPrecipitation * 0.5f)
        {
            return 0f;
        }

        float rate = Math.Max(millimetresPerHour, LightPrecipitation);
        float t = MathF.Log(rate / LightPrecipitation) / MathF.Log(HeavyPrecipitation / LightPrecipitation);
        return Math.Clamp(LightIntensity + ((1f - LightIntensity) * t), LightIntensity, 1f);
    }

    /// <summary>Autumn: September to November in the north, March to May in the south.</summary>
    public static bool IsLeafSeason(DateTimeOffset time, GeoPoint location)
    {
        int month = time.UtcDateTime.Month;
        return location.IsSouthern ? month is >= 3 and <= 5 : month is >= 9 and <= 11;
    }

    /// <summary>"lightrainshowers_day" → "lightrainshowers".</summary>
    private static string BaseSymbol(string symbol)
    {
        int suffix = symbol.IndexOf('_', StringComparison.Ordinal);
        return suffix < 0 ? symbol : symbol[..suffix];
    }

    /// <summary>Least precipitation a symbol stands for, when the forecast's amount is smaller (it often says 0 for showers).</summary>
    private static float SymbolMinimum(string symbol) =>
        symbol.StartsWith("light", StringComparison.Ordinal) ? 0.2f
        : symbol.StartsWith("heavy", StringComparison.Ordinal) ? 4f
        : 1f;

    /// <summary>The fog symbol is thick fog; very humid, calm and overcast air is mist.</summary>
    private static float Fog(string symbol, WeatherSnapshot weather) =>
        symbol == "fog" ? 0.75f
        : weather.Humidity >= 0.97f && weather.WindSpeed < 3f && weather.CloudFraction >= 0.9f ? 0.3f
        : 0f;
}
