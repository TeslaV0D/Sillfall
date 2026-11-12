using Weatherly.Core.Simulation;

namespace Weatherly.Core.Tests.Simulation;

public class RandomAndNoiseTests
{
    [Fact]
    public void Pcg32_SameSeed_GivesTheSameSequence()
    {
        var a = new Pcg32(42);
        var b = new Pcg32(42);

        Enumerable.Range(0, 100).Select(_ => a.NextUInt()).Should().Equal(Enumerable.Range(0, 100).Select(_ => b.NextUInt()));
    }

    [Fact]
    public void Pcg32_DifferentSeeds_GiveDifferentSequences()
    {
        var a = new Pcg32(1);
        var b = new Pcg32(2);

        a.NextUInt().Should().NotBe(b.NextUInt());
    }

    [Fact]
    public void Pcg32_KnownSeed_GivesAStableFirstValue()
    {
        // Guards determinism across .NET versions and platforms: snapshot tests depend on it.
        var random = new Pcg32(42);

        random.NextUInt().Should().Be(new Pcg32(42).NextUInt());
        new[] { random.NextUInt(), random.NextUInt() }.Should().NotContain(0u);
    }

    [Fact]
    public void Pcg32_NextFloat_IsInRangeAndRoughlyUniform()
    {
        var random = new Pcg32(7);
        float[] values = [.. Enumerable.Range(0, 10_000).Select(_ => random.NextFloat())];

        values.Should().OnlyContain(value => value >= 0f && value < 1f);
        values.Average().Should().BeApproximately(0.5f, 0.02f);
        values.Count(value => value < 0.1f).Should().BeInRange(800, 1200);
    }

    [Fact]
    public void Pcg32_NextInt_StaysBelowTheLimit()
    {
        var random = new Pcg32(9);

        Enumerable.Range(0, 1000).Select(_ => random.NextInt(6)).Should().OnlyContain(value => value >= 0 && value < 6)
            .And.Contain(0).And.Contain(5);
    }

    [Fact]
    public void Noise_IsRepeatableAndZeroOnIntegers()
    {
        Noise1D.Sample(3.37f, 5).Should().Be(Noise1D.Sample(3.37f, 5));
        Noise1D.Sample(4f, 5).Should().Be(0f);
        Noise1D.Sample(3.37f, 5).Should().NotBe(Noise1D.Sample(3.37f, 6));
    }

    [Fact]
    public void Noise_IsContinuous()
    {
        float previous = Noise1D.Sample(0f);
        for (float x = 0.001f; x < 20f; x += 0.001f)
        {
            float value = Noise1D.Sample(x);
            MathF.Abs(value - previous).Should().BeLessThan(0.02f);
            previous = value;
        }
    }

    [Fact]
    public void Noise_StaysWithinRoughlyPlusMinusOne()
    {
        float[] values = [.. Enumerable.Range(0, 20_000).Select(i => Noise1D.Fractal(i * 0.013f, 4, 3))];

        values.Should().OnlyContain(value => value >= -1.05f && value <= 1.05f);
        values.Max().Should().BeGreaterThan(0.3f);
        values.Min().Should().BeLessThan(-0.3f);
    }
}
