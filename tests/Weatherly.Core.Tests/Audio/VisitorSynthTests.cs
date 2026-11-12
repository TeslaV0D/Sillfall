using Weatherly.Core.Audio;
using Weatherly.Core.Visitors;

namespace Weatherly.Core.Tests.Audio;

public class VisitorSynthTests
{
    [Theory]
    [InlineData(VisitorSound.Arrive)]
    [InlineData(VisitorSound.Collect)]
    [InlineData(VisitorSound.Sulk)]
    public void Render_IsShortCleanAndQuietAtTheEnds(VisitorSound sound)
    {
        foreach (VisitorKind kind in Enum.GetValues<VisitorKind>())
        {
            float[] samples = VisitorSynth.Render(sound, kind);

            (samples.Length / (double)VisitorSynth.DefaultSampleRate).Should().BeInRange(0.1, 0.8);
            samples.Should().OnlyContain(sample => float.IsFinite(sample));
            samples.Max(MathF.Abs).Should().BeApproximately(VisitorSynth.Peak, 1e-4f);
            MathF.Abs(samples[0]).Should().BeLessThan(0.02f, "no click at the start");
            MathF.Abs(samples[^1]).Should().BeLessThan(0.02f, "no click at the end");
        }
    }

    [Fact]
    public void Collect_IsLongerThanArrive()
    {
        VisitorSynth.Render(VisitorSound.Collect, VisitorKind.Pip).Length.Should().BeGreaterThan(VisitorSynth.Render(VisitorSound.Arrive, VisitorKind.Pip).Length);
    }

    [Fact]
    public void EveryVisitor_HasItsOwnPitch()
    {
        float[] pitches = [.. Enum.GetValues<VisitorKind>().Select(VisitorSynth.Pitch)];
        pitches.Should().OnlyHaveUniqueItems();

        // Higher pitch, more zero crossings in the same sound.
        Crossings(VisitorSynth.Render(VisitorSound.Arrive, VisitorKind.Lumen))
            .Should().BeGreaterThan(Crossings(VisitorSynth.Render(VisitorSound.Arrive, VisitorKind.Tuck)));
    }

    [Fact]
    public void Render_IsDeterministic()
    {
        VisitorSynth.Render(VisitorSound.Sulk, VisitorKind.Sol).Should().Equal(VisitorSynth.Render(VisitorSound.Sulk, VisitorKind.Sol));
    }

    private static int Crossings(float[] samples)
    {
        int count = 0;
        for (int i = 1; i < samples.Length; i++)
        {
            if ((samples[i - 1] < 0f) != (samples[i] < 0f))
            {
                count++;
            }
        }

        return count;
    }
}
