using Sillfall.Core.Simulation;

namespace Sillfall.Core.Tests.Simulation;

public class FixedStepClockTests
{
    [Theory]
    [InlineData(1.0 / 30, 4)]
    [InlineData(1.0 / 60, 2)]
    [InlineData(1.0 / 120, 1)]
    public void Advance_RegularFrames_RunsTheMatchingNumberOfSteps(double frameTime, int stepsPerFrame)
    {
        var clock = new FixedStepClock();
        int total = 0;
        for (int frame = 0; frame < 120; frame++)
        {
            total += clock.Advance(frameTime + 1e-9);
        }

        total.Should().Be(120 * stepsPerFrame);
    }

    [Fact]
    public void Advance_144HzFrames_KeepsRealTimeOnAverage()
    {
        var clock = new FixedStepClock();
        int steps = 0;
        for (int frame = 0; frame < 144 * 10; frame++)
        {
            steps += clock.Advance(1.0 / 144);
        }

        // Ten seconds of frames simulate ten seconds, give or take the one step still in the accumulator.
        steps.Should().BeInRange(1199, 1200);
        clock.SimulationTime.Should().BeApproximately(10.0, 1.0 / 120);
    }

    [Fact]
    public void Advance_Hitch_IsClampedAndTheExcessDropped()
    {
        var clock = new FixedStepClock();

        clock.Advance(2.0).Should().Be(FixedStepClock.DefaultMaxStepsPerFrame);

        clock.DroppedTime.Should().BeApproximately(2.0 - (FixedStepClock.DefaultMaxStepsPerFrame / 120.0), 1e-9);
        clock.Advance(0).Should().Be(0, "the dropped time is gone, not deferred");
    }

    [Fact]
    public void Advance_CarriesTheRemainderAndReportsAlpha()
    {
        var clock = new FixedStepClock();

        clock.Advance(0.75 / 120).Should().Be(0);
        clock.Alpha.Should().BeApproximately(0.75f, 1e-4f);
        clock.Advance(0.5 / 120).Should().Be(1);
        clock.Alpha.Should().BeApproximately(0.25f, 1e-4f);
    }

    [Fact]
    public void Advance_IgnoresNegativeTime()
    {
        var clock = new FixedStepClock();

        clock.Advance(-1).Should().Be(0);
        clock.SimulationTime.Should().Be(0);
    }

    [Fact]
    public void Reset_DiscardsTheRemainder()
    {
        var clock = new FixedStepClock();
        clock.Advance(0.9 / 120);

        clock.Reset();

        clock.Alpha.Should().Be(0);
        clock.Advance(0.2 / 120).Should().Be(0);
    }
}
