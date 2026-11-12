using System.Buffers.Binary;
using System.IO.Compression;

namespace Sillfall.Core.Imaging;

/// <summary>
/// Minimal PNG writer for rendered frames (previews, golden images). Takes the pixel format of
/// the renderer, BGRA with premultiplied alpha, and writes 8-bit RGBA with straight alpha.
/// </summary>
public static class PngEncoder
{
    private static readonly byte[] s_signature = [0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A];
    private static readonly uint[] s_crcTable = BuildCrcTable();

    /// <summary>Encodes an image to PNG.</summary>
    /// <param name="premultipliedBgra">4 bytes per pixel (blue, green, red, alpha), rows top to bottom without padding.</param>
    /// <param name="width">Image width in pixels.</param>
    /// <param name="height">Image height in pixels.</param>
    public static byte[] Encode(ReadOnlySpan<byte> premultipliedBgra, int width, int height)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(width, 1);
        ArgumentOutOfRangeException.ThrowIfLessThan(height, 1);
        if (premultipliedBgra.Length != (long)width * height * 4)
        {
            throw new ArgumentException($"Expected {(long)width * height * 4} bytes for {width}x{height}, got {premultipliedBgra.Length}.", nameof(premultipliedBgra));
        }

        using var png = new MemoryStream();
        png.Write(s_signature);

        Span<byte> header = stackalloc byte[13];
        BinaryPrimitives.WriteInt32BigEndian(header, width);
        BinaryPrimitives.WriteInt32BigEndian(header[4..], height);
        header[8] = 8; // bits per channel
        header[9] = 6; // color type: RGB with alpha
        header[10] = 0; // compression: deflate
        header[11] = 0; // filter method: adaptive
        header[12] = 0; // no interlace
        WriteChunk(png, "IHDR"u8, header);

        WriteChunk(png, "IDAT"u8, Compress(premultipliedBgra, width, height));
        WriteChunk(png, "IEND"u8, []);
        return png.ToArray();
    }

    /// <summary>Converts one premultiplied channel value back to straight alpha.</summary>
    internal static byte Unpremultiply(byte channel, byte alpha) =>
        alpha == 0 ? (byte)0 : (byte)Math.Min(255, ((channel * 255) + (alpha / 2)) / alpha);

    private static byte[] Compress(ReadOnlySpan<byte> premultipliedBgra, int width, int height)
    {
        using var compressed = new MemoryStream();
        using (var zlib = new ZLibStream(compressed, CompressionLevel.Optimal, leaveOpen: true))
        {
            // Each row: one filter byte (0 = none) followed by RGBA pixels.
            byte[] row = new byte[1 + (width * 4)];
            for (int y = 0; y < height; y++)
            {
                ReadOnlySpan<byte> source = premultipliedBgra.Slice(y * width * 4, width * 4);
                for (int x = 0; x < width; x++)
                {
                    byte alpha = source[(x * 4) + 3];
                    row[1 + (x * 4)] = Unpremultiply(source[(x * 4) + 2], alpha);
                    row[2 + (x * 4)] = Unpremultiply(source[(x * 4) + 1], alpha);
                    row[3 + (x * 4)] = Unpremultiply(source[x * 4], alpha);
                    row[4 + (x * 4)] = alpha;
                }

                zlib.Write(row);
            }
        }

        return compressed.ToArray();
    }

    private static void WriteChunk(Stream stream, ReadOnlySpan<byte> type, ReadOnlySpan<byte> data)
    {
        Span<byte> number = stackalloc byte[4];

        BinaryPrimitives.WriteInt32BigEndian(number, data.Length);
        stream.Write(number);
        stream.Write(type);
        stream.Write(data);

        uint crc = UpdateCrc(UpdateCrc(0xFFFFFFFF, type), data) ^ 0xFFFFFFFF;
        BinaryPrimitives.WriteUInt32BigEndian(number, crc);
        stream.Write(number);
    }

    private static uint UpdateCrc(uint crc, ReadOnlySpan<byte> data)
    {
        foreach (byte value in data)
        {
            crc = s_crcTable[(crc ^ value) & 0xFF] ^ (crc >> 8);
        }

        return crc;
    }

    private static uint[] BuildCrcTable()
    {
        uint[] table = new uint[256];
        for (uint n = 0; n < table.Length; n++)
        {
            uint c = n;
            for (int bit = 0; bit < 8; bit++)
            {
                c = (c & 1) != 0 ? 0xEDB88320 ^ (c >> 1) : c >> 1;
            }

            table[n] = c;
        }

        return table;
    }
}
