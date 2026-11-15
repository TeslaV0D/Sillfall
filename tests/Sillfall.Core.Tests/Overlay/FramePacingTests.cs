using Sillfall.Core.Overlay;

namespace Sillfall.Core.Tests.Overlay;

public class FramePacingTests
{
    [Fact]
    public void FirstFrame_StartsAtOnce()
    {
        new FramePacing().WaitBeforeNextFrame(5.0, 60).Should().Be(0);
    }

    [Fact]
    public void Capped_WaitsForTheNextSlot()
    {
        var pacing = new FramePacing();
        pacing.FrameStarted(1.0, 60);

        pacing.WaitBeforeNextFrame(1.005, 60).Should().BeApproximately((1.0 / 60) - 0.005 - FramePacing.EarlyStartSeconds, 1e-9);
        pacing.WaitBeforeNextFrame(1.02, 60).Should().Be(0);
    }

    [Fact]
    public void Capped_SlotsFollowTheScheduleNotTheActualStart()
    {
        var pacing = new FramePacing();
        pacing.FrameStarted(0.0, 50);

        // Started 3 ms late: the next slot is still 40 ms after the first one, not 23 ms after this start.
        pacing.FrameStarted(0.023, 50);

        pacing.WaitBeforeNextFrame(0.023, 50).Should().BeApproximately(0.040 - 0.023 - FramePacing.EarlyStartSeconds, 1e-9);
    }

    [Fact]
    public void Capped_FarBehind_StartsANewSchedule()
    {
        var pacing = new FramePacing();
        pacing.FrameStarted(0.0, 60);

        // A hitch of half a second: no burst of frames to catch up.
        pacing.FrameStarted(0.5, 60);

        pacing.WaitBeforeNextFrame(0.5, 60).Should().BeApproximately((1.0 / 60) - FramePacing.EarlyStartSeconds, 1e-9);
    }

    [Fact]
    public void Uncapped_GuardsAgainstABusyLoopOnly()
    {
        var pacing = new FramePacing();
        pacing.FrameStarted(1.0, 0);

        pacing.WaitBeforeNextFrame(1.001, 0).Should().BeApproximately((1.0 / FramePacing.UncappedMaximumRate) - 0.001, 1e-9);
        pacing.WaitBeforeNextFrame(1.005, 0).Should().Be(0);
    }

    [Fact]
    public void ChangedCap_StartsAtOnce()
    {
        var pacing = new FramePacing();
        pacing.FrameStarted(1.0, 30);

        pacing.WaitBeforeNextFrame(1.001, 60).Should().Be(0);
    }

    [Theory]
    [InlineData(60, 180)]
    [InlineData(30, 180)]
    [InlineData(60, 60)]
    [InlineData(60, 144)]
    [InlineData(30, 60)]
    [InlineData(60, 165)]
    [InlineData(0, 180)]
    [InlineData(0, 60)]
    public void Loop_OnAVsyncDisplay_RunsAtTheCap(int cap, int refresh)
    {
        // F-06: Auto (60) ran at ~49 fps and Smoothest at 64 fps on a 180 Hz display. This simulates the loop: the
        // pacing wait (the timer wakes up to 0.6 ms late), a frame that takes 1 ms to draw, and a swap chain with one
        // queued frame that shows it at the next vertical blank and blocks the next frame until then.
        double vblank = 1.0 / refresh;
        var pacing = new FramePacing();
        var random = new Random(3);
        double now = 0;
        double shown = 0;
        double firstShown = 0;
        const int Frames = 900;
        for (int frame = 0; frame < Frames; frame++)
        {
            double wait = pacing.WaitBeforeNextFrame(now, cap);
            if (wait > 0)
            {
                now += wait + (random.NextDouble() * 0.0006);
            }

            pacing.FrameStarted(now, cap);
            now = Math.Max(now, shown);              // the swap chain waits until the previous frame is on screen
            now += 0.001;                            // drawing
            shown = Math.Ceiling(now / vblank) * vblank;
            if (frame == 0)
            {
                firstShown = shown;
            }
        }

        double fps = (Frames - 1) / (shown - firstShown);
        double expected = cap > 0 ? Math.Min(cap, refresh) : Math.Min(refresh, FramePacing.UncappedMaximumRate);
        fps.Should().BeInRange(expected * 0.97, expected * 1.01);
    }
}
