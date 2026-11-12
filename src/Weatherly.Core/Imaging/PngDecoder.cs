using System.Buffers.Binary;
using System.IO.Compression;

namespace Weatherly.Core.Imaging;

/// <summary>A decoded image in the renderer's format: BGRA, premultiplied alpha, rows top to bottom.</summary>
public sealed record DecodedImage(int Width, int Height, byte[] PremultipliedBgra);

/// <summary>
/// Reads 8-bit RGBA and RGB PNG files without interlacing, which covers everything Weatherly writes
/// (golden images, previews). All five row filters are supported.
/// </summary>
public static class PngDecoder
{
    private static readonly byte[] s_signature = [0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A];

    public static DecodedImage Decode(ReadOnlySpan<byte> png)
    {
        if (png.Length < 8 || !png[..8].SequenceEqual(s_signature))
        {
            throw new InvalidDataException("Not a PNG file.");
        }

        int width = 0;
        int height = 0;
        int channels = 0;
        using var compressed = new MemoryStream();
        int offset = 8;
        while (offset + 8 <= png.Length)
        {
            int length = BinaryPrimitives.ReadInt32BigEndian(png[offset..]);
            ReadOnlySpan<byte> type = png.Slice(offset + 4, 4);
            ReadOnlySpan<byte> data = png.Slice(offset + 8, length);

            if (type.SequenceEqual("IHDR"u8))
            {
                width = BinaryPrimitives.ReadInt32BigEndian(data);
                height = BinaryPrimitives.ReadInt32BigEndian(data[4..]);
                byte bitDepth = data[8];
                byte colorType = data[9];
                byte interlace = data[12];
                channels = colorType switch
                {
                    6 => 4,
                    2 => 3,
                    _ => throw new NotSupportedException($"PNG color type {colorType} is not supported."),
                };
                if (bitDepth != 8 || interlace != 0)
                {
                    throw new NotSupportedException("Only 8-bit, non-interlaced PNG files are supported.");
                }
            }
            else if (type.SequenceEqual("IDAT"u8))
            {
                compressed.Write(data);
            }
            else if (type.SequenceEqual("IEND"u8))
            {
                break;
            }

            offset += 12 + length;
        }

        if (width <= 0 || height <= 0)
        {
            throw new InvalidDataException("The PNG file has no image header.");
        }

        byte[] raw = Inflate(compressed.ToArray(), (((width * channels) + 1) * height));
        return new DecodedImage(width, height, Unfilter(raw, width, height, channels));
    }

    private static byte[] Inflate(byte[] compressed, int expectedLength)
    {
        using var zlib = new ZLibStream(new MemoryStream(compressed), CompressionMode.Decompress);
        byte[] raw = new byte[expectedLength];
        zlib.ReadExactly(raw);
        return raw;
    }

    private static byte[] Unfilter(byte[] raw, int width, int height, int channels)
    {
        int stride = width * channels;
        byte[] current = new byte[stride];
        byte[] previous = new byte[stride];
        byte[] output = new byte[width * height * 4];

        for (int y = 0; y < height; y++)
        {
            int rowStart = y * (stride + 1);
            byte filter = raw[rowStart];
            for (int i = 0; i < stride; i++)
            {
                byte value = raw[rowStart + 1 + i];
                int left = i >= channels ? current[i - channels] : 0;
                int up = previous[i];
                int upLeft = i >= channels ? previous[i - channels] : 0;
                current[i] = filter switch
                {
                    0 => value,
                    1 => (byte)(value + left),
                    2 => (byte)(value + up),
                    3 => (byte)(value + ((left + up) / 2)),
                    4 => (byte)(value + Paeth(left, up, upLeft)),
                    _ => throw new InvalidDataException($"Unknown PNG row filter {filter}."),
                };
            }

            for (int x = 0; x < width; x++)
            {
                int source = x * channels;
                byte alpha = channels == 4 ? current[source + 3] : (byte)255;
                int target = ((y * width) + x) * 4;
                output[target] = Premultiply(current[source + 2], alpha);
                output[target + 1] = Premultiply(current[source + 1], alpha);
                output[target + 2] = Premultiply(current[source], alpha);
                output[target + 3] = alpha;
            }

            (previous, current) = (current, previous);
        }

        return output;
    }

    private static byte Premultiply(byte channel, byte alpha) => (byte)(((channel * alpha) + 127) / 255);

    private static int Paeth(int a, int b, int c)
    {
        int p = a + b - c;
        int pa = Math.Abs(p - a);
        int pb = Math.Abs(p - b);
        int pc = Math.Abs(p - c);
        return pa <= pb && pa <= pc ? a : pb <= pc ? b : c;
    }
}
