using Weatherly.Core.Effects.Snow;
using Weatherly.Core.Simulation;

namespace Weatherly.Core.Tests.Effects.Snow;

public class SnowFieldTests
{
    private const float Tolerance = 1e-3f;

    [Fact]
    public void Add_RaisesTheBinUnderThePosition()
    {
        var field = new SnowField(width: 100, binWidth: 2, maxHeight: 28, maxSlope: 2);

        field.Add(localX: 11, volume: 6);

        field.Heights[5].Should().Be(3f, "6 square pixels on a 2 px bin are 3 px high");
        field.Volume.Should().Be(6f);
    }

    [Fact]
    public void Add_AboveTheMaximumHeight_SpillsIntoTheNeighbours()
    {
        var field = new SnowField(width: 100, binWidth: 2, maxHeight: 10, maxSlope: 2);

        field.Add(localX: 10, volume: 30);

        field.Heights[5].Should().Be(10f);
        field.Heights[6].Should().Be(5f);
        field.LeftShed.Should().Be(0f);
        field.Volume.Should().Be(30f);
    }

    [Fact]
    public void Add_WhenEverythingNearIsFull_ShedsTheRestAtTheNearerEnd()
    {
        var left = new SnowField(width: 8, binWidth: 2, maxHeight: 10, maxSlope: 2);
        var right = new SnowField(width: 8, binWidth: 2, maxHeight: 10, maxSlope: 2);

        left.Add(localX: 2, volume: 100);
        right.Add(localX: 7, volume: 90);

        left.Peak.Should().Be(10f);
        left.LeftShed.Should().Be(20f, "4 bins hold 80 square pixels");
        right.RightShed.Should().Be(10f);
        (left.Volume + left.LeftShed).Should().BeApproximately(100f, Tolerance);
    }

    [Fact]
    public void Relax_KeepsTheAngleOfRepose()
    {
        var field = new SnowField(width: 200, binWidth: 2, maxHeight: 28, maxSlope: 2);
        field.Add(localX: 100, volume: 56);

        for (int i = 0; i < 400; i++)
        {
            field.Relax();
        }

        ReadOnlySpan<float> h = field.Heights;
        for (int i = 0; i < h.Length - 1; i++)
        {
            MathF.Abs(h[i] - h[i + 1]).Should().BeLessThanOrEqualTo(2f + Tolerance, $"bins {i} and {i + 1}");
        }

        field.Peak.Should().BeLessThan(28f, "the spike spread into a heap");
    }

    [Theory]
    [InlineData(1)]
    [InlineData(7)]
    [InlineData(123)]
    public void Snow_IsConservedThroughEveryOperation(ulong seed)
    {
        // PLAN.md, section 4.2: volume before = volume after + what fell off (+ what melted).
        var random = new Pcg32(seed);
        var field = new SnowField(width: 157, binWidth: 2, maxHeight: 28, maxSlope: 2);
        double added = 0;
        double removed = 0;

        for (int step = 0; step < 3000; step++)
        {
            float volume = random.NextFloat(0f, 12f);
            field.Add(random.NextFloat(-10f, 170f), volume);
            added += volume;
            field.Relax();

            switch (step % 500)
            {
                case 100:
                    field.Resize(90 + random.NextInt(200));
                    break;
                case 200:
                    removed += field.Shed(random.NextInt(field.Count), 5, 0.6f);
                    break;
                case 300:
                    removed += field.Melt(dt: 0.5f, rate: 0.1f, linear: 0.2f);
                    break;
            }

            if (step % 50 == 0)
            {
                removed += field.LeftShed + field.RightShed;
                field.LeftShed = 0f;
                field.RightShed = 0f;
            }
        }

        double remaining = field.Volume + field.LeftShed + field.RightShed;
        (remaining + removed).Should().BeApproximately(added, added * 1e-4);
    }

    [Fact]
    public void Relax_AtTheEnds_PushesSnowOverTheEdge()
    {
        var field = new SnowField(width: 20, binWidth: 2, maxHeight: 28, maxSlope: 2);
        field.Add(localX: 0, volume: 20);

        field.Relax();

        field.Heights[0].Should().BeLessThanOrEqualTo(2f);
        field.LeftShed.Should().BePositive();
    }

    [Fact]
    public void Melt_ReturnsWhatItTookAndEmptiesTheFieldOverTime()
    {
        var field = new SnowField(width: 100, binWidth: 2, maxHeight: 28, maxSlope: 2);
        field.Fill(20);
        float before = field.Volume;

        float melted = field.Melt(dt: 1f, rate: 0.08f, linear: 0.15f);

        (field.Volume + melted).Should().BeApproximately(before, before * 1e-5f);
        for (int i = 0; i < 60; i++)
        {
            field.Melt(dt: 1f, rate: 0.08f, linear: 0.15f);
        }

        field.Volume.Should().Be(0f);
    }

    [Fact]
    public void Resize_KeepsTheVolume()
    {
        var field = new SnowField(width: 300, binWidth: 2, maxHeight: 28, maxSlope: 2);
        field.Fill(12);
        float before = field.Volume;

        field.Resize(480);
        field.Volume.Should().BeApproximately(before, before * 1e-4f);
        field.Count.Should().Be(240);

        field.Resize(150);
        (field.Volume + field.LeftShed + field.RightShed).Should().BeApproximately(before, before * 1e-4f);
        field.Peak.Should().BeLessThanOrEqualTo(28f);
    }

    [Fact]
    public void Fill_IsAlreadyAtRest()
    {
        var field = new SnowField(width: 200, binWidth: 2, maxHeight: 28, maxSlope: 2);
        field.Fill(16);
        float before = field.Volume;

        field.Relax();

        field.Volume.Should().BeApproximately(before, Tolerance);
        field.LeftShed.Should().Be(0f);
        field.Heights[field.Count / 2].Should().Be(16f);
    }

    [Fact]
    public void TakeAll_EmptiesEverything()
    {
        var field = new SnowField(width: 100, binWidth: 2, maxHeight: 10, maxSlope: 2);
        field.Add(10, 40);

        float taken = field.TakeAll();

        taken.Should().BeApproximately(40f, Tolerance);
        field.IsEmpty.Should().BeTrue();
    }
}
