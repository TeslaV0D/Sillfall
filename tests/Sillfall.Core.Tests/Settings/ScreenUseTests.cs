using Sillfall.Core.Geometry;
using Sillfall.Core.Settings;

namespace Sillfall.Core.Tests.Settings;

public class ScreenUseTests
{
    private static readonly PixelRect s_monitor = new(0, 0, 2560, 1440);

    private static readonly PixelRect s_workArea = new(0, 0, 2560, 1392);

    [Fact]
    public void Evaluate_OwnOverlayOnlyWithNormalWindowInFront_IsNotFullScreen()
    {
        // F-01: the shell says "busy" because of Sillfall's own topmost overlay, while Notepad is in front.
        var sample = new ScreenUseSample(ShellNotificationState.Busy, new PixelRect(300, 200, 1300, 900), s_monitor, HasCaption: true);

        ScreenUseRules.Evaluate(sample).Should().Be((false, false));
    }

    [Fact]
    public void Evaluate_MaximizedWindowInFront_IsNotFullScreen()
    {
        var sample = new ScreenUseSample(ShellNotificationState.Busy, s_workArea, s_monitor, IsMaximized: true, HasCaption: true);

        ScreenUseRules.Evaluate(sample).FullScreen.Should().BeFalse();
    }

    [Fact]
    public void Evaluate_MaximizedWindowWithCaptionCoveringTheMonitor_IsNotFullScreen()
    {
        // Auto-hide taskbar: a maximized window covers the whole monitor but is still an ordinary window.
        var sample = new ScreenUseSample(ShellNotificationState.Busy, s_monitor, s_monitor, IsMaximized: true, HasCaption: true);

        ScreenUseRules.Evaluate(sample).FullScreen.Should().BeFalse();
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void Evaluate_BorderlessWindowCoveringTheMonitor_IsFullScreen(bool maximized)
    {
        // A video or a browser after F11: no caption, the whole monitor.
        var sample = new ScreenUseSample(ShellNotificationState.Busy, new PixelRect(-1, -1, 2561, 1441), s_monitor, IsMaximized: maximized);

        ScreenUseRules.Evaluate(sample).Should().Be((true, false));
    }

    [Fact]
    public void Evaluate_BorderlessWindowCoveringTheMonitorWhileTheShellIsNotBusy_IsNotFullScreen()
    {
        var sample = new ScreenUseSample(ShellNotificationState.AcceptsNotifications, s_monitor, s_monitor);

        ScreenUseRules.Evaluate(sample).FullScreen.Should().BeFalse();
    }

    [Fact]
    public void Evaluate_DesktopInFront_IsNotFullScreen()
    {
        // F-18: Win+D, an empty virtual desktop or the last window minimized. Progman covers the monitor.
        var sample = new ScreenUseSample(ShellNotificationState.Busy, s_monitor, s_monitor, IsDesktop: true);

        ScreenUseRules.Evaluate(sample).FullScreen.Should().BeFalse();
    }

    [Fact]
    public void Evaluate_OwnWindowInFront_IsNotFullScreen()
    {
        var sample = new ScreenUseSample(ShellNotificationState.Busy, s_monitor, s_monitor, IsOwnProcess: true);

        ScreenUseRules.Evaluate(sample).FullScreen.Should().BeFalse();
    }

    [Fact]
    public void Evaluate_Direct3DExclusive_IsFullScreen()
    {
        var sample = new ScreenUseSample(ShellNotificationState.RunningDirect3DFullScreen, new PixelRect(0, 0, 800, 600), s_monitor, HasCaption: true);

        ScreenUseRules.Evaluate(sample).Should().Be((true, false));
    }

    [Fact]
    public void Evaluate_PresentationMode_IsPresentation()
    {
        var sample = new ScreenUseSample(ShellNotificationState.PresentationMode, new PixelRect(0, 0, 800, 600), s_monitor, HasCaption: true);

        ScreenUseRules.Evaluate(sample).Should().Be((false, true));
    }

    [Fact]
    public void Evaluate_WindowOnAnotherMonitorCoveringIt_IsFullScreen()
    {
        var second = new PixelRect(2560, 0, 4480, 1080);
        var sample = new ScreenUseSample(ShellNotificationState.Busy, second, second);

        ScreenUseRules.Evaluate(sample).FullScreen.Should().BeTrue();
    }

    [Fact]
    public void Evaluate_UnknownMonitor_IsNotFullScreen()
    {
        var sample = new ScreenUseSample(ShellNotificationState.Busy, s_monitor, default);

        ScreenUseRules.Evaluate(sample).FullScreen.Should().BeFalse();
    }

    [Fact]
    public void Filter_OneOddSample_DoesNotChangeTheState()
    {
        var filter = new ScreenUseFilter();

        filter.Update((true, false)).Should().Be((false, false));
        filter.Pending.Should().BeTrue();
        filter.Update((false, false)).Should().Be((false, false));
        filter.Pending.Should().BeFalse();
        filter.Update((true, false)).Should().Be((false, false));
    }

    [Fact]
    public void Filter_TwoAgreeingSamples_ChangeTheStateBothWays()
    {
        var filter = new ScreenUseFilter();

        filter.Update((true, false));
        filter.Update((true, false)).Should().Be((true, false));
        filter.Pending.Should().BeFalse();

        filter.Update((false, false)).Should().Be((true, false));
        filter.Update((false, false)).Should().Be((false, false));
    }

    [Fact]
    public void Filter_AlternatingSamples_NeverChangeTheState()
    {
        // The F-01 loop seen from the filter: busy, not busy, busy, ...
        var filter = new ScreenUseFilter();

        for (int i = 0; i < 20; i++)
        {
            filter.Update((i % 2 == 0, false)).Should().Be((false, false));
        }
    }

    [Fact]
    public void Filter_ChangingCandidate_StartsCountingAgain()
    {
        var filter = new ScreenUseFilter();

        filter.Update((true, false));
        filter.Update((false, true)).Should().Be((false, false));
        filter.Update((false, true)).Should().Be((false, true));
    }
}
