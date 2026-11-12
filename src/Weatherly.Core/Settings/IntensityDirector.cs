using Weatherly.Core.Simulation;

namespace Weatherly.Core.Settings;

/// <summary>
/// Turns the stored settings into the intensities the effects run with (PLAN.md, section 4.8): the fixed value,
/// or for effects in random mode a smooth random walk inside [min, max]. Every rate period the walk picks a new
/// target and glides towards it. Seeded and driven by the time it is given, so it is testable.
/// </summary>
public sealed class IntensityDirector
{
    private const int Effects = 6;

    private readonly float[] _current = new float[Effects];
    private readonly float[] _target = new float[Effects];
    private readonly double[] _nextChange = new double[Effects];
    private readonly bool[] _started = new bool[Effects];
    private Pcg32 _random;
    private double _time;

    public IntensityDirector(ulong seed = 1)
    {
        _random = new Pcg32(seed);
    }

    /// <summary>The intensity of every effect, indexed by <see cref="EffectKind"/>.</summary>
    public ReadOnlySpan<float> Intensities => _current;

    /// <summary>Advances the walks by <paramref name="seconds"/> and returns the intensities for <paramref name="settings"/>.</summary>
    public ReadOnlySpan<float> Update(AppSettings settings, double seconds)
    {
        ArgumentNullException.ThrowIfNull(settings);
        _time += Math.Max(seconds, 0);
        for (int kind = 0; kind < Effects; kind++)
        {
            EffectConfig config = settings.Effect((EffectKind)kind);
            if (config.Mode != IntensityMode.Random)
            {
                _current[kind] = config.Intensity;
                _started[kind] = false;
                continue;
            }

            float period = config.RandomMinutes * 60f;
            if (!_started[kind])
            {
                // Start somewhere inside the range, close to where the user had it.
                _started[kind] = true;
                _current[kind] = Math.Clamp(config.Intensity, config.RandomMin, config.RandomMax);
                PickTarget(kind, config, period);
            }
            else if (_time >= _nextChange[kind])
            {
                PickTarget(kind, config, period);
            }

            // Glide: a third of the period to get most of the way.
            float blend = 1f - MathF.Exp(-(float)seconds / MathF.Max(period / 3f, 1f));
            float next = _current[kind] + ((_target[kind] - _current[kind]) * blend);
            _current[kind] = Math.Clamp(next, config.RandomMin, config.RandomMax);
        }

        return _current;
    }

    private void PickTarget(int kind, EffectConfig config, float period)
    {
        _target[kind] = _random.NextFloat(config.RandomMin, MathF.Max(config.RandomMin, config.RandomMax));
        _nextChange[kind] = _time + (period * _random.NextFloat(0.6f, 1.4f));
    }
}
