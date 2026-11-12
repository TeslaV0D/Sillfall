using Sillfall.Core.Imaging;

namespace Sillfall.Core.Tests.Imaging;

public class ImageComparisonTests
{
    [Fact]
    public void Compare_IdenticalImages_HaveNoDifference()
    {
        byte[] image = [1, 2, 3, 4, 5, 6, 7, 8];

        ImageDifference difference = ImageComparison.Compare(image, image, channelTolerance: 0);

        difference.Should().Be(new ImageDifference(0, 0, 2));
    }

    [Fact]
    public void Compare_CountsPixelsBeyondTheTolerance()
    {
        byte[] expected = [10, 10, 10, 255, 10, 10, 10, 255, 10, 10, 10, 255];
        byte[] actual = [11, 10, 10, 255, 10, 30, 10, 255, 10, 10, 10, 250];

        ImageDifference difference = ImageComparison.Compare(expected, actual, channelTolerance: 2);

        difference.MaxChannelDifference.Should().Be(20);
        difference.DifferingPixels.Should().Be(2);
        difference.DifferingFraction.Should().BeApproximately(2 / 3.0, 1e-9);
    }

    [Fact]
    public void Compare_DifferentSizes_Throws()
    {
        Action compare = () => ImageComparison.Compare(new byte[8], new byte[4], 0);

        compare.Should().Throw<ArgumentException>();
    }

    [Fact]
    public void ContactSheet_PlacesFramesInAGridOverACheckerboard()
    {
        byte[] opaqueRed = [0, 0, 255, 255, 0, 0, 255, 255, 0, 0, 255, 255, 0, 0, 255, 255];
        byte[] transparent = new byte[16];

        DecodedImage sheet = ContactSheet.Compose([opaqueRed, transparent, opaqueRed], width: 2, height: 2, columns: 2);

        sheet.Width.Should().Be((2 * 2) + (3 * 8));
        sheet.Height.Should().Be((2 * 2) + (3 * 8));
        Pixel(sheet, 8, 8).Should().Equal(0, 0, 255, 255);
        Pixel(sheet, 18, 8).Should().NotEqual([0, 0, 255, 255], "a transparent frame shows the checkerboard");
        Pixel(sheet, 8, 18).Should().Equal(0, 0, 255, 255);
    }

    private static byte[] Pixel(DecodedImage image, int x, int y) =>
        image.PremultipliedBgra.AsSpan(((y * image.Width) + x) * 4, 4).ToArray();
}
