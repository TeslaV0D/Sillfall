using System.Buffers.Binary;
using System.IO.Compression;
using System.Text;
using Weatherly.Core.Imaging;

namespace Weatherly.Core.Tests.Imaging;

public class PngEncoderTests
{
    [Fact]
    public void Encode_WritesSignatureAndHeader()
    {
        byte[] png = PngEncoder.Encode(new byte[3 * 2 * 4], width: 3, height: 2);

        png.AsSpan(0, 8).ToArray().Should().Equal(0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A);

        (string type, byte[] data) = ReadChunks(png)[0];
        type.Should().Be("IHDR");
        BinaryPrimitives.ReadInt32BigEndian(data).Should().Be(3);
        BinaryPrimitives.ReadInt32BigEndian(data.AsSpan(4)).Should().Be(2);
        data.AsSpan(8).ToArray().Should().Equal(8, 6, 0, 0, 0);
    }

    [Fact]
    public void Encode_ChunkSequenceIsHeaderDataEnd()
    {
        byte[] png = PngEncoder.Encode(new byte[4], width: 1, height: 1);

        ReadChunks(png).Select(chunk => chunk.Type).Should().Equal("IHDR", "IDAT", "IEND");
    }

    [Fact]
    public void Encode_ConvertsPremultipliedBgraToStraightRgba()
    {
        // Two pixels: opaque orange, and red at 50 % alpha (premultiplied: red channel 128).
        byte[] bgra =
        [
            0, 128, 255, 255,
            0, 0, 128, 128,
        ];

        byte[] rows = Decode(PngEncoder.Encode(bgra, width: 2, height: 1));

        rows.Should().Equal(
            0, // filter type of the row
            255, 128, 0, 255,
            255, 0, 0, 128);
    }

    [Fact]
    public void Encode_FullyTransparentPixelsBecomeTransparentBlack()
    {
        byte[] rows = Decode(PngEncoder.Encode([0, 0, 0, 0], width: 1, height: 1));

        rows.Should().Equal(0, 0, 0, 0, 0);
    }

    [Fact]
    public void Encode_KeepsRowOrder()
    {
        byte[] bgra =
        [
            255, 0, 0, 255, // top: blue
            0, 255, 0, 255, // bottom: green
        ];

        byte[] rows = Decode(PngEncoder.Encode(bgra, width: 1, height: 2));

        rows.Should().Equal(
            0, 0, 0, 255, 255,
            0, 0, 255, 0, 255);
    }

    [Fact]
    public void Encode_ChecksumMatchesTheReferenceValue()
    {
        byte[] png = PngEncoder.Encode(new byte[4], width: 1, height: 1);

        // The IEND chunk is identical in every PNG file, its CRC is a fixed, well-known value.
        png.AsSpan(png.Length - 4).ToArray().Should().Equal(0xAE, 0x42, 0x60, 0x82);
    }

    [Fact]
    public void Encode_RejectsBufferOfWrongSize()
    {
        Action encode = () => PngEncoder.Encode(new byte[7], width: 2, height: 1);

        encode.Should().Throw<ArgumentException>();
    }

    [Theory]
    [InlineData(0, 0, 0)]
    [InlineData(128, 128, 255)]
    [InlineData(64, 128, 128)]
    [InlineData(255, 255, 255)]
    [InlineData(200, 100, 255)] // invalid premultiplied input is clamped instead of wrapping around
    public void Unpremultiply_DividesByAlpha(byte channel, byte alpha, byte expected)
    {
        PngEncoder.Unpremultiply(channel, alpha).Should().Be(expected);
    }

    private static List<(string Type, byte[] Data)> ReadChunks(byte[] png)
    {
        var chunks = new List<(string, byte[])>();
        int offset = 8;
        while (offset < png.Length)
        {
            int length = BinaryPrimitives.ReadInt32BigEndian(png.AsSpan(offset));
            string type = Encoding.ASCII.GetString(png, offset + 4, 4);
            chunks.Add((type, png.AsSpan(offset + 8, length).ToArray()));
            offset += 12 + length;
        }

        return chunks;
    }

    /// <summary>Returns the raw scanlines: per row one filter byte followed by RGBA pixels.</summary>
    private static byte[] Decode(byte[] png)
    {
        byte[] compressed = ReadChunks(png).Single(chunk => chunk.Type == "IDAT").Data;
        using var zlib = new ZLibStream(new MemoryStream(compressed), CompressionMode.Decompress);
        using var rows = new MemoryStream();
        zlib.CopyTo(rows);
        return rows.ToArray();
    }
}
