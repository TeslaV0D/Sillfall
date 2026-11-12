using Weatherly.Core.Desktop;
using Weatherly.Core.Diagnostics;
using Weatherly.Core.Geometry;
using Weatherly.Core.Overlay;
using Weatherly.Core.Surfaces;

namespace Weatherly.Core.Tests.Overlay;

public class TrackingDiagnosticsTests
{
    private const long AppId = 42;
    private static readonly PixelRect s_appBounds = PixelRect.FromSize(300, 200, 800, 600);

    [Fact]
    public void EvaluateWindow_TrackedAtExactBounds_Passes()
    {
        OverlayStatus status = Status(windows: [new TrackedWindow(AppId, s_appBounds)]);

        TrackingDiagnostics.EvaluateWindow("Window tracking", status, AppId, s_appBounds).Passed.Should().BeTrue();
    }

    [Fact]
    public void EvaluateWindow_OffByOnePixel_Fails()
    {
        OverlayStatus status = Status(windows: [new TrackedWindow(AppId, PixelRect.FromSize(300, 201, 800, 600))]);

        DiagnosticResult result = TrackingDiagnostics.EvaluateWindow("Window tracking", status, AppId, s_appBounds);

        result.Passed.Should().BeFalse();
        result.Detail.Should().Contain("(300, 201)");
    }

    [Fact]
    public void EvaluateWindow_NotTracked_Fails()
    {
        TrackingDiagnostics.EvaluateWindow("Window tracking", Status(), AppId, s_appBounds).Passed.Should().BeFalse();
    }

    [Fact]
    public void EvaluateTopEdge_FullEdgeOnTheFrame_Passes()
    {
        OverlayStatus status = Status(surfaces: [TopPiece(300, 1100)]);

        TrackingDiagnostics.EvaluateTopEdge(status, AppId, s_appBounds).Passed.Should().BeTrue();
    }

    [Fact]
    public void EvaluateTopEdge_EdgeSplitAcrossMonitors_StillCountsAsComplete()
    {
        OverlayStatus status = Status(surfaces: [TopPiece(300, 700), TopPiece(700, 1100, monitor: 1)]);

        TrackingDiagnostics.EvaluateTopEdge(status, AppId, s_appBounds).Passed.Should().BeTrue();
    }

    [Fact]
    public void EvaluateTopEdge_PartlyHidden_Fails()
    {
        OverlayStatus status = Status(surfaces: [TopPiece(300, 900)]);

        TrackingDiagnostics.EvaluateTopEdge(status, AppId, s_appBounds).Passed.Should().BeFalse();
    }

    [Fact]
    public void EvaluateTopEdge_WrongRow_Fails()
    {
        OverlayStatus status = Status(surfaces: [TopPiece(300, 1100) with { Position = 199 }]);

        TrackingDiagnostics.EvaluateTopEdge(status, AppId, s_appBounds).Passed.Should().BeFalse();
    }

    [Fact]
    public void EvaluateTopEdge_Missing_Fails()
    {
        TrackingDiagnostics.EvaluateTopEdge(Status(), AppId, s_appBounds).Passed.Should().BeFalse();
    }

    [Fact]
    public void EvaluateMoveEvents_CountsOnlyNewMoves()
    {
        OverlayStatus before = Status() with { WindowEvents = new Dictionary<WindowEventKind, long> { [WindowEventKind.Moved] = 3 } };
        OverlayStatus after = Status() with { WindowEvents = new Dictionary<WindowEventKind, long> { [WindowEventKind.Moved] = 5 } };

        TrackingDiagnostics.EvaluateMoveEvents(before, after).Detail.Should().StartWith("2 move event(s)");
        TrackingDiagnostics.EvaluateMoveEvents(before, after).Passed.Should().BeTrue();
        TrackingDiagnostics.EvaluateMoveEvents(after, after).Passed.Should().BeFalse();
    }

    private static SurfaceSegment TopPiece(int start, int end, int monitor = 0) =>
        new(new SurfaceKey(AppId, SurfaceEdge.Top), s_appBounds.Top, start, end, s_appBounds.Left, monitor);

    private static OverlayStatus Status(TrackedWindow[]? windows = null, SurfaceSegment[]? surfaces = null) =>
        new([], [], null, OverlayHasFocus: false, LastError: null)
        {
            Windows = windows ?? [],
            Surfaces = surfaces ?? [],
        };
}
