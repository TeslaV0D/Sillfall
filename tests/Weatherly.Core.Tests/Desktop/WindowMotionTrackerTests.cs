using System.Numerics;
using Weatherly.Core.Desktop;
using Weatherly.Core.Geometry;

namespace Weatherly.Core.Tests.Desktop;

public class WindowMotionTrackerTests
{
    private readonly WindowMotionTracker _tracker = new();

    [Fact]
    public void Update_FirstSnapshot_ReportsEveryWindowAsAppeared()
    {
        List<WindowEvent> events = Update(0, Window(1, 0, 0), Window(2, 500, 0));

        events.Select(e => (e.Kind, e.WindowId)).Should().Equal((WindowEventKind.Appeared, 1L), (WindowEventKind.Appeared, 2L));
    }

    [Fact]
    public void Update_NothingChanged_ReportsNothing()
    {
        Update(0, Window(1, 0, 0));

        Update(0.1, Window(1, 0, 0)).Should().BeEmpty();
    }

    [Fact]
    public void Update_ZOrderChangeOnly_ReportsNothing()
    {
        Update(0, Window(1, 0, 0), Window(2, 500, 0));

        Update(0.1, Window(2, 500, 0), Window(1, 0, 0)).Should().BeEmpty();
    }

    [Fact]
    public void Update_Move_ReportsVelocityInPixelsPerSecond()
    {
        Update(0, Window(1, 100, 100));

        WindowEvent moved = Update(0.5, Window(1, 300, 50)).Single();

        moved.Kind.Should().Be(WindowEventKind.Moved);
        moved.Velocity.Should().Be(new Vector2(400, -100));
        moved.PreviousBounds.Left.Should().Be(100);
        moved.Bounds.Left.Should().Be(300);
    }

    [Fact]
    public void Update_Resize_ReportsResized()
    {
        Update(0, Window(1, 100, 100));

        WindowEvent resized = Update(0.1, new TrackedWindow(1, PixelRect.FromSize(100, 100, 600, 300))).Single();

        resized.Kind.Should().Be(WindowEventKind.Resized);
        resized.PreviousBounds.Width.Should().Be(400);
        resized.Bounds.Width.Should().Be(600);
    }

    [Fact]
    public void Update_ResizeFromTheLeftEdge_ReportsResizeAndMove()
    {
        Update(0, Window(1, 100, 100));

        Update(0.1, new TrackedWindow(1, new PixelRect(50, 100, 500, 400)))
            .Select(e => e.Kind).Should().Equal(WindowEventKind.Resized, WindowEventKind.Moved);
    }

    [Fact]
    public void Update_WindowGone_ReportsDisappearedWithItsLastBounds()
    {
        Update(0, Window(1, 100, 100), Window(2, 700, 100));
        Update(0.1, Window(1, 120, 100), Window(2, 700, 100));

        WindowEvent gone = Update(0.2, Window(2, 700, 100)).Single();

        gone.Kind.Should().Be(WindowEventKind.Disappeared);
        gone.WindowId.Should().Be(1);
        gone.Bounds.Left.Should().Be(120);
    }

    [Fact]
    public void Update_WindowComesBack_ReportsAppearedAgain()
    {
        Update(0, Window(1, 100, 100));
        Update(0.1);

        Update(0.2, Window(1, 100, 100)).Single().Kind.Should().Be(WindowEventKind.Appeared);
    }

    [Fact]
    public void Update_ShakingWindow_ReportsShakenOnce()
    {
        Update(0, Window(1, 500, 300));
        var events = new List<WindowEvent>();
        int[] xs = [560, 440, 560, 440];
        for (int i = 0; i < xs.Length; i++)
        {
            events.AddRange(Update(0.08 * (i + 1), Window(1, xs[i], 300)));
        }

        events.Count(e => e.Kind == WindowEventKind.Shaken).Should().Be(1);
    }

    [Fact]
    public void Update_VerticalShake_IsDetectedToo()
    {
        Update(0, Window(1, 500, 300));
        var events = new List<WindowEvent>();
        int[] ys = [360, 240, 360, 240];
        for (int i = 0; i < ys.Length; i++)
        {
            events.AddRange(Update(0.08 * (i + 1), Window(1, 500, ys[i])));
        }

        events.Should().ContainSingle(e => e.Kind == WindowEventKind.Shaken);
    }

    [Fact]
    public void Update_SlowBackAndForth_IsNoShake()
    {
        Update(0, Window(1, 500, 300));
        var events = new List<WindowEvent>();
        int[] xs = [560, 440, 560, 440, 560];
        for (int i = 0; i < xs.Length; i++)
        {
            events.AddRange(Update(0.4 * (i + 1), Window(1, xs[i], 300)));
        }

        events.Should().NotContain(e => e.Kind == WindowEventKind.Shaken);
    }

    private static TrackedWindow Window(long id, int left, int top) => new(id, PixelRect.FromSize(left, top, 400, 300));

    private List<WindowEvent> Update(double time, params TrackedWindow[] windows)
    {
        var events = new List<WindowEvent>();
        _tracker.Update(WindowSnapshot.Of(time, windows), events);
        return events;
    }
}
