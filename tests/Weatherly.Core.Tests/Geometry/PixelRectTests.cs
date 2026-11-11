using Weatherly.Core.Geometry;

namespace Weatherly.Core.Tests.Geometry;

public class PixelRectTests
{
    [Fact]
    public void FromSize_ComputesExclusiveRightAndBottom()
    {
        PixelRect rect = PixelRect.FromSize(-1920, 100, 1920, 1080);

        rect.Should().Be(new PixelRect(-1920, 100, 0, 1180));
        rect.Size.Should().Be(new PixelSize(1920, 1080));
    }

    [Theory]
    [InlineData(0, 0, true)]
    [InlineData(99, 49, true)]
    [InlineData(100, 49, false)]
    [InlineData(99, 50, false)]
    [InlineData(-1, 0, false)]
    public void Contains_TreatsRightAndBottomAsExclusive(int x, int y, bool expected)
    {
        new PixelRect(0, 0, 100, 50).Contains(x, y).Should().Be(expected);
    }

    [Theory]
    [InlineData(0, 0, 10, 10, false)]
    [InlineData(0, 0, 0, 10, true)]
    [InlineData(0, 0, 10, 0, true)]
    [InlineData(10, 10, 5, 20, true)]
    public void IsEmpty_IsTrueWithoutArea(int left, int top, int right, int bottom, bool expected)
    {
        new PixelRect(left, top, right, bottom).IsEmpty.Should().Be(expected);
    }

    [Fact]
    public void PixelSize_IsEmptyWithoutArea()
    {
        new PixelSize(0, 10).IsEmpty.Should().BeTrue();
        new PixelSize(10, -1).IsEmpty.Should().BeTrue();
        new PixelSize(1, 1).IsEmpty.Should().BeFalse();
    }
}
