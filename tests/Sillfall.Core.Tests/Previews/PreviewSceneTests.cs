using System.Text.Json;
using Sillfall.Core.Geometry;
using Sillfall.Core.Previews;
using Sillfall.Core.Surfaces;

namespace Sillfall.Core.Tests.Previews;

public class PreviewSceneTests
{
    private const string Json = """
        {
          // Comments are allowed in scene files.
          "name": "two-windows",
          "seed": 7,
          "monitor": { "width": 640, "height": 400, "taskbar": 30 },
          "windows": [
            { "id": 2, "x": 300, "y": 80, "width": 220, "height": 140 },
            { "id": 1, "x": 60, "y": 120, "width": 300, "height": 200 }
          ],
          "effects": { "testDrops": 1.0 },
          "wind": { "x": 30 },
          "captures": [0.5, 2.0],
          "events": [ { "at": 1.0, "window": 1, "x": 80, "y": 120 } ],
        }
        """;

    [Fact]
    public void Parse_ReadsAllFields()
    {
        PreviewScene scene = PreviewScene.Parse(Json);

        scene.Name.Should().Be("two-windows");
        scene.Seed.Should().Be(7);
        scene.Monitor.Taskbar.Should().Be(30);
        scene.Windows.Select(window => window.Id).Should().Equal(2, 1);
        scene.Effects.TestDrops.Should().Be(1f);
        scene.Wind.X.Should().Be(30f);
        scene.Wind.Gustiness.Should().Be(0.5f, "missing fields keep their defaults");
        scene.Duration.Should().Be(2.0);
        scene.Events.Single().X.Should().Be(80);
    }

    [Fact]
    public void Parse_UnknownField_IsRejected()
    {
        Action parse = () => PreviewScene.Parse("""{ "nmae": "typo" }""");

        parse.Should().Throw<JsonException>();
    }

    [Fact]
    public void ToJson_RoundTrips()
    {
        PreviewScene scene = PreviewScene.Parse(Json);

        PreviewScene.Parse(scene.ToJson()).Should().BeEquivalentTo(scene);
    }

    [Fact]
    public void Run_CapturesInOrderWithSprites()
    {
        var times = new List<double>();
        var spriteCounts = new List<int>();

        PreviewSceneRunner.Run(PreviewScene.Parse(Json), capture =>
        {
            times.Add(capture.Time);
            spriteCounts.Add(capture.Scene.SpriteCount);
        });

        times.Should().HaveCount(2);
        times[0].Should().BeApproximately(0.5, 1e-9);
        times[1].Should().BeApproximately(2.0, 1e-9);
        spriteCounts.Should().OnlyContain(count => count > 0);
    }

    [Fact]
    public void Run_AppliesScriptedMoves()
    {
        var windowOneLeft = new List<int>();

        PreviewSceneRunner.Run(PreviewScene.Parse(Json), capture =>
        {
            int index = capture.Windows.IndexOf(1);
            windowOneLeft.Add(capture.Windows[index].Bounds.Left);
        });

        windowOneLeft.Should().Equal(60, 80);
    }

    [Fact]
    public void Run_ClosingAWindowRemovesItsSurfaces()
    {
        PreviewScene scene = PreviewScene.Parse(Json) with
        {
            Events = [new PreviewEvent { At = 1.0, Window = 2, Close = true }],
        };
        var topEdges = new List<bool>();

        PreviewSceneRunner.Run(scene, capture =>
        {
            bool found = false;
            foreach (SurfaceSegment segment in capture.Surfaces.Segments)
            {
                found |= segment.Key == new SurfaceKey(2, SurfaceEdge.Top);
            }

            topEdges.Add(found);
        });

        topEdges.Should().Equal(true, false);
    }

    [Fact]
    public void Run_IsDeterministic()
    {
        var first = new List<string>();
        var second = new List<string>();

        PreviewSceneRunner.Run(PreviewScene.Parse(Json), capture => first.Add(Fingerprint(capture)));
        PreviewSceneRunner.Run(PreviewScene.Parse(Json), capture => second.Add(Fingerprint(capture)));

        first.Should().Equal(second);
    }

    [Fact]
    public void MonitorOf_PlacesTheMonitorAtTheOriginWithTheTaskbarAsWorkAreaBottom()
    {
        var monitor = PreviewSceneRunner.MonitorOf(PreviewScene.Parse(Json));

        monitor.Bounds.Size.Should().Be(new PixelSize(640, 400));
        monitor.WorkArea.Bottom.Should().Be(370);
    }

    private static string Fingerprint(PreviewCapture capture)
    {
        var hash = new HashCode();
        foreach (var sprite in capture.Scene.Sprites)
        {
            hash.Add(sprite);
        }

        return $"{capture.Scene.SpriteCount}:{hash.ToHashCode()}";
    }
}
