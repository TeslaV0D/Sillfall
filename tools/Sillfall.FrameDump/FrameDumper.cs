using Sillfall.Core.Geometry;
using Sillfall.Core.Imaging;
using Sillfall.Core.Overlay;
using Sillfall.Core.Previews;
using Sillfall.Render;

namespace Sillfall.FrameDump;

/// <summary>Plays a scene and writes its captured frames and a contact sheet as PNG files.</summary>
internal static class FrameDumper
{
    private const int SheetColumns = 3;

    public static IReadOnlyList<string> Dump(PreviewScene scene, string outputDirectory, HeadlessRenderer renderer)
    {
        Directory.CreateDirectory(outputDirectory);
        var size = new PixelSize(scene.Monitor.Width, scene.Monitor.Height);
        PixelRect monitor = PreviewSceneRunner.MonitorOf(scene).Bounds;
        var frames = new List<byte[]>();
        var written = new List<string>();

        PreviewSceneRunner.Run(scene, capture =>
        {
            var frame = new OverlayFrame(
                OverlayContent.None,
                scene.Monitor.Scale,
                monitor,
                scene.ShowSurfaces ? capture.Surfaces : null,
                capture.Scene);
            byte[] pixels = renderer.Render(size, frame);
            frames.Add(pixels);

            string path = Path.Combine(outputDirectory, $"{scene.Name}-{capture.Index:00}-{capture.Time:0.00}s.png".Replace(',', '.'));
            File.WriteAllBytes(path, PngEncoder.Encode(pixels, size.Width, size.Height));
            written.Add(path);
        });

        if (frames.Count > 0)
        {
            DecodedImage sheet = ContactSheet.Compose(frames, size.Width, size.Height, Math.Min(SheetColumns, frames.Count));
            string path = Path.Combine(outputDirectory, $"{scene.Name}-sheet.png");
            File.WriteAllBytes(path, PngEncoder.Encode(sheet.PremultipliedBgra, sheet.Width, sheet.Height));
            written.Add(path);
        }

        return written;
    }
}
