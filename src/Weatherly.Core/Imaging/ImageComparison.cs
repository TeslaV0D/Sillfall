namespace Weatherly.Core.Imaging;

/// <summary>Result of comparing a rendered frame with a golden image.</summary>
/// <param name="MaxChannelDifference">Largest difference of any channel of any pixel.</param>
/// <param name="DifferingPixels">Pixels whose largest channel difference exceeds the tolerance.</param>
/// <param name="TotalPixels">Pixels compared.</param>
public sealed record ImageDifference(int MaxChannelDifference, int DifferingPixels, int TotalPixels)
{
    public double DifferingFraction => TotalPixels == 0 ? 0 : DifferingPixels / (double)TotalPixels;
}

/// <summary>Pixel-wise comparison with a tolerance, for golden-image tests.</summary>
public static class ImageComparison
{
    public static ImageDifference Compare(ReadOnlySpan<byte> expected, ReadOnlySpan<byte> actual, int channelTolerance)
    {
        if (expected.Length != actual.Length || expected.Length % 4 != 0)
        {
            throw new ArgumentException("Both images must have the same size in BGRA.");
        }

        int max = 0;
        int differing = 0;
        for (int i = 0; i < expected.Length; i += 4)
        {
            int pixelMax = 0;
            for (int c = 0; c < 4; c++)
            {
                pixelMax = Math.Max(pixelMax, Math.Abs(expected[i + c] - actual[i + c]));
            }

            max = Math.Max(max, pixelMax);
            if (pixelMax > channelTolerance)
            {
                differing++;
            }
        }

        return new ImageDifference(max, differing, expected.Length / 4);
    }
}
