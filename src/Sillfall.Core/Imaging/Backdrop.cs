namespace Sillfall.Core.Imaging;

/// <summary>Puts a premultiplied BGRA image on an opaque colour, as a window's backdrop would show it.</summary>
public static class Backdrop
{
    /// <summary>
    /// Composites <paramref name="premultipliedBgra"/> over the opaque colour (<paramref name="red"/>, <paramref name="green"/>,
    /// <paramref name="blue"/>) in place; every pixel ends opaque. For pictures of windows with a transparent backdrop
    /// (Mica): without it a viewer shows them over white, with white text on white (finding F-16).
    /// </summary>
    public static void Flatten(Span<byte> premultipliedBgra, byte red, byte green, byte blue)
    {
        for (int i = 0; i + 3 < premultipliedBgra.Length; i += 4)
        {
            int alpha = premultipliedBgra[i + 3];
            int rest = 255 - alpha;
            premultipliedBgra[i] = (byte)Math.Min(255, premultipliedBgra[i] + (((blue * rest) + 127) / 255));
            premultipliedBgra[i + 1] = (byte)Math.Min(255, premultipliedBgra[i + 1] + (((green * rest) + 127) / 255));
            premultipliedBgra[i + 2] = (byte)Math.Min(255, premultipliedBgra[i + 2] + (((red * rest) + 127) / 255));
            premultipliedBgra[i + 3] = 255;
        }
    }
}
