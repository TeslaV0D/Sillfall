using Weatherly.Core.Geometry;
using Weatherly.Core.Imaging;

namespace Weatherly.Render.Tests;

/// <summary>One pixel as the renderer stores it: premultiplied alpha.</summary>
internal readonly record struct Pixel(byte Red, byte Green, byte Blue, byte Alpha)
{
    public static readonly Pixel Transparent = new(0, 0, 0, 0);

    public override string ToString() => $"rgba({Red}, {Green}, {Blue}, {Alpha})";
}

/// <summary>A frame read back from the GPU, with pixel access for assertions.</summary>
internal sealed class RenderedImage(byte[] premultipliedBgra, PixelSize size)
{
    public PixelSize Size { get; } = size;

    public Pixel this[int x, int y]
    {
        get
        {
            int offset = ((y * Size.Width) + x) * 4;
            return new Pixel(premultipliedBgra[offset + 2], premultipliedBgra[offset + 1], premultipliedBgra[offset], premultipliedBgra[offset + 3]);
        }
    }

    public IEnumerable<Pixel> Pixels()
    {
        for (int y = 0; y < Size.Height; y++)
        {
            for (int x = 0; x < Size.Width; x++)
            {
                yield return this[x, y];
            }
        }
    }

    /// <summary>
    /// Saves the image as PNG into the preview folder. CI uploads that folder as an artifact, so
    /// rendered output can be reviewed without a Windows desktop.
    /// </summary>
    public void SavePreview(string name)
    {
        string directory = Environment.GetEnvironmentVariable("WEATHERLY_PREVIEW_DIR") is { Length: > 0 } configured
            ? configured
            : Path.Combine(AppContext.BaseDirectory, "previews");

        Directory.CreateDirectory(directory);
        File.WriteAllBytes(Path.Combine(directory, $"{name}.png"), PngEncoder.Encode(premultipliedBgra, Size.Width, Size.Height));
    }
}
