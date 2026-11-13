using Sillfall.Core.Audio;

namespace Sillfall.Core.Tests.Audio;

public class RainSynthTests
{
    private const int OneSecond = RainSynth.DefaultSampleRate;

    [Fact]
    public void Render_AtIntensityZero_IsSilent()
    {
        var synth = new RainSynth { Intensity = 0f, HitsPerSecond = 500 };

        float[] buffer = Render(synth, OneSecond);

        buffer.Max(MathF.Abs).Should().Be(0f);
    }

    [Fact]
    public void Render_StaysWithinFullScale()
    {
        var synth = new RainSynth { Intensity = 1f, HitsPerSecond = 5000, Volume = 1f };

        float[] buffer = Render(synth, OneSecond * 3);

        buffer.Should().OnlyContain(sample => sample >= -1f && sample <= 1f);
        Rms(buffer).Should().BeGreaterThan(0.02f);
    }

    [Fact]
    public void Render_GetsLouderWithIntensity()
    {
        float quiet = Rms(Settled(new RainSynth { Intensity = 0.2f }));
        float loud = Rms(Settled(new RainSynth { Intensity = 0.9f }));

        loud.Should().BeGreaterThan(quiet * 1.5f);
    }

    [Fact]
    public void Render_LightRainSoundsBrighterThanHeavyRain()
    {
        // Zero crossings per second as a cheap measure of brightness: light rain is a high hiss.
        float light = ZeroCrossings(Settled(new RainSynth { Intensity = 0.15f }));
        float heavy = ZeroCrossings(Settled(new RainSynth { Intensity = 1f }));

        light.Should().BeGreaterThan(heavy);
    }

    [Fact]
    public void Render_MoreHits_GiveMoreAudibleDrops()
    {
        // Drops are short peaks: they raise the crest factor (peak to RMS).
        float few = Crest(Settled(new RainSynth { Intensity = 0.5f, HitsPerSecond = 0 }));
        float many = Crest(Settled(new RainSynth { Intensity = 0.5f, HitsPerSecond = 2000 }));

        many.Should().BeGreaterThan(few);
    }

    [Fact]
    public void Render_VolumeScalesTheOutput()
    {
        float full = Rms(Settled(new RainSynth { Intensity = 0.6f, Volume = 1f }));
        float half = Rms(Settled(new RainSynth { Intensity = 0.6f, Volume = 0.5f }));

        half.Should().BeApproximately(full / 2, full * 0.05f);
    }

    [Fact]
    public void Render_ChangingIntensity_DoesNotJump()
    {
        var synth = new RainSynth { Intensity = 0f };
        float[] silent = Render(synth, 4800);
        synth.Intensity = 1f;
        float[] start = Render(synth, 480);

        // The gain ramps up over the buffer: the first samples stay small.
        start.Take(20).Max(MathF.Abs).Should().BeLessThan(0.05f);
        silent.Max(MathF.Abs).Should().Be(0f);
    }

    [Fact]
    public void Render_IsDeterministicForTheSameSeed()
    {
        float[] a = Render(new RainSynth(seed: 3) { Intensity = 0.7f, HitsPerSecond = 800 }, 9600);
        float[] b = Render(new RainSynth(seed: 3) { Intensity = 0.7f, HitsPerSecond = 800 }, 9600);

        a.Should().Equal(b);
    }

    [Fact]
    public void Render_DoesNotAllocate()
    {
        var synth = new RainSynth { Intensity = 0.8f, HitsPerSecond = 1500 };
        float[] buffer = new float[2400];
        synth.Render(buffer);

        Allocations.Measure(() =>
        {
            for (int i = 0; i < 100; i++)
            {
                synth.Intensity = i % 2 == 0 ? 0.8f : 0.6f;
                synth.Render(buffer);
            }
        }).Should().Be(0);
    }

    private static float[] Render(RainSynth synth, int samples)
    {
        float[] buffer = new float[samples];
        synth.Render(buffer);
        return buffer;
    }

    /// <summary>Renders past the fade-in and returns one second.</summary>
    private static float[] Settled(RainSynth synth)
    {
        Render(synth, OneSecond / 2);
        return Render(synth, OneSecond);
    }

    private static float Rms(float[] samples) => MathF.Sqrt(samples.Average(sample => sample * sample));

    private static float Crest(float[] samples) => samples.Max(MathF.Abs) / Rms(samples);

    private static float ZeroCrossings(float[] samples) =>
        Enumerable.Range(1, samples.Length - 1).Count(i => samples[i - 1] < 0 != samples[i] < 0);
}
