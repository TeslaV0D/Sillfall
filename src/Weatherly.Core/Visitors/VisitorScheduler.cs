using Weatherly.Core.Simulation;

namespace Weatherly.Core.Visitors;

/// <summary>How often visitors come (PLAN.md, section 6).</summary>
public enum VisitorFrequency
{
    Never,
    Rarely,
    Sometimes,
    Often,
}

/// <summary>
/// When the next visitor comes and who it is. Works on wall-clock seconds passed in by the host, because the
/// simulation sleeps while nothing is on screen: the host asks <see cref="IsDue"/> on its idle timer and wakes the
/// render loop for the visit.
/// </summary>
public sealed class VisitorScheduler
{
    private Pcg32 _random;
    private VisitorFrequency _frequency = VisitorFrequency.Sometimes;

    public VisitorScheduler(ulong seed = 1)
    {
        _random = new Pcg32(seed);
    }

    /// <summary>When the next visitor is due, in the host's clock (seconds). <see cref="double.PositiveInfinity"/>: never.</summary>
    public double NextVisit { get; private set; } = double.PositiveInfinity;

    public VisitorFrequency Frequency => _frequency;

    /// <summary>Average time between visits.</summary>
    public static TimeSpan MeanInterval(VisitorFrequency frequency) => frequency switch
    {
        VisitorFrequency.Rarely => TimeSpan.FromMinutes(30),
        VisitorFrequency.Sometimes => TimeSpan.FromMinutes(10),
        VisitorFrequency.Often => TimeSpan.FromMinutes(3),
        _ => TimeSpan.MaxValue,
    };

    /// <summary>How many visitors may be on screen at once.</summary>
    public static int MaxAtOnce(VisitorFrequency frequency) => frequency == VisitorFrequency.Often ? 2 : 1;

    /// <summary>Sets the frequency and plans the next visit from <paramref name="now"/> (unchanged frequency keeps the plan).</summary>
    public void SetFrequency(VisitorFrequency frequency, double now)
    {
        if (frequency == _frequency && !double.IsPositiveInfinity(NextVisit))
        {
            return;
        }

        _frequency = frequency;
        PlanNext(now);
    }

    public bool IsDue(double now) => now >= NextVisit;

    /// <summary>A visit happened (or could not happen): plans the following one, 0.5 to 1.5 mean intervals later.</summary>
    public void PlanNext(double now)
    {
        if (_frequency == VisitorFrequency.Never)
        {
            NextVisit = double.PositiveInfinity;
            return;
        }

        NextVisit = now + (MeanInterval(_frequency).TotalSeconds * _random.NextFloat(0.5f, 1.5f));
    }

    /// <summary>
    /// Who comes: rarer visitors less often, and a visitor whose favourite weather is on screen up to four times as often.
    /// </summary>
    public VisitorKind Choose(WeatherMood mood)
    {
        Span<float> weights = stackalloc float[VisitorCatalog.All.Count];
        float total = 0f;
        for (int i = 0; i < weights.Length; i++)
        {
            VisitorProfile profile = VisitorCatalog.All[i];
            float weather = profile.Affinity == WeatherAffinity.Any ? 0.5f : mood.Of(profile.Affinity);
            weights[i] = profile.BaseWeight * (1f + (3f * Math.Clamp(weather, 0f, 1f)));
            total += weights[i];
        }

        float pick = _random.NextFloat() * total;
        for (int i = 0; i < weights.Length; i++)
        {
            pick -= weights[i];
            if (pick < 0f)
            {
                return (VisitorKind)i;
            }
        }

        return VisitorKind.Nim;
    }
}
