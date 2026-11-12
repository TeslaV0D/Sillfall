using Sillfall.Core.Geometry;
using Sillfall.Core.Surfaces;

namespace Sillfall.Core.Tests.Surfaces;

public class SurfaceGeometryTests
{
    [Fact]
    public void Band_TopEdge_IsTheRowsDirectlyAboveTheWindow()
    {
        var segment = new SurfaceSegment(new SurfaceKey(1, SurfaceEdge.Top), Position: 200, Start: 100, End: 900, Origin: 100, MonitorIndex: 0);

        SurfaceGeometry.Band(segment, 2).Should().Be(new PixelRect(100, 198, 900, 200));
    }

    [Fact]
    public void Band_Floor_IsTheRowsDirectlyAboveTheFloor()
    {
        var segment = new SurfaceSegment(new SurfaceKey(0, SurfaceEdge.Floor), 1032, 0, 1920, 0, 0);

        SurfaceGeometry.Band(segment, 3).Should().Be(new PixelRect(0, 1029, 1920, 1032));
    }

    [Fact]
    public void Band_LeftSide_EndsAtTheSurfaceColumn()
    {
        // Window starts at x = 100, so the surface column is 99.
        var segment = new SurfaceSegment(new SurfaceKey(1, SurfaceEdge.Left), 99, 200, 800, 200, 0);

        SurfaceGeometry.Band(segment, 2).Should().Be(new PixelRect(98, 200, 100, 800));
    }

    [Fact]
    public void Band_RightSide_StartsAtTheSurfaceColumn()
    {
        // Window ends at x = 900 (exclusive), so the surface column is 900.
        var segment = new SurfaceSegment(new SurfaceKey(1, SurfaceEdge.Right), 900, 200, 800, 200, 0);

        SurfaceGeometry.Band(segment, 2).Should().Be(new PixelRect(900, 200, 902, 800));
    }

    [Fact]
    public void Band_RejectsZeroThickness()
    {
        var segment = new SurfaceSegment(new SurfaceKey(1, SurfaceEdge.Top), 200, 100, 900, 100, 0);

        Action band = () => SurfaceGeometry.Band(segment, 0);

        band.Should().Throw<ArgumentOutOfRangeException>();
    }

    [Fact]
    public void ToMonitor_SubtractsTheMonitorOrigin()
    {
        var rect = new PixelRect(-1700, -110, -1100, -100);
        PixelRect monitor = PixelRect.FromSize(-1920, -300, 1920, 1080);

        SurfaceGeometry.ToMonitor(rect, monitor).Should().Be(new PixelRect(220, 190, 820, 200));
    }
}
