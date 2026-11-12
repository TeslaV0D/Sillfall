using Sillfall.Core.Imaging;

namespace Sillfall.Core.Tests.Imaging;

public class PngDecoderTests
{
    /// <summary>
    /// 7 x 5 RGBA image written by Pillow with optimization on, so its rows use the Sub and Paeth filters.
    /// Pixel (x, y) = ((37x) % 256, (51y) % 256, (23(x + y)) % 256, alpha 255, or 128 where (x + y) % 3 == 0).
    /// </summary>
    private const string PillowImage =
        "iVBORw0KGgoAAAANSUhEUgAAAAcAAAAFCAYAAACJmvbYAAAAN0lEQVR42oXMwQnAMAwEwQmoh6tAFahAx53nm4Ahv4WBvXC3rBYt+91lsgiy+XaZMxBlzuBv+wDYGxDJ5iSXrwAAAABJRU5ErkJggg==";

    [Fact]
    public void Decode_ForeignFileWithSubAndPaethFilters_RestoresEveryPixel()
    {
        DecodedImage image = PngDecoder.Decode(Convert.FromBase64String(PillowImage));

        image.Width.Should().Be(7);
        image.Height.Should().Be(5);
        for (int y = 0; y < 5; y++)
        {
            for (int x = 0; x < 7; x++)
            {
                byte alpha = (x + y) % 3 == 0 ? (byte)128 : (byte)255;
                byte[] expected =
                [
                    Premultiply((23 * (x + y)) % 256, alpha),
                    Premultiply((51 * y) % 256, alpha),
                    Premultiply((37 * x) % 256, alpha),
                    alpha,
                ];
                image.PremultipliedBgra.AsSpan(((y * 7) + x) * 4, 4).ToArray().Should().Equal(expected, $"pixel ({x}, {y})");
            }
        }
    }

    [Fact]
    public void Decode_OwnEncoderOutput_RoundTripsOpaqueAndTransparentPixelsExactly()
    {
        byte[] bgra =
        [
            10, 20, 30, 255,
            0, 0, 0, 0,
            200, 100, 50, 255,
            1, 2, 3, 255,
        ];

        DecodedImage image = PngDecoder.Decode(PngEncoder.Encode(bgra, 2, 2));

        image.PremultipliedBgra.Should().Equal(bgra);
    }

    [Fact]
    public void Decode_OwnEncoderOutput_KeepsHalfTransparentPixelsWithinOneStep()
    {
        byte[] bgra = [64, 32, 16, 128, 100, 100, 100, 200];

        DecodedImage image = PngDecoder.Decode(PngEncoder.Encode(bgra, 2, 1));

        for (int i = 0; i < bgra.Length; i++)
        {
            ((int)image.PremultipliedBgra[i]).Should().BeInRange(bgra[i] - 1, bgra[i] + 1);
        }
    }

    [Fact]
    public void Decode_NotAPng_Throws()
    {
        Action decode = () => PngDecoder.Decode([1, 2, 3, 4, 5, 6, 7, 8, 9]);

        decode.Should().Throw<InvalidDataException>();
    }

    private static byte Premultiply(int channel, byte alpha) => (byte)(((channel * alpha) + 127) / 255);
}
