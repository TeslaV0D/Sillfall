using Weatherly.Core.Diagnostics;
using Weatherly.Core.Displays;
using Weatherly.Core.Geometry;
using Weatherly.Core.Overlay;

namespace Weatherly.Core.Tests.Overlay;

public class OverlayDiagnosticsTests
{
    private static readonly MonitorInfo s_primary = Monitor(1, 0, 0, 2560, 1440);
    private static readonly MonitorInfo s_left = Monitor(2, -1920, 0, 1920, 1080);
    private static readonly OverlayOptions s_enabled = new() { Enabled = true, Content = OverlayContent.TestPattern };

    [Fact]
    public void Evaluate_HealthyOverlays_PassEveryCheck()
    {
        OverlayStatus status = Status([s_primary, s_left], [Healthy(s_primary), Healthy(s_left)]);

        IReadOnlyList<DiagnosticResult> results = OverlayDiagnostics.Evaluate(status, s_enabled);

        results.Should().OnlyContain(result => result.Passed);
        results.Select(result => result.Name).Should().Equal(
            "Overlay windows",
            "Overlay window styles",
            "Click-through",
            "Z-order",
            "First frame",
            "Screenshot exclusion",
            "Keyboard focus",
            "Overlay errors");
    }

    [Fact]
    public void Evaluate_MonitorWithoutOverlay_FailsCoverage()
    {
        OverlayStatus status = Status([s_primary, s_left], [Healthy(s_primary)]);

        DiagnosticResult coverage = Result(status, s_enabled, "Overlay windows");

        coverage.Passed.Should().BeFalse();
        coverage.Detail.Should().Contain(s_left.DeviceName);
    }

    [Fact]
    public void Evaluate_WindowNotOnItsMonitor_FailsCoverage()
    {
        OverlayWindowStatus shifted = Healthy(s_left) with { WindowBounds = PixelRect.FromSize(-1900, 0, 1920, 1080) };
        OverlayStatus status = Status([s_primary, s_left], [Healthy(s_primary), shifted]);

        Result(status, s_enabled, "Overlay windows").Passed.Should().BeFalse();
    }

    [Fact]
    public void Evaluate_NoMonitors_FailsCoverage()
    {
        Result(Status([], []), s_enabled, "Overlay windows").Passed.Should().BeFalse();
    }

    [Fact]
    public void Evaluate_OverlayThatCatchesTheMouse_FailsClickThrough()
    {
        OverlayStatus status = Status([s_primary], [Healthy(s_primary) with { IsClickThrough = false }]);

        DiagnosticResult clickThrough = Result(status, s_enabled, "Click-through");

        clickThrough.Passed.Should().BeFalse();
        clickThrough.Detail.Should().Contain(s_primary.DeviceName);
    }

    [Fact]
    public void Evaluate_NormalWindowAboveTheOverlay_FailsZOrder()
    {
        OverlayWindowStatus covered = Healthy(s_primary) with
        {
            CoveredBy = [new CoveringWindow("Shell_TrayWnd", IsTopmost: true), new CoveringWindow("WinUIDesktopWin32WindowClass", IsTopmost: false)],
        };

        DiagnosticResult zOrder = Result(Status([s_primary], [covered]), s_enabled, "Z-order");

        zOrder.Passed.Should().BeFalse();
        zOrder.Detail.Should().Contain("WinUIDesktopWin32WindowClass").And.NotContain("Shell_TrayWnd");
    }

    [Fact]
    public void Evaluate_OnlyTopmostWindowsAbove_PassesZOrderAndNamesThem()
    {
        OverlayWindowStatus covered = Healthy(s_primary) with { CoveredBy = [new CoveringWindow("Shell_TrayWnd", IsTopmost: true)] };

        DiagnosticResult zOrder = Result(Status([s_primary], [covered]), s_enabled, "Z-order");

        zOrder.Passed.Should().BeTrue();
        zOrder.Detail.Should().Contain("Shell_TrayWnd (topmost)");
    }

    [Fact]
    public void Evaluate_MissingStyles_FailsStyleCheck()
    {
        OverlayStatus status = Status([s_primary], [Healthy(s_primary) with { HasOverlayStyles = false }]);

        Result(status, s_enabled, "Overlay window styles").Passed.Should().BeFalse();
    }

    [Fact]
    public void Evaluate_NothingPresented_FailsFirstFrame()
    {
        OverlayStatus status = Status([s_primary], [Healthy(s_primary) with { PresentedFrames = 0 }]);

        Result(status, s_enabled, "First frame").Passed.Should().BeFalse();
    }

    [Theory]
    [InlineData(false, false, true)]
    [InlineData(true, true, true)]
    [InlineData(true, false, false)]
    [InlineData(false, true, false)]
    public void Evaluate_ScreenshotExclusion_MustMatchTheOption(bool wanted, bool actual, bool expected)
    {
        OverlayStatus status = Status([s_primary], [Healthy(s_primary) with { IsExcludedFromCapture = actual }]);

        Result(status, s_enabled with { ExcludeFromCapture = wanted }, "Screenshot exclusion").Passed.Should().Be(expected);
    }

    [Fact]
    public void Evaluate_OverlayWithFocus_FailsFocusCheck()
    {
        OverlayStatus status = Status([s_primary], [Healthy(s_primary)]) with { OverlayHasFocus = true };

        Result(status, s_enabled, "Keyboard focus").Passed.Should().BeFalse();
    }

    [Fact]
    public void Evaluate_ReportedError_FailsWithItsMessage()
    {
        OverlayStatus status = Status([s_primary], [Healthy(s_primary)]) with { LastError = "device removed" };

        DiagnosticResult errors = Result(status, s_enabled, "Overlay errors");

        errors.Passed.Should().BeFalse();
        errors.Detail.Should().Be("device removed");
    }

    [Fact]
    public void Evaluate_SwitchedOff_ExpectsNoWindows()
    {
        var disabled = new OverlayOptions { Enabled = false };

        Result(Status([s_primary], []), disabled, "Overlay windows").Passed.Should().BeTrue();
        Result(Status([s_primary], [Healthy(s_primary)]), disabled, "Overlay windows").Passed.Should().BeFalse();
        OverlayDiagnostics.Evaluate(Status([s_primary], []), disabled)
            .Select(result => result.Name)
            .Should().Equal("Overlay windows", "Keyboard focus", "Overlay errors");
    }

    [Fact]
    public void Evaluate_EnabledWithoutAnyOverlay_FailsThePerWindowChecks()
    {
        IReadOnlyList<DiagnosticResult> results = OverlayDiagnostics.Evaluate(Status([s_primary], []), s_enabled);

        results.Where(result => result.Name is "Click-through" or "Z-order" or "First frame" or "Overlay window styles")
            .Should().OnlyContain(result => !result.Passed);
    }

    [Fact]
    public void EvaluateIdle_NoNewFrames_Passes()
    {
        OverlayStatus status = Status([s_primary], [Healthy(s_primary)]);

        OverlayDiagnostics.EvaluateIdle(status, status).Passed.Should().BeTrue();
    }

    [Fact]
    public void EvaluateIdle_NewFrames_Fails()
    {
        OverlayStatus earlier = Status([s_primary], [Healthy(s_primary)]);
        OverlayStatus later = Status([s_primary], [Healthy(s_primary) with { PresentedFrames = 31 }]);

        DiagnosticResult idle = OverlayDiagnostics.EvaluateIdle(earlier, later);

        idle.Passed.Should().BeFalse();
        idle.Detail.Should().StartWith("30 frame(s)");
    }

    [Fact]
    public void EvaluateIdle_RebuiltOverlays_Fails()
    {
        OverlayStatus earlier = Status([s_primary], [Healthy(s_primary)]);
        OverlayStatus later = Status([s_primary], [Healthy(s_primary) with { WindowHandle = 999 }]);

        OverlayDiagnostics.EvaluateIdle(earlier, later).Passed.Should().BeFalse();
    }

    [Fact]
    public void DiagnosticResult_ToString_IsTabSeparated()
    {
        new DiagnosticResult("Click-through", true, "fine").ToString().Should().Be("OK\tClick-through\tfine");
        new DiagnosticResult("Click-through", false, "bad").ToString().Should().Be("FAILED\tClick-through\tbad");
    }

    private static DiagnosticResult Result(OverlayStatus status, OverlayOptions options, string name) =>
        OverlayDiagnostics.Evaluate(status, options).Single(result => result.Name == name);

    private static OverlayStatus Status(MonitorInfo[] monitors, OverlayWindowStatus[] overlays) =>
        new(monitors, overlays, overlays.Length > 0 ? "Test adapter" : null, OverlayHasFocus: false, LastError: null);

    private static OverlayWindowStatus Healthy(MonitorInfo monitor) =>
        new(monitor, WindowHandle: monitor.Bounds.Left + 1000, monitor.Bounds, HasOverlayStyles: true, IsClickThrough: true, IsExcludedFromCapture: false, PresentedFrames: 1, CoveredBy: []);

    private static MonitorInfo Monitor(int number, int left, int top, int width, int height)
    {
        PixelRect bounds = PixelRect.FromSize(left, top, width, height);
        return new MonitorInfo($@"\\.\DISPLAY{number}", bounds, bounds, MonitorInfo.DefaultDpi, IsPrimary: number == 1);
    }
}
