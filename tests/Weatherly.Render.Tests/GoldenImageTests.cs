using Weatherly.Core.Geometry;
using Weatherly.Core.Imaging;
using Weatherly.Core.Overlay;
using Weatherly.Core.Previews;

namespace Weatherly.Render.Tests;

/// <summary>
/// Renders the FrameDump scenes with WARP and compares every captured frame with its golden image in
/// Goldens/. A missing golden fails the test and leaves the rendered frame in the preview folder, so it
/// can be reviewed and then committed as the new golden.
/// </summary>
public sealed class GoldenImageTests : IDisposable
{
    /// <summary>Largest per-channel difference that still counts as equal (WARP builds may round differently).</summary>
    private const int ChannelTolerance = 2;

    /// <summary>Fraction of pixels allowed beyond the tolerance.</summary>
    private const double MaxDifferingFraction = 0.001;

    private readonly HeadlessRenderer _renderer = new();

    public static TheoryData<string> Scenes => new(
        Directory.GetFiles(Path.Combine(AppContext.BaseDirectory, "Scenes"), "*.json")
            .Select(Path.GetFileNameWithoutExtension)
            .Order(StringComparer.Ordinal)!);

    [Theory]
    [MemberData(nameof(Scenes))]
    public void Scene_MatchesItsGoldenImages(string sceneName)
    {
        PreviewScene scene = PreviewScene.Parse(File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "Scenes", $"{sceneName}.json")));
        var size = new PixelSize(scene.Monitor.Width, scene.Monitor.Height);
        PixelRect monitor = PreviewSceneRunner.MonitorOf(scene).Bounds;
        var failures = new List<string>();

        PreviewSceneRunner.Run(scene, capture =>
        {
            byte[] actual = _renderer.Render(size, new OverlayFrame(
                OverlayContent.None, scene.Monitor.Scale, monitor, scene.ShowSurfaces ? capture.Surfaces : null, capture.Scene));
            string name = $"{scene.Name}-{capture.Index:00}";
            var image = new RenderedImage(actual, size);
            image.SavePreview($"golden-actual-{name}");

            string goldenPath = Path.Combine(AppContext.BaseDirectory, "Goldens", $"{name}.png");
            if (!File.Exists(goldenPath))
            {
                failures.Add($"{name}: no golden image. Review previews/golden-actual-{name}.png and commit it as tests/Weatherly.Render.Tests/Goldens/{name}.png.");
                return;
            }

            DecodedImage golden = PngDecoder.Decode(File.ReadAllBytes(goldenPath));
            if (golden.Width != size.Width || golden.Height != size.Height)
            {
                failures.Add($"{name}: golden is {golden.Width}x{golden.Height}, frame is {size}.");
                return;
            }

            ImageDifference difference = ImageComparison.Compare(golden.PremultipliedBgra, actual, ChannelTolerance);
            if (difference.DifferingFraction > MaxDifferingFraction)
            {
                failures.Add($"{name}: {difference.DifferingPixels} of {difference.TotalPixels} pixels differ (max channel difference {difference.MaxChannelDifference}).");
            }
        });

        failures.Should().BeEmpty();
    }

    public void Dispose() => _renderer.Dispose();
}
