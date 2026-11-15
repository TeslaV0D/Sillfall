using Sillfall.Core.Imaging;

namespace Sillfall.Core.Tests.Imaging;

public class BackdropTests
{
    [Fact]
    public void Flatten_TransparentPixel_TakesTheBackdrop()
    {
        byte[] pixel = [0, 0, 0, 0];

        Backdrop.Flatten(pixel, red: 32, green: 32, blue: 32);

        pixel.Should().Equal(32, 32, 32, 255);
    }

    [Fact]
    public void Flatten_OpaquePixel_StaysAsItIs()
    {
        byte[] pixel = [10, 20, 30, 255];

        Backdrop.Flatten(pixel, 200, 200, 200);

        pixel.Should().Equal(10, 20, 30, 255);
    }

    [Fact]
    public void Flatten_WhiteTextAtHalfAlpha_StaysReadableOnDark()
    {
        // Premultiplied white at 50 %: 128 in every channel.
        byte[] pixel = [128, 128, 128, 128];

        Backdrop.Flatten(pixel, 32, 32, 32);

        pixel.Should().Equal(144, 144, 144, 255);
    }
}
