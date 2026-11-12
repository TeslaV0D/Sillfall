using Sillfall.Core.Displays;
using Sillfall.Core.Geometry;
using Sillfall.Core.Overlay;

namespace Sillfall.Core.Tests.Overlay;

public class OverlayLayoutPlannerTests
{
    private static readonly MonitorInfo s_primary = Monitor(1, 0, 0, 2560, 1440, dpi: 96, primary: true);
    private static readonly MonitorInfo s_right = Monitor(2, 2560, 0, 1920, 1080);
    private static readonly MonitorInfo s_leftAbove = Monitor(3, -1920, -400, 1920, 1080, dpi: 144);

    [Fact]
    public void Plan_FirstStart_CreatesOneOverlayPerMonitor()
    {
        OverlayLayoutPlan plan = OverlayLayoutPlanner.Plan([], [s_primary, s_right, s_leftAbove]);

        plan.ToCreate.Should().Equal(s_primary, s_right, s_leftAbove);
        plan.ToRemove.Should().BeEmpty();
        plan.ToKeep.Should().BeEmpty();
        plan.HasChanges.Should().BeTrue();
    }

    [Fact]
    public void Plan_NothingChanged_KeepsEverything()
    {
        OverlayLayoutPlan plan = OverlayLayoutPlanner.Plan([s_primary, s_right], [s_right, s_primary]);

        plan.HasChanges.Should().BeFalse();
        plan.ToKeep.Should().BeEquivalentTo([s_primary, s_right]);
    }

    [Fact]
    public void Plan_MonitorPluggedIn_CreatesOnlyTheNewOverlay()
    {
        OverlayLayoutPlan plan = OverlayLayoutPlanner.Plan([s_primary], [s_primary, s_leftAbove]);

        plan.ToCreate.Should().Equal(s_leftAbove);
        plan.ToRemove.Should().BeEmpty();
        plan.ToKeep.Should().Equal(s_primary);
    }

    [Fact]
    public void Plan_MonitorUnplugged_RemovesOnlyItsOverlay()
    {
        OverlayLayoutPlan plan = OverlayLayoutPlanner.Plan([s_primary, s_right], [s_primary]);

        plan.ToRemove.Should().Equal(s_right.DeviceName);
        plan.ToCreate.Should().BeEmpty();
        plan.ToKeep.Should().Equal(s_primary);
    }

    [Fact]
    public void Plan_OverlaysDisabled_RemovesAll()
    {
        OverlayLayoutPlan plan = OverlayLayoutPlanner.Plan([s_primary, s_right], []);

        plan.ToRemove.Should().BeEquivalentTo([s_primary.DeviceName, s_right.DeviceName]);
        plan.ToCreate.Should().BeEmpty();
        plan.ToKeep.Should().BeEmpty();
    }

    [Fact]
    public void Plan_ResolutionChanged_RebuildsTheOverlay()
    {
        MonitorInfo resized = s_right with { Bounds = PixelRect.FromSize(2560, 0, 3840, 2160) };

        OverlayLayoutPlan plan = OverlayLayoutPlanner.Plan([s_primary, s_right], [s_primary, resized]);

        plan.ToRemove.Should().Equal(s_right.DeviceName);
        plan.ToCreate.Should().Equal(resized);
        plan.ToKeep.Should().Equal(s_primary);
    }

    [Fact]
    public void Plan_MonitorMovedToNegativeCoordinates_RebuildsTheOverlay()
    {
        MonitorInfo moved = s_right with { Bounds = PixelRect.FromSize(-1920, 360, 1920, 1080) };

        OverlayLayoutPlan plan = OverlayLayoutPlanner.Plan([s_primary, s_right], [s_primary, moved]);

        plan.ToRemove.Should().Equal(s_right.DeviceName);
        plan.ToCreate.Should().Equal(moved);
    }

    [Fact]
    public void Plan_ScalingChanged_RebuildsTheOverlay()
    {
        MonitorInfo rescaled = s_primary with { Dpi = 120 };

        OverlayLayoutPlan plan = OverlayLayoutPlanner.Plan([s_primary], [rescaled]);

        plan.ToRemove.Should().Equal(s_primary.DeviceName);
        plan.ToCreate.Should().Equal(rescaled);
    }

    [Fact]
    public void Plan_OnlyWorkAreaOrPrimaryFlagChanged_KeepsTheOverlayWithNewData()
    {
        MonitorInfo taskbarMoved = s_primary with { WorkArea = PixelRect.FromSize(0, 48, 2560, 1392), IsPrimary = false };

        OverlayLayoutPlan plan = OverlayLayoutPlanner.Plan([s_primary], [taskbarMoved]);

        plan.HasChanges.Should().BeFalse();
        plan.ToKeep.Should().Equal(taskbarMoved);
    }

    [Fact]
    public void Plan_MonitorSwappedForAnother_RemovesOldAndCreatesNew()
    {
        OverlayLayoutPlan plan = OverlayLayoutPlanner.Plan([s_primary, s_right], [s_primary, s_leftAbove]);

        plan.ToRemove.Should().Equal(s_right.DeviceName);
        plan.ToCreate.Should().Equal(s_leftAbove);
        plan.ToKeep.Should().Equal(s_primary);
    }

    [Fact]
    public void Plan_MonitorWithoutArea_GetsNoOverlay()
    {
        MonitorInfo collapsed = s_right with { Bounds = new PixelRect(2560, 0, 2560, 0) };

        OverlayLayoutPlanner.Plan([], [s_primary, collapsed]).ToCreate.Should().Equal(s_primary);
        OverlayLayoutPlanner.Plan([s_primary, s_right], [s_primary, collapsed]).ToRemove.Should().Equal(s_right.DeviceName);
    }

    [Fact]
    public void Plan_DuplicateDeviceNames_AreHandledOnce()
    {
        MonitorInfo duplicate = s_primary with { Bounds = PixelRect.FromSize(5000, 0, 800, 600) };

        OverlayLayoutPlan plan = OverlayLayoutPlanner.Plan([], [s_primary, duplicate]);

        plan.ToCreate.Should().Equal(s_primary);
    }

    [Fact]
    public void Plan_DeviceNamesDifferOnlyInCase_AreTheSameMonitor()
    {
        MonitorInfo lowerCase = s_primary with { DeviceName = s_primary.DeviceName.ToLowerInvariant() };

        OverlayLayoutPlanner.Plan([s_primary], [lowerCase]).HasChanges.Should().BeFalse();
    }

    [Fact]
    public void Plan_EveryTargetMonitorEndsUpCreatedOrKept()
    {
        MonitorInfo[] current = [s_primary, s_right];
        MonitorInfo[] target = [s_primary with { Dpi = 192 }, s_leftAbove, s_right];

        OverlayLayoutPlan plan = OverlayLayoutPlanner.Plan(current, target);

        plan.ToCreate.Concat(plan.ToKeep).Should().BeEquivalentTo(target);
        plan.ToRemove.Should().Equal(s_primary.DeviceName);
    }

    private static MonitorInfo Monitor(int number, int left, int top, int width, int height, int dpi = 96, bool primary = false)
    {
        PixelRect bounds = PixelRect.FromSize(left, top, width, height);
        var workArea = new PixelRect(bounds.Left, bounds.Top, bounds.Right, bounds.Bottom - 48);
        return new MonitorInfo($@"\\.\DISPLAY{number}", bounds, workArea, dpi, primary);
    }
}
