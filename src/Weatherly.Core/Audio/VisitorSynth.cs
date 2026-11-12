using Weatherly.Core.Visitors;

namespace Weatherly.Core.Audio;

/// <summary>The sounds a visitor makes (PLAN.md, section 6: 2–3 sounds per character).</summary>
public enum VisitorSound
{
    /// <summary>Two rising blips when it appears.</summary>
    Arrive,

    /// <summary>A little sparkling arpeggio when it is collected.</summary>
    Collect,

    /// <summary>A falling "hmpf" when it is dropped outside the box.</summary>
    Sulk,
}

/// <summary>
/// Procedural visitor sounds: sine tones with soft envelopes, pitched per character, so every visitor sounds a little
/// different without any recorded samples (nothing to license). Rendered once per sound and kept by the player.
/// </summary>
public static class VisitorSynth
{
    public const int DefaultSampleRate = 48_000;

    /// <summary>Loudest sample of a rendered sound.</summary>
    public const float Peak = 0.6f;

    /// <summary>The visitor's own pitch in Hz: small ones sound higher.</summary>
    public static float Pitch(VisitorKind kind) => kind switch
    {
        VisitorKind.Pip => 660f,
        VisitorKind.Tuck => 520f,
        VisitorKind.Bristle => 600f,
        VisitorKind.Lumen => 990f,
        VisitorKind.Sol => 560f,
        VisitorKind.Drift => 740f,
        VisitorKind.Sprout => 820f,
        VisitorKind.Nim => 880f,
        _ => 700f,
    };

    /// <summary>The samples of a sound (mono, -<see cref="Peak"/> to <see cref="Peak"/>), silent at both ends.</summary>
    public static float[] Render(VisitorSound sound, VisitorKind kind, int sampleRate = DefaultSampleRate)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(sampleRate, 8000);
        float pitch = Pitch(kind);
        float[] samples = sound switch
        {
            VisitorSound.Arrive => Notes(sampleRate, 0.24f, [(0f, pitch, 1.06f, 0.075f), (0.09f, pitch * 1.26f, 1.06f, 0.11f)], sparkle: 0f),
            VisitorSound.Collect => Notes(
                sampleRate,
                0.62f,
                [(0f, pitch, 1f, 0.12f), (0.08f, pitch * 1.26f, 1f, 0.12f), (0.16f, pitch * 1.5f, 1f, 0.16f), (0.26f, pitch * 2f, 1f, 0.3f)],
                sparkle: 0.35f),
            _ => Notes(sampleRate, 0.36f, [(0f, pitch * 0.9f, 0.62f, 0.3f)], sparkle: 0f, vibrato: 7f),
        };

        Normalize(samples);
        return samples;
    }

    /// <summary>
    /// Sums notes (start s, frequency Hz, glide to this factor by the end, length s): sine plus a soft third harmonic,
    /// 5 ms attack, exponential decay, a short fade at the very end against clicks.
    /// </summary>
    private static float[] Notes(int sampleRate, float seconds, (float Start, float Frequency, float Glide, float Length)[] notes, float sparkle, float vibrato = 0f)
    {
        var samples = new float[(int)(seconds * sampleRate)];
        foreach ((float start, float frequency, float glide, float length) in notes)
        {
            int first = (int)(start * sampleRate);
            int count = Math.Min((int)(length * sampleRate), samples.Length - first);
            double phase = 0;
            for (int i = 0; i < count; i++)
            {
                float t = i / (float)sampleRate;
                float progress = i / (float)count;
                float f = frequency * (1f + ((glide - 1f) * progress)) * (1f + (0.02f * MathF.Sin(2f * MathF.PI * vibrato * t)));
                phase += 2.0 * Math.PI * f / sampleRate;
                float attack = Math.Min(1f, t / 0.005f);
                float decay = MathF.Exp(-4f * progress);
                float release = Math.Min(1f, (count - i) / (0.004f * sampleRate));
                float tone = (float)(Math.Sin(phase) + (0.25 * Math.Sin(3 * phase)) + (sparkle * Math.Sin(4 * phase) * decay));
                samples[first + i] += tone * attack * decay * release;
            }
        }

        return samples;
    }

    private static void Normalize(float[] samples)
    {
        float loudest = 0f;
        foreach (float sample in samples)
        {
            loudest = Math.Max(loudest, MathF.Abs(sample));
        }

        if (loudest <= 0f)
        {
            return;
        }

        float gain = Peak / loudest;
        for (int i = 0; i < samples.Length; i++)
        {
            samples[i] *= gain;
        }
    }
}
