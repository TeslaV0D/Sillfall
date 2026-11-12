using Sillfall.Core.Surfaces;

namespace Sillfall.Core.Tests.Surfaces;

public class SurfaceIndexTests
{
    [Fact]
    public void TryFindLanding_DropCrossingATopEdge_LandsOnIt()
    {
        SurfaceIndex index = Index(Top(1, row: 200, 100, 900));

        index.TryFindLanding(500, 190, 205, out SurfaceHit hit).Should().BeTrue();

        hit.Row.Should().Be(200);
        hit.Segment.Should().Be(0);
    }

    [Theory]
    [InlineData(190, 199, false)] // stops just short of the line
    [InlineData(190, 200, true)] // reaches the line exactly
    [InlineData(200, 210, false)] // already at the line: does not land again
    [InlineData(199.5f, 200.5f, true)]
    public void TryFindLanding_UsesFromExclusiveToInclusive(float fromY, float toY, bool lands)
    {
        SurfaceIndex index = Index(Top(1, row: 200, 100, 900));

        index.TryFindLanding(500, fromY, toY, out _).Should().Be(lands);
    }

    [Theory]
    [InlineData(99.9f, false)]
    [InlineData(100f, true)]
    [InlineData(899.9f, true)]
    [InlineData(900f, false)]
    public void TryFindLanding_OnlyWithinTheSegmentColumns(float x, bool lands)
    {
        SurfaceIndex index = Index(Top(1, row: 200, 100, 900));

        index.TryFindLanding(x, 150, 250, out _).Should().Be(lands);
    }

    [Fact]
    public void TryFindLanding_SeveralSurfacesBelow_TakesTheFirstOneCrossed()
    {
        SurfaceIndex index = Index(Top(1, row: 500, 0, 1000), Top(2, row: 300, 400, 600), Floor(row: 1032, 0, 1920));

        index.TryFindLanding(450, 100, 2000, out SurfaceHit hit).Should().BeTrue();
        hit.Row.Should().Be(300);

        index.TryFindLanding(100, 100, 2000, out hit).Should().BeTrue();
        hit.Row.Should().Be(500);

        index.TryFindLanding(1500, 100, 2000, out hit).Should().BeTrue();
        hit.Row.Should().Be(1032);
    }

    [Fact]
    public void TryFindLanding_IgnoresSideSurfaces()
    {
        var side = new SurfaceSegment(new SurfaceKey(1, SurfaceEdge.Left), 99, 0, 1000, 0, 0);

        Index(side).TryFindLanding(99, 0, 2000, out _).Should().BeFalse();
    }

    [Fact]
    public void TryFindLanding_SegmentsSpanningManyBuckets_AreFoundEverywhere()
    {
        SurfaceIndex index = Index(Top(1, row: 200, -1000, 3000));

        foreach (float x in new[] { -1000f, -500f, 0f, 1234.5f, 2999f })
        {
            index.TryFindLanding(x, 0, 300, out _).Should().BeTrue($"x = {x}");
        }
    }

    [Fact]
    public void TryFindLanding_WithoutSurfaces_FindsNothing()
    {
        Index().TryFindLanding(0, 0, 1000, out _).Should().BeFalse();
        new SurfaceIndex().TryFindLanding(0, 0, 1000, out _).Should().BeFalse();
    }

    [Fact]
    public void Build_AndQuery_DoNotAllocateOnceWarm()
    {
        var surfaces = new SurfaceSet();
        for (int i = 0; i < 40; i++)
        {
            surfaces.Add(Top(i, row: 100 + (i * 20), i * 50, (i * 50) + 700));
        }

        var index = new SurfaceIndex();
        void Round()
        {
            index.Build(surfaces);
            for (int x = 0; x < 2000; x += 7)
            {
                index.TryFindLanding(x, 0, 2000, out _);
            }
        }

        // Warm-up runs the measured body often enough for the runtime to finish tiering it up,
        // so first-use costs land before the measurement.
        for (int warmUp = 0; warmUp < 100; warmUp++)
        {
            Round();
        }

        long before = GC.GetAllocatedBytesForCurrentThread();
        for (int round = 0; round < 50; round++)
        {
            Round();
        }

        (GC.GetAllocatedBytesForCurrentThread() - before).Should().Be(0);
    }

    private static SurfaceSegment Top(long id, int row, int start, int end) =>
        new(new SurfaceKey(id, SurfaceEdge.Top), row, start, end, start, 0);

    private static SurfaceSegment Floor(int row, int start, int end) =>
        new(new SurfaceKey(0, SurfaceEdge.Floor), row, start, end, start, 0);

    private static SurfaceIndex Index(params SurfaceSegment[] segments)
    {
        var surfaces = new SurfaceSet();
        foreach (SurfaceSegment segment in segments)
        {
            surfaces.Add(segment);
        }

        var index = new SurfaceIndex();
        index.Build(surfaces);
        return index;
    }
}
