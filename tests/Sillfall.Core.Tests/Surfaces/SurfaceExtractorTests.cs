using Sillfall.Core.Desktop;
using Sillfall.Core.Displays;
using Sillfall.Core.Geometry;
using Sillfall.Core.Surfaces;

namespace Sillfall.Core.Tests.Surfaces;

public class SurfaceExtractorTests
{
    /// <summary>1920 x 1080 primary monitor with a 48 px taskbar at the bottom.</summary>
    private static readonly MonitorInfo s_primary = Monitor("1", 0, 0, 1920, 1080, taskbar: 48);

    /// <summary>Second monitor right of the primary one, same height.</summary>
    private static readonly MonitorInfo s_right = Monitor("2", 1920, 0, 1920, 1080, taskbar: 0);

    /// <summary>Monitor left of and higher than the primary one: negative coordinates.</summary>
    private static readonly MonitorInfo s_leftAbove = Monitor("3", -1920, -300, 1920, 1080, taskbar: 0);

    // ---------- top edges ----------

    [Fact]
    public void TopEdge_SingleWindow_IsFullyVisible()
    {
        var window = Window(1, 100, 200, 800, 600);

        Segments(Top(1), [s_primary], window).Should().Equal(
            new SurfaceSegment(Top(1), Position: 200, Start: 100, End: 900, Origin: 100, MonitorIndex: 0));
    }

    [Fact]
    public void TopEdge_PartlyCoveredByHigherWindow_IsSplitAroundIt()
    {
        var higher = Window(2, 300, 100, 200, 400);
        var lower = Window(1, 100, 200, 800, 600);

        Ranges(Top(1), [s_primary], higher, lower).Should().Equal((100, 300), (500, 900));
    }

    [Fact]
    public void TopEdge_FullyCoveredByHigherWindow_IsHidden()
    {
        var higher = Window(2, 0, 0, 1200, 900);
        var lower = Window(1, 100, 200, 800, 600);

        Segments(Top(1), [s_primary], higher, lower).Should().BeEmpty();
    }

    [Fact]
    public void TopEdge_LowerWindowInZOrder_DoesNotHideHigherWindow()
    {
        var higher = Window(1, 100, 200, 800, 600);
        var lower = Window(2, 0, 0, 1200, 900);

        Ranges(Top(1), [s_primary], higher, lower).Should().Equal((100, 900));
    }

    [Theory]
    [InlineData(100, 200, true)] // ends exactly on the edge row, covering the row above it: no room to pile anything up
    [InlineData(100, 199, false)] // ends one row higher: the row above the edge stays free
    [InlineData(200, 500, true)] // starts on the edge row
    [InlineData(201, 500, false)] // starts one row below the edge: the edge itself stays visible
    public void TopEdge_HigherWindowNearTheEdgeRow_HidesOnlyWhenTouchingIt(int higherTop, int higherBottom, bool hidden)
    {
        // The lower window's edge row is 200.
        var higher = new TrackedWindow(2, new PixelRect(0, higherTop, 1000, higherBottom));
        var lower = Window(1, 100, 200, 800, 600);

        Segments(Top(1), [s_primary], higher, lower).Should().HaveCount(hidden ? 0 : 1);
    }

    [Theory]
    [InlineData(0, 100)] // ends exactly where the edge starts
    [InlineData(900, 1000)] // starts exactly where the edge ends
    public void TopEdge_HorizontallyAdjacentWindow_DoesNotHideIt(int left, int right)
    {
        var higher = new TrackedWindow(2, new PixelRect(left, 0, right, 1000));
        var lower = Window(1, 100, 200, 800, 600);

        Ranges(Top(1), [s_primary], higher, lower).Should().Equal((100, 900));
    }

    [Fact]
    public void TopEdge_SeveralHigherWindows_LeaveTheGapsBetweenThem()
    {
        var a = Window(2, 150, 150, 100, 100);
        var b = Window(3, 400, 100, 50, 300);
        var c = Window(4, 850, 0, 300, 250);
        var lower = Window(1, 100, 200, 800, 600);

        Ranges(Top(1), [s_primary], a, b, c, lower).Should().Equal((100, 150), (250, 400), (450, 850));
    }

    [Fact]
    public void TopEdge_OverlappingOccluders_RemoveTheirUnion()
    {
        var a = Window(2, 200, 100, 300, 300);
        var b = Window(3, 400, 150, 300, 300);
        var lower = Window(1, 100, 200, 800, 600);

        Ranges(Top(1), [s_primary], a, b, lower).Should().Equal((100, 200), (700, 900));
    }

    [Fact]
    public void TopEdge_MaximizedWindowOnMonitorTop_HasNoRoomAndIsDropped()
    {
        var maximized = new TrackedWindow(1, new PixelRect(0, 0, 1920, 1032), WindowTraits.Maximized);

        Segments(Top(1), [s_primary], maximized).Should().BeEmpty();
    }

    [Fact]
    public void TopEdge_OneRowBelowMonitorTop_IsKept()
    {
        var window = Window(1, 100, 1, 800, 600);

        Ranges(Top(1), [s_primary], window).Should().Equal((100, 900));
    }

    [Fact]
    public void TopEdge_AboveTheMonitor_IsDropped()
    {
        var window = Window(1, 100, -50, 800, 600);

        Segments(Top(1), [s_primary], window).Should().BeEmpty();
    }

    [Fact]
    public void TopEdge_PartlyOffScreen_IsClippedToTheMonitor()
    {
        var window = Window(1, 1500, 300, 800, 600);

        Ranges(Top(1), [s_primary], window).Should().Equal((1500, 1920));
    }

    [Fact]
    public void TopEdge_SpanningTwoMonitors_YieldsOnePiecePerMonitorWithTheSameKeyAndOrigin()
    {
        var window = Window(1, 1500, 300, 800, 600);

        Segments(Top(1), [s_primary, s_right], window).Should().Equal(
            new SurfaceSegment(Top(1), 300, 1500, 1920, 1500, 0),
            new SurfaceSegment(Top(1), 300, 1920, 2300, 1500, 1));
    }

    [Fact]
    public void TopEdge_OnMonitorWithNegativeCoordinates_KeepsNegativePositions()
    {
        var window = Window(1, -1700, -100, 600, 400);

        Segments(Top(1), [s_primary, s_leftAbove], window).Should().Equal(
            new SurfaceSegment(Top(1), -100, -1700, -1100, -1700, 1));
    }

    [Fact]
    public void TopEdge_SpanningMonitorsWithDifferentTops_ExistsOnlyWhereTheRowHasRoom()
    {
        // Row -300 is the top of the left monitor (no room there) and does not exist on the primary monitor.
        var window = Window(1, -500, -300, 1000, 500);

        Segments(Top(1), [s_primary, s_leftAbove], window).Should().BeEmpty();

        // Row 10 has room on both monitors.
        var lower = Window(2, -500, 10, 1000, 500);
        Ranges(Top(2), [s_primary, s_leftAbove], lower).Should().Equal((0, 500), (-500, 0));
    }

    [Fact]
    public void Extract_WindowOutsideAllMonitors_HasNoSurfaces()
    {
        var window = Window(1, 5000, 5000, 400, 300);

        All([s_primary], window).Where(segment => segment.Key.OwnerId == 1).Should().BeEmpty();
    }

    [Fact]
    public void Extract_WindowWithoutArea_IsIgnored()
    {
        var empty = new TrackedWindow(1, new PixelRect(100, 100, 100, 400));

        All([s_primary], empty).Where(segment => segment.Key.OwnerId == 1).Should().BeEmpty();
    }

    // ---------- sides ----------

    [Fact]
    public void Sides_SingleWindow_AreTheColumnsJustOutsideTheFrame()
    {
        var window = Window(1, 100, 200, 800, 600);

        Segments(Left(1), [s_primary], window).Should().Equal(new SurfaceSegment(Left(1), 99, 200, 800, 200, 0));
        Segments(Right(1), [s_primary], window).Should().Equal(new SurfaceSegment(Right(1), 900, 200, 800, 200, 0));
    }

    [Fact]
    public void Side_PartlyCoveredByHigherWindow_IsSplitIntoRows()
    {
        var higher = Window(2, 0, 400, 150, 100);
        var lower = Window(1, 100, 200, 800, 600);

        Ranges(Left(1), [s_primary], higher, lower).Should().Equal((200, 400), (500, 800));
    }

    [Fact]
    public void Side_HigherWindowCoveringOnlyTheBorderColumn_HidesIt()
    {
        // Starts exactly on the window's left edge, so the surface column 99 itself stays free,
        // but rain running down there would vanish under the higher window.
        var higher = Window(2, 100, 300, 200, 100);
        var lower = Window(1, 100, 200, 800, 600);

        Ranges(Left(1), [s_primary], higher, lower).Should().Equal((200, 300), (400, 800));
    }

    [Fact]
    public void Side_HigherWindowEndingLeftOfTheColumn_DoesNotHideIt()
    {
        var higher = new TrackedWindow(2, new PixelRect(0, 300, 99, 400));
        var lower = Window(1, 100, 200, 800, 600);

        Ranges(Left(1), [s_primary], higher, lower).Should().Equal((200, 800));
    }

    [Fact]
    public void Side_ColumnOffTheMonitor_IsDropped()
    {
        var window = Window(1, 0, 200, 800, 600);

        Segments(Left(1), [s_primary], window).Should().BeEmpty();
    }

    [Fact]
    public void Side_ColumnOnNeighbouringMonitor_BelongsToThatMonitor()
    {
        var window = Window(1, 0, 200, 800, 600);

        Segments(Left(1), [s_primary, s_leftAbove], window).Should().Equal(
            new SurfaceSegment(Left(1), -1, 200, 780, 200, 1));
    }

    [Fact]
    public void Side_TallerThanTheMonitor_IsClippedVertically()
    {
        var window = Window(1, 100, 500, 800, 2000);

        Ranges(Right(1), [s_primary], window).Should().Equal((500, 1080));
    }

    [Fact]
    public void Sides_CascadedWindows_AreVisibleOnlyAboveTheNextWindow()
    {
        // Typical cascade: every newer window 40 px further right and down, newest on top.
        TrackedWindow[] cascade = [.. Enumerable.Range(0, 4).Reverse().Select(i => Window(i + 1, 100 + (40 * i), 100 + (40 * i), 800, 600))];

        List<SurfaceSegment> all = All([s_primary], cascade);

        foreach (int id in new[] { 1, 2, 3 })
        {
            int offset = 100 + (40 * (id - 1));

            // The next window starts 40 px lower, so this top edge is free, but the right side only shows above it.
            Ranges(all, Top(id)).Should().Equal((offset, offset + 800));
            Ranges(all, Right(id)).Should().Equal((offset, offset + 40));
        }

        Ranges(all, Right(4)).Should().Equal((220, 820));
    }

    // ---------- floor ----------

    [Fact]
    public void Floor_IsTheBottomOfTheWorkAreaOfEveryMonitor()
    {
        List<SurfaceSegment> all = All([s_primary, s_right]);

        all.Should().Equal(
            new SurfaceSegment(Floor(0), 1032, 0, 1920, 0, 0),
            new SurfaceSegment(Floor(1), 1080, 1920, 3840, 1920, 1));
    }

    [Fact]
    public void Floor_WindowReachingIt_HidesThatPart()
    {
        var window = Window(1, 500, 600, 400, 432);

        Ranges(Floor(0), [s_primary], window).Should().Equal((0, 500), (900, 1920));
    }

    [Fact]
    public void Floor_WindowEndingAboveIt_LeavesItVisible()
    {
        var window = Window(1, 500, 600, 400, 431);

        Ranges(Floor(0), [s_primary], window).Should().Equal((0, 1920));
    }

    [Fact]
    public void Floor_MaximizedWindow_HidesItCompletely()
    {
        var maximized = new TrackedWindow(1, new PixelRect(0, 0, 1920, 1032), WindowTraits.Maximized);

        Segments(Floor(0), [s_primary], maximized).Should().BeEmpty();
    }

    [Fact]
    public void Floor_MonitorWithoutWorkArea_UsesItsBounds()
    {
        MonitorInfo monitor = s_right with { WorkArea = default };

        Segments(Floor(0), [monitor]).Should().Equal(new SurfaceSegment(Floor(0), 1080, 1920, 3840, 1920, 0));
    }

    // ---------- whole set ----------

    [Fact]
    public void Extract_NoWindows_YieldsOnlyFloors()
    {
        All([s_primary, s_right, s_leftAbove]).Select(segment => segment.Key.Edge).Should().AllBeEquivalentTo(SurfaceEdge.Floor);
    }

    [Fact]
    public void Extract_OrdersWindowsByZOrderThenFloors()
    {
        var higher = Window(7, 1000, 100, 300, 300);
        var lower = Window(3, 100, 200, 400, 300);

        All([s_primary], higher, lower).Select(segment => segment.Key).Should().Equal(
            Top(7), Left(7), Right(7), Top(3), Left(3), Right(3), Floor(0));
    }

    [Fact]
    public void Extract_ClearsThePreviousResult()
    {
        var extractor = new SurfaceExtractor();
        var output = new SurfaceSet();

        extractor.Extract(WindowSnapshot.Of(0, Window(1, 100, 200, 800, 600)), [s_primary], output);
        extractor.Extract(WindowSnapshot.Of(0), [s_primary], output);

        output.Count.Should().Be(1);
    }

    [Fact]
    public void Extract_IsDeterministic()
    {
        TrackedWindow[] windows = [Window(2, 300, 100, 200, 400), Window(1, 100, 200, 800, 600), Window(5, 1700, 50, 600, 900)];
        var extractor = new SurfaceExtractor();
        var first = new SurfaceSet();
        var second = new SurfaceSet();

        extractor.Extract(WindowSnapshot.Of(0, windows), [s_primary, s_right], first);
        extractor.Extract(WindowSnapshot.Of(0, windows), [s_primary, s_right], second);

        first.HasSameSegments(second).Should().BeTrue();
    }

    [Fact]
    public void Extract_AfterWarmUp_DoesNotAllocate()
    {
        TrackedWindow[] windows = [.. Enumerable.Range(0, 30).Select(i => Window(i + 1, 50 * i, 30 * i, 700, 500))];
        WindowSnapshot snapshot = WindowSnapshot.Of(0, windows);
        MonitorInfo[] monitors = [s_primary, s_right, s_leftAbove];
        var extractor = new SurfaceExtractor();
        var output = new SurfaceSet();
        for (int warmUp = 0; warmUp < 100; warmUp++)
        {
            extractor.Extract(snapshot, monitors, output);
        }

        long before = GC.GetAllocatedBytesForCurrentThread();
        for (int i = 0; i < 100; i++)
        {
            extractor.Extract(snapshot, monitors, output);
        }

        (GC.GetAllocatedBytesForCurrentThread() - before).Should().Be(0);
    }

    [Fact]
    public void Segments_ReportLengthAndOrientation()
    {
        var top = new SurfaceSegment(Top(1), 10, 5, 25, 5, 0);
        var side = new SurfaceSegment(Left(1), 10, 5, 25, 5, 0);

        top.Length.Should().Be(20);
        top.IsHorizontal.Should().BeTrue();
        side.IsHorizontal.Should().BeFalse();
        new SurfaceSegment(Floor(0), 0, 0, 1, 0, 0).IsHorizontal.Should().BeTrue();
    }

    // ---------- helpers ----------

    private static SurfaceKey Top(long id) => new(id, SurfaceEdge.Top);

    private static SurfaceKey Left(long id) => new(id, SurfaceEdge.Left);

    private static SurfaceKey Right(long id) => new(id, SurfaceEdge.Right);

    private static SurfaceKey Floor(int monitor) => new(monitor, SurfaceEdge.Floor);

    private static TrackedWindow Window(long id, int left, int top, int width, int height) =>
        new(id, PixelRect.FromSize(left, top, width, height));

    private static List<SurfaceSegment> All(MonitorInfo[] monitors, params TrackedWindow[] windowsTopmostFirst)
    {
        var output = new SurfaceSet();
        new SurfaceExtractor().Extract(WindowSnapshot.Of(0, windowsTopmostFirst), monitors, output);
        return [.. output.Segments];
    }

    private static List<SurfaceSegment> Segments(SurfaceKey key, MonitorInfo[] monitors, params TrackedWindow[] windowsTopmostFirst) =>
        [.. All(monitors, windowsTopmostFirst).Where(segment => segment.Key == key)];

    private static List<(int Start, int End)> Ranges(SurfaceKey key, MonitorInfo[] monitors, params TrackedWindow[] windowsTopmostFirst) =>
        Ranges(All(monitors, windowsTopmostFirst), key);

    private static List<(int Start, int End)> Ranges(List<SurfaceSegment> segments, SurfaceKey key) =>
        [.. segments.Where(segment => segment.Key == key).Select(segment => (segment.Start, segment.End))];

    private static MonitorInfo Monitor(string number, int left, int top, int width, int height, int taskbar)
    {
        PixelRect bounds = PixelRect.FromSize(left, top, width, height);
        var workArea = new PixelRect(bounds.Left, bounds.Top, bounds.Right, bounds.Bottom - taskbar);
        return new MonitorInfo($@"\\.\DISPLAY{number}", bounds, workArea, MonitorInfo.DefaultDpi, IsPrimary: number == "1");
    }
}
