namespace Weatherly.Core.Imaging;

/// <summary>
/// Puts several frames side by side on a checkerboard, so transparent overlay content is visible in
/// one image. Frames are premultiplied BGRA of equal size. The result is premultiplied BGRA too.
/// </summary>
public static class ContactSheet
{
    private const int Gap = 8;
    private const int Checker = 16;

    public static DecodedImage Compose(IReadOnlyList<byte[]> frames, int width, int height, int columns)
    {
        ArgumentNullException.ThrowIfNull(frames);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(columns);
        if (frames.Count == 0)
        {
            throw new ArgumentException("At least one frame is needed.", nameof(frames));
        }

        int rows = (frames.Count + columns - 1) / columns;
        int sheetWidth = (columns * width) + ((columns + 1) * Gap);
        int sheetHeight = (rows * height) + ((rows + 1) * Gap);
        byte[] sheet = new byte[sheetWidth * sheetHeight * 4];

        for (int y = 0; y < sheetHeight; y++)
        {
            for (int x = 0; x < sheetWidth; x++)
            {
                // Two muted desktop-like tones: dark enough for light effects, light enough for dark ones.
                bool light = ((x / Checker) + (y / Checker)) % 2 == 0;
                int i = ((y * sheetWidth) + x) * 4;
                sheet[i] = light ? (byte)150 : (byte)120;
                sheet[i + 1] = light ? (byte)130 : (byte)104;
                sheet[i + 2] = light ? (byte)110 : (byte)88;
                sheet[i + 3] = 255;
            }
        }

        for (int f = 0; f < frames.Count; f++)
        {
            int originX = Gap + ((f % columns) * (width + Gap));
            int originY = Gap + ((f / columns) * (height + Gap));
            byte[] frame = frames[f];
            for (int y = 0; y < height; y++)
            {
                for (int x = 0; x < width; x++)
                {
                    int source = ((y * width) + x) * 4;
                    int target = (((originY + y) * sheetWidth) + originX + x) * 4;
                    int inverseAlpha = 255 - frame[source + 3];
                    for (int c = 0; c < 3; c++)
                    {
                        sheet[target + c] = (byte)Math.Min(255, frame[source + c] + ((sheet[target + c] * inverseAlpha) + 127) / 255);
                    }
                }
            }
        }

        return new DecodedImage(sheetWidth, sheetHeight, sheet);
    }
}
