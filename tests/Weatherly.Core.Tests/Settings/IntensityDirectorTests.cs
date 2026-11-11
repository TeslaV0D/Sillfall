using Weatherly.Core.Settings;

namespace Weatherly.Core.Tests.Settings;

public class IntensityDirectorTests
{
    [Fact]
    public void Fixed_GivesTheSetIntensities()
    {
        var director = new IntensityDirector();
        AppSettings settings = new() { Rain = new EffectConfig { Intensity = 0.4f }, Grass = new EffectConfig { Intensity = 1f } };

        ReadOnlySpan<float> intensities = director.Update(settings, 1);

        intensities[(int)EffectKind.Rain].Should().Be(0.4f);
        intensities[(int)EffectKind.Grass].Should().Be(1f);
        intensities[(int)EffectKind.Snow].Should().Be(0f);
    }

    [Fact]
    public void Random_StaysInsideItsRangeAndMovesAround()
    {
        var director = new IntensityDirector(seed: 3);
        AppSettings settings = new() { Snow = new EffectConfig { Mode = IntensityMode.Random, RandomMin = 0.3f, RandomMax = 0.7f, RandomMinutes = 1f } };
        var seen = new List<float>();

        // Two simulated hours in steps of one second.
        for (int i = 0; i < 7200; i++)
        {
            seen.Add(director.Update(settings, 1)[(int)EffectKind.Snow]);
        }

        seen.Should().OnlyContain(value => value >= 0.3f && value <= 0.7f);
        (seen.Max() - seen.Min()).Should().BeGreaterThan(0.2f, "the walk covers most of the range");
        seen.Zip(seen.Skip(1), (a, b) => MathF.Abs(b - a)).Max().Should().BeLessThan(0.05f, "it glides, it does not jump");
    }

    [Fact]
    public void Random_LongerRates_ChangeMoreSlowly()
    {
        static float Travel(float minutes)
        {
            var director = new IntensityDirector(seed: 9);
            AppSettings settings = new() { Fog = new EffectConfig { Mode = IntensityMode.Random, RandomMin = 0f, RandomMax = 1f, RandomMinutes = minutes } };
            float previous = director.Update(settings, 1)[(int)EffectKind.Fog];
            float travel = 0f;
            for (int i = 0; i < 3600; i++)
            {
                float next = director.Update(settings, 1)[(int)EffectKind.Fog];
                travel += MathF.Abs(next - previous);
                previous = next;
            }

            return travel;
        }

        Travel(120f).Should().BeLessThan(Travel(2f) / 4f);
    }

    [Fact]
    public void Random_IsSeeded()
    {
        AppSettings settings = new() { Rain = new EffectConfig { Mode = IntensityMode.Random, RandomMinutes = 1f } };
        var first = new IntensityDirector(seed: 5);
        var second = new IntensityDirector(seed: 5);

        for (int i = 0; i < 500; i++)
        {
            first.Update(settings, 1);
            second.Update(settings, 1);
        }

        first.Intensities.ToArray().Should().Equal(second.Intensities.ToArray());
    }
}
