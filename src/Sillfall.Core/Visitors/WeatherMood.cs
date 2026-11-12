namespace Sillfall.Core.Visitors;

/// <summary>
/// The weather on screen as the visitors feel it, 0 to 1 per kind of weather: from the effects' intensities
/// (manual or synced), so visitors match what the user sees.
/// </summary>
public readonly record struct WeatherMood(float Rain, float Snow, float Leaves, float Fog, float Sun, float Wind, float Grass)
{
    /// <summary>Below this a kind of weather does not count.</summary>
    public const float Threshold = 0.2f;

    public float Of(WeatherAffinity affinity) => affinity switch
    {
        WeatherAffinity.Rain => Rain,
        WeatherAffinity.Snow => Snow,
        WeatherAffinity.Leaves => Leaves,
        WeatherAffinity.Fog => Fog,
        WeatherAffinity.Sun => Sun,
        WeatherAffinity.Wind => Wind,
        WeatherAffinity.Grass => Grass,
        _ => 0f,
    };

    /// <summary>The strongest kind of weather, or <see cref="WeatherAffinity.Any"/> when nothing reaches <see cref="Threshold"/>.</summary>
    public WeatherAffinity Dominant
    {
        get
        {
            WeatherAffinity best = WeatherAffinity.Any;
            float strongest = Threshold;
            for (WeatherAffinity affinity = WeatherAffinity.Rain; affinity <= WeatherAffinity.Grass; affinity++)
            {
                float value = Of(affinity);
                if (value >= strongest)
                {
                    strongest = value;
                    best = affinity;
                }
            }

            return best;
        }
    }

    /// <summary>Whether a visitor's favourite weather is on screen (always for <see cref="WeatherAffinity.Any"/>).</summary>
    public bool Suits(WeatherAffinity affinity) => affinity == WeatherAffinity.Any || Of(affinity) >= Threshold;
}
