using Sillfall.Core.Desktop;
using Sillfall.Core.Geometry;

namespace Sillfall.Core.Tests.Desktop;

public class WindowSnapshotTests
{
    private static readonly TrackedWindow s_a = new(1, PixelRect.FromSize(0, 0, 100, 100));
    private static readonly TrackedWindow s_b = new(2, PixelRect.FromSize(50, 50, 100, 100), WindowTraits.Topmost);

    [Fact]
    public void HasSameWindows_ComparesContentAndOrderButNotTime()
    {
        WindowSnapshot.Of(1, s_a, s_b).HasSameWindows(WindowSnapshot.Of(2, s_a, s_b)).Should().BeTrue();
        WindowSnapshot.Of(1, s_a, s_b).HasSameWindows(WindowSnapshot.Of(1, s_b, s_a)).Should().BeFalse();
        WindowSnapshot.Of(1, s_a).HasSameWindows(WindowSnapshot.Of(1, s_a with { Traits = WindowTraits.Maximized })).Should().BeFalse();
    }

    [Fact]
    public void CopyFrom_MakesAnIndependentCopy()
    {
        var copy = new WindowSnapshot();
        WindowSnapshot source = WindowSnapshot.Of(3, s_a, s_b);

        copy.CopyFrom(source);
        source.Clear();

        copy.Count.Should().Be(2);
        copy.Timestamp.Should().Be(3);
        copy[1].Should().Be(s_b);
    }

    [Fact]
    public void IndexOf_FindsWindowsById()
    {
        WindowSnapshot snapshot = WindowSnapshot.Of(0, s_a, s_b);

        snapshot.IndexOf(2).Should().Be(1);
        snapshot.IndexOf(9).Should().Be(-1);
    }
}
