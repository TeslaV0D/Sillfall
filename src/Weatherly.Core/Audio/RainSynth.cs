using Weatherly.Core.Simulation;

namespace Weatherly.Core.Audio;

/// <summary>
/// Procedural rain sound (PLAN.md, section 8): pink noise through a band-pass whose center rises with the
/// intensity, plus short drop transients whose density follows the splash rate of the simulation. No
/// samples, so no licensing questions, and it loops seamlessly. Platform-neutral: it only fills buffers;
/// playing them is up to Weatherly.Audio. Allocation-free.
/// </summary>
public sealed class RainSynth
{
    public const int DefaultSampleRate = 48_000;

    private const int MaxTransients = 32;

    /// <summary>Drop transients per splash: only a share of the hits is heard individually.</summary>
    private const float TransientsPerHit = 0.04f;

    private const float MaxTransientsPerSecond = 90f;

    private readonly float[] _transientLevel = new float[MaxTransients];
    private readonly float[] _transientDecay = new float[MaxTransients];
    private Pcg32 _random;

    // Pink noise filter state (Paul Kellet's refined method).
    private float _b0;
    private float _b1;
    private float _b2;
    private float _b3;
    private float _b4;
    private float _b5;
    private float _b6;

    // Band-pass biquad state and coefficients.
    private float _x1;
    private float _x2;
    private float _y1;
    private float _y2;
    private float _a0;
    private float _a1;
    private float _a2;
    private float _c1;
    private float _c2;

    // One-pole high-pass for the transients.
    private float _transientLow;

    private float _gain;
    private float _currentIntensity = -1f;
    private int _activeTransients;

    public RainSynth(int sampleRate = DefaultSampleRate, ulong seed = 1)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(sampleRate);
        SampleRate = sampleRate;
        _random = new Pcg32(seed);
        Configure(0f);
    }

    public int SampleRate { get; }

    /// <summary>Rain intensity, 0 to 1. Changes are smoothed over the next buffer.</summary>
    public float Intensity { get; set; }

    /// <summary>Splashes per second in the simulation. More hits, more audible drops.</summary>
    public float HitsPerSecond { get; set; }

    /// <summary>Output volume, 0 to 1.</summary>
    public float Volume { get; set; } = 0.6f;

    /// <summary>Fills <paramref name="buffer"/> with mono samples in [-1, 1].</summary>
    public void Render(Span<float> buffer)
    {
        float target = Math.Clamp(Intensity, 0f, 1f);
        if (MathF.Abs(target - _currentIntensity) > 0.001f)
        {
            Configure(target);
        }

        // Gain ramps over the buffer, so intensity changes do not click.
        float targetGain = target <= 0f ? 0f : 0.35f * MathF.Pow(target, 0.7f) * Math.Clamp(Volume, 0f, 1f);
        float gainStep = (targetGain - _gain) / Math.Max(buffer.Length, 1);

        float transientRate = MathF.Min(HitsPerSecond * TransientsPerHit, MaxTransientsPerSecond) * (target > 0f ? 1f : 0f);
        float transientChance = transientRate / SampleRate;

        for (int i = 0; i < buffer.Length; i++)
        {
            _gain += gainStep;
            float white = (_random.NextFloat() * 2f) - 1f;
            float sample = BandPass(Pink(white)) * _gain;

            if (_random.NextFloat() < transientChance)
            {
                StartTransient();
            }

            sample += Transients(white) * _gain * 2.5f;
            buffer[i] = Math.Clamp(sample, -1f, 1f);
        }

        _gain = targetGain;
    }

    private void Configure(float intensity)
    {
        _currentIntensity = intensity;

        // Light rain is a soft high hiss, heavy rain a fuller roar: the band moves down and widens.
        float center = 3200f - (1900f * intensity);
        float q = 0.9f - (0.4f * intensity);
        float omega = 2f * MathF.PI * center / SampleRate;
        float alpha = MathF.Sin(omega) / (2f * q);
        float norm = 1f + alpha;

        // RBJ cookbook band-pass, constant 0 dB peak gain.
        _a0 = alpha / norm;
        _a1 = 0f;
        _a2 = -alpha / norm;
        _c1 = -2f * MathF.Cos(omega) / norm;
        _c2 = (1f - alpha) / norm;
    }

    private float Pink(float white)
    {
        _b0 = (0.99886f * _b0) + (white * 0.0555179f);
        _b1 = (0.99332f * _b1) + (white * 0.0750759f);
        _b2 = (0.96900f * _b2) + (white * 0.1538520f);
        _b3 = (0.86650f * _b3) + (white * 0.3104856f);
        _b4 = (0.55000f * _b4) + (white * 0.5329522f);
        _b5 = (-0.7616f * _b5) - (white * 0.0168980f);
        float pink = _b0 + _b1 + _b2 + _b3 + _b4 + _b5 + _b6 + (white * 0.5362f);
        _b6 = white * 0.115926f;
        return pink * 0.11f;
    }

    private float BandPass(float x)
    {
        float y = (_a0 * x) + (_a1 * _x1) + (_a2 * _x2) - (_c1 * _y1) - (_c2 * _y2);
        _x2 = _x1;
        _x1 = x;
        _y2 = _y1;
        _y1 = y;
        return y * 2.2f;
    }

    private void StartTransient()
    {
        if (_activeTransients >= MaxTransients)
        {
            return;
        }

        int t = _activeTransients++;
        _transientLevel[t] = _random.NextFloat(0.15f, 0.45f);

        // 2 to 8 ms: from a tap to a small splash.
        float decaySeconds = _random.NextFloat(0.002f, 0.008f);
        _transientDecay[t] = MathF.Exp(-1f / (decaySeconds * SampleRate));
    }

    private float Transients(float white)
    {
        if (_activeTransients == 0)
        {
            return 0f;
        }

        float level = 0f;
        int t = 0;
        while (t < _activeTransients)
        {
            level += _transientLevel[t];
            _transientLevel[t] *= _transientDecay[t];
            if (_transientLevel[t] < 0.001f)
            {
                int last = --_activeTransients;
                _transientLevel[t] = _transientLevel[last];
                _transientDecay[t] = _transientDecay[last];
                continue;
            }

            t++;
        }

        // High-passed noise burst: a drop is a bright tick, not a thump.
        float burst = white * level;
        _transientLow += 0.25f * (burst - _transientLow);
        return burst - _transientLow;
    }
}
