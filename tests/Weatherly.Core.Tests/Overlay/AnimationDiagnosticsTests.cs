using Weatherly.Core.Overlay;

namespace Weatherly.Core.Tests.Overlay;

public class AnimationDiagnosticsTests
{
    [Fact]
    public void EvaluateAnimation_SteadyFrames_Passes()
    {
        OverlayStatus before = Status() with { IsAnimating = true, AnimationFrames = 100, Sprites = 10 };
        OverlayStatus after = before with { AnimationFrames = 220 };

        AnimationDiagnostics.EvaluateAnimation(before, after, 2.0).Passed.Should().BeTrue();
        AnimationDiagnostics.EvaluateAnimation(before, after, 2.0).Detail.Should().StartWith("60 fps");
    }

    [Theory]
    [InlineData(true, 30, 10, false)] // 15 fps
    [InlineData(false, 120, 10, false)] // stopped
    [InlineData(true, 120, 0, false)] // nothing drawn
    [InlineData(true, 40, 1, true)] // exactly 20 fps
    public void EvaluateAnimation_RequiresRunningLoopFrameRateAndSprites(bool animating, long frames, int sprites, bool expected)
    {
        OverlayStatus before = Status() with { IsAnimating = true };
        OverlayStatus after = Status() with { IsAnimating = animating, AnimationFrames = frames, Sprites = sprites };

        AnimationDiagnostics.EvaluateAnimation(before, after, 2.0).Passed.Should().Be(expected);
    }

    [Theory]
    [InlineData(100, 0, true)]
    [InlineData(100, 48, false)]
    [InlineData(10, 0, false)]
    public void EvaluateLoopAllocations_NeedsEnoughFramesAndZeroBytes(long frames, long bytes, bool expected)
    {
        OverlayStatus status = Status() with { MeasuredLoopFrames = frames, LoopAllocatedBytes = bytes };

        AnimationDiagnostics.EvaluateLoopAllocations(status).Passed.Should().Be(expected);
    }

    [Fact]
    public void EvaluateIdleStop_PassesOnlyWhenTheLoopStopped()
    {
        AnimationDiagnostics.EvaluateIdleStop(Status() with { IsAnimating = false }, TimeSpan.FromSeconds(3)).Passed.Should().BeTrue();
        AnimationDiagnostics.EvaluateIdleStop(Status() with { IsAnimating = true }, TimeSpan.FromSeconds(3)).Passed.Should().BeFalse();
    }

    private static OverlayStatus Status() => new([], [], null, OverlayHasFocus: false, LastError: null);
}
