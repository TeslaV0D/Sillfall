using Sillfall.Core.Effects.Rain;

namespace Sillfall.Core.Tests.Effects.Rain;

public class WaterFieldTests
{
    [Fact]
    public void Constructor_SplitsTheWidthIntoBins()
    {
        var field = new WaterField(width: 101, binWidth: 4);

        field.Count.Should().Be(26);
        field.BinAt(0).Should().Be(0);
        field.BinAt(7.9f).Should().Be(1);
        field.BinAt(1000).Should().Be(25);
        field.BinAt(-5).Should().Be(0);
    }

    [Fact]
    public void Add_PutsWaterIntoTheBinUnderThePosition()
    {
        var field = new WaterField(100, 4);

        field.Add(10, 0.3f);

        field.Bins[2].Should().BeApproximately(0.3f, 1e-6f);
        field.Total.Should().BeApproximately(0.3f, 1e-6f);
    }

    [Fact]
    public void Add_OverflowRunsToTheNearerEnd()
    {
        var field = new WaterField(100, 4);

        field.Add(2, 1.5f);
        field.Add(98, 1.25f);

        field.Bins[0].Should().Be(WaterField.MaxPerBin);
        field.LeftRunoff.Should().BeApproximately(0.5f, 1e-6f);
        field.RightRunoff.Should().BeApproximately(0.25f, 1e-6f);
    }

    [Fact]
    public void Step_WithoutEvaporation_ConservesMass()
    {
        var field = new WaterField(400, 4);
        for (int i = 0; i < 100; i += 7)
        {
            field.Add(i * 4, 0.4f);
        }

        float before = field.Total;
        for (int step = 0; step < 600; step++)
        {
            field.Step(1f / 120, evaporation: 0f);
        }

        (field.Total + field.LeftRunoff + field.RightRunoff).Should().BeApproximately(before, 1e-3f);
    }

    [Fact]
    public void Step_SpreadsAPeakToItsNeighbours()
    {
        var field = new WaterField(200, 4);
        field.Add(100, 1f);
        int peak = field.BinAt(100);

        for (int step = 0; step < 30; step++)
        {
            field.Step(1f / 120, creep: 0f, evaporation: 0f);
        }

        field.Bins[peak].Should().BeLessThan(1f);
        field.Bins[peak - 1].Should().BePositive();
        field.Bins[peak + 1].Should().BePositive();
    }

    [Fact]
    public void Step_MovesWaterOffBothEnds()
    {
        var field = new WaterField(200, 4);
        field.Add(20, 1f);
        field.Add(180, 1f);

        for (int step = 0; step < 1200; step++)
        {
            field.Step(1f / 120, evaporation: 0f);
        }

        field.LeftRunoff.Should().BeGreaterThan(0.5f);
        field.RightRunoff.Should().BeGreaterThan(0.5f);
    }

    [Fact]
    public void Step_EvaporatesSlowly()
    {
        var field = new WaterField(200, 4);
        field.Add(100, 1f);

        for (int step = 0; step < 120; step++)
        {
            field.Step(1f / 120, creep: 0f, evaporation: 0.1f);
        }

        field.Total.Should().BeInRange(0.85f, 0.95f);
    }

    [Fact]
    public void Step_StaysStableWithLargeSteps()
    {
        var field = new WaterField(200, 4);
        field.Add(100, 1f);

        field.Step(1f, diffusion: 20f, creep: 0f, evaporation: 0f);

        foreach (float water in field.Bins)
        {
            water.Should().BeInRange(0f, 1f);
        }
    }

    [Fact]
    public void Resize_KeepsTheTotal()
    {
        var field = new WaterField(200, 4);
        for (int x = 0; x < 200; x += 10)
        {
            field.Add(x, 0.3f);
        }

        float total = field.Total;
        field.Resize(333);

        field.Count.Should().Be(84);
        field.Width.Should().Be(333);
        field.Total.Should().BeApproximately(total, 1e-3f);

        field.Resize(60);
        field.Total.Should().BeApproximately(total, 1e-3f);
    }

    [Fact]
    public void Drain_EmptiesEverythingAndReportsTheAmount()
    {
        var field = new WaterField(100, 4);
        field.Add(10, 0.5f);
        field.Add(2, 1.2f);

        field.Drain().Should().BeApproximately(1.7f, 1e-5f);
        field.Total.Should().Be(0f);
        field.LeftRunoff.Should().Be(0f);
    }
}
