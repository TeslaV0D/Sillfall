using System.Numerics;
using Weatherly.Core.Desktop;
using Weatherly.Core.Displays;
using Weatherly.Core.Effects;
using Weatherly.Core.Geometry;
using Weatherly.Core.Rendering;
using Weatherly.Core.Surfaces;
using Weatherly.Core.Visitors;

namespace Weatherly.Core.Tests.Visitors;

public class VisitorSystemTests
{
    private const double FrameTime = 1.0 / 60;

    private static readonly MonitorInfo s_monitor = new(
        @"\\.\DISPLAY1", PixelRect.FromSize(0, 0, 1920, 1080), PixelRect.FromSize(0, 0, 1920, 1032), MonitorInfo.DefaultDpi, IsPrimary: true);

    private static readonly TrackedWindow s_window = new(7, PixelRect.FromSize(400, 300, 600, 400));

    /// <summary>A second window to the right, a little lower, within jumping distance of the first.</summary>
    private static readonly TrackedWindow s_neighbour = new(8, PixelRect.FromSize(1080, 340, 500, 300));

    private static readonly SurfaceKey s_top = new(7, SurfaceEdge.Top);

    [Fact]
    public void Spawn_PutsTheVisitorOnAVisibleHorizontalEdge()
    {
        for (ulong seed = 1; seed <= 20; seed++)
        {
            (SimulationEngine engine, VisitorSystem visitors) = Create(seed: seed);

            visitors.Spawn(VisitorKind.Tuck, engine.Context).Should().BeTrue();

            VisitorInfo visitor = visitors[0];
            visitor.Surface!.Value.Edge.Should().BeOneOf(SurfaceEdge.Top, SurfaceEdge.Floor);
            visitor.Feet.Y.Should().BeOneOf(300f, 1032f);
            visitor.State.Should().Be(VisitorState.Entering);
        }
    }

    [Fact]
    public void Spawn_WithoutRoom_Fails()
    {
        (SimulationEngine engine, VisitorSystem visitors) = Create();
        for (int i = 0; i < VisitorSystem.Capacity; i++)
        {
            visitors.Spawn(VisitorKind.Nim, engine.Context).Should().BeTrue();
        }

        visitors.Spawn(VisitorKind.Nim, engine.Context).Should().BeFalse();
    }

    [Fact]
    public void Visitor_PopsIn_ThenWalksAlongItsEdge()
    {
        (SimulationEngine engine, VisitorSystem visitors) = Create();
        visitors.SpawnAt(VisitorKind.Sol, engine.Context, s_top, 700).Should().BeTrue();

        Run(engine, VisitorSystem.EnterSeconds + 0.1);
        visitors[0].State.Should().BeOneOf(VisitorState.Walking, VisitorState.Idle);

        var positions = new List<float>();
        for (int i = 0; i < 30; i++)
        {
            Run(engine, 0.5);
            positions.Add(visitors[0].Feet.X);
            visitors[0].Feet.Y.Should().Be(300f, "it stays on the window's top edge");
        }

        positions.Should().OnlyContain(x => x >= 400f && x <= 1000f);
        positions.Distinct().Should().HaveCountGreaterThan(5, "it walks");
    }

    [Fact]
    public void Visitor_RidesAlongWhenItsWindowMoves()
    {
        (SimulationEngine engine, VisitorSystem visitors) = Create();
        visitors.SpawnAt(VisitorKind.Pip, engine.Context, s_top, 700);
        Run(engine, 0.2);
        float before = visitors[0].Feet.X;

        var moved = new TrackedWindow(7, PixelRect.FromSize(500, 250, 600, 400));
        engine.SetWindows(WindowSnapshot.Of(1, moved));
        Run(engine, FrameTime);

        visitors[0].Feet.Y.Should().Be(250f);
        visitors[0].Feet.X.Should().BeApproximately(before + 100f, 2f, "Pip is slow: the window carried it 100 px");
    }

    [Fact]
    public void ClosingTheWindow_DropsTheVisitorOntoTheTaskbar()
    {
        (SimulationEngine engine, VisitorSystem visitors) = Create();
        visitors.SpawnAt(VisitorKind.Tuck, engine.Context, s_top, 700);
        Run(engine, 0.2);

        engine.SetWindows(WindowSnapshot.Of(1));
        Run(engine, FrameTime);
        visitors[0].State.Should().Be(VisitorState.Airborne);

        Run(engine, 2);
        visitors[0].Surface.Should().Be(new SurfaceKey(0, SurfaceEdge.Floor));
        visitors[0].Feet.Y.Should().Be(1032f);
    }

    [Fact]
    public void ShakingTheWindow_ThrowsTheVisitorOff()
    {
        (SimulationEngine engine, VisitorSystem visitors) = Create();
        visitors.SpawnAt(VisitorKind.Bristle, engine.Context, s_top, 700);
        Run(engine, 0.2);

        visitors.OnWindowEvent(new WindowEvent(WindowEventKind.Shaken, 7, s_window.Bounds, s_window.Bounds, Vector2.Zero), engine.Context);

        visitors[0].State.Should().Be(VisitorState.Airborne);
        visitors[0].Surface.Should().BeNull();
    }

    [Fact]
    public void AtTheEndOfItsEdge_TheVisitorJumpsToTheNeighbour()
    {
        (SimulationEngine engine, VisitorSystem visitors) = Create(windows: [s_window, s_neighbour]);
        visitors.SpawnAt(VisitorKind.Drift, engine.Context, s_top, 960);
        Run(engine, VisitorSystem.EnterSeconds + 0.05);

        bool reached = false;
        for (int i = 0; i < 600 && !reached; i++)
        {
            // Keep it heading right, as if it had decided to explore.
            ForceFacingRight(visitors, engine);
            Run(engine, 0.05);
            reached = visitors.Count > 0 && visitors[0].Surface == new SurfaceKey(8, SurfaceEdge.Top);
        }

        reached.Should().BeTrue("the neighbour's top edge is within reach");
        visitors[0].Feet.Y.Should().Be(340f);
    }

    [Fact]
    public void AtTheEndOfItsEdge_WithoutANeighbour_TheVisitorTurnsAround()
    {
        (SimulationEngine engine, VisitorSystem visitors) = Create();
        visitors.SpawnAt(VisitorKind.Drift, engine.Context, s_top, 990);

        Run(engine, 20);

        visitors[0].Surface.Should().Be(s_top, "there is nothing within reach, so it stays on its window");
    }

    [Fact]
    public void Visitor_LeavesAfterItsVisit()
    {
        (SimulationEngine engine, VisitorSystem visitors) = Create();
        visitors.SpawnAt(VisitorKind.Nim, engine.Context, s_top, 700);

        Run(engine, VisitorSystem.MaxVisitSeconds + VisitorSystem.LeaveSeconds + 5);

        visitors.Count.Should().Be(0);
        visitors.IsActive.Should().BeFalse("the render loop may stop again");
    }

    [Fact]
    public void DroppedIntoTheBox_IsCollected()
    {
        (SimulationEngine engine, VisitorSystem visitors) = Create();
        visitors.SpawnAt(VisitorKind.Sprout, engine.Context, s_top, 700);
        Run(engine, 1);
        int id = visitors[0].Id;

        visitors.PickUp(id).Should().BeTrue();
        visitors.Drag(id, new Vector2(1800, 900));
        Run(engine, 0.5);
        visitors[0].State.Should().Be(VisitorState.Held);
        visitors[0].Feet.X.Should().Be(1800f, "a held visitor hangs where the pointer is");

        visitors.Release(id, overBox: true).Should().Be(VisitorKind.Sprout);
        visitors.Count.Should().Be(0);
    }

    [Fact]
    public void DroppedElsewhere_FallsSulksAndLeaves()
    {
        (SimulationEngine engine, VisitorSystem visitors) = Create();
        visitors.SpawnAt(VisitorKind.Sol, engine.Context, s_top, 700);
        Run(engine, 1);
        int id = visitors[0].Id;
        visitors.PickUp(id);
        visitors.Drag(id, new Vector2(200, 500));

        visitors.Release(id, overBox: false).Should().BeNull();
        Run(engine, 1.5);
        visitors[0].State.Should().Be(VisitorState.Sulking);
        engine.Frame.Visitors[0].Expression.Should().Be(VisitorExpression.Sulky);

        Run(engine, 5);
        visitors.Count.Should().Be(0, "an offended visitor does not stay");
    }

    [Fact]
    public void CollectOldest_TakesTheFirstVisitor()
    {
        (SimulationEngine engine, VisitorSystem visitors) = Create();
        visitors.SpawnAt(VisitorKind.Lumen, engine.Context, s_top, 600);
        visitors.SpawnAt(VisitorKind.Tuck, engine.Context, s_top, 900);

        visitors.CollectOldest().Should().Be(VisitorKind.Lumen);
        visitors.Count.Should().Be(1);
    }

    [Fact]
    public void Emit_DescribesEveryVisitor()
    {
        (SimulationEngine engine, VisitorSystem visitors) = Create();
        visitors.SpawnAt(VisitorKind.Lumen, engine.Context, s_top, 700);
        Run(engine, 2);

        VisitorPose pose = engine.Frame.Visitors.ToArray().Should().ContainSingle().Subject;
        pose.Kind.Should().Be(VisitorKind.Lumen);
        pose.Height.Should().BeApproximately(VisitorCatalog.StandardHeight * 0.85f, 0.01f);
        pose.Alpha.Should().Be(1f);
        pose.Bob.Should().BeGreaterThan(2f, "Lumen hovers");
        pose.Facing.Should().BeOneOf(-1f, 1f);
    }

    [Fact]
    public void Weather_BringsOutTheProp_AndFavouriteWeatherMakesHappy()
    {
        (SimulationEngine engine, VisitorSystem visitors) = Create();
        visitors.Mood = new WeatherMood(Rain: 0.8f, 0, 0, 0, 0, 0, 0);
        visitors.SpawnAt(VisitorKind.Sol, engine.Context, s_top, 600);
        visitors.SpawnAt(VisitorKind.Pip, engine.Context, s_top, 900);

        Run(engine, 2);

        VisitorPose sol = engine.Frame.Visitors.ToArray().Single(p => p.Kind == VisitorKind.Sol);
        VisitorPose pip = engine.Frame.Visitors.ToArray().Single(p => p.Kind == VisitorKind.Pip);
        sol.Prop.Should().Be(VisitorProp.Umbrella);
        sol.PropAmount.Should().Be(1f);
        pip.Prop.Should().Be(VisitorProp.None, "Pip loves the rain");
        pip.Expression.Should().Be(VisitorExpression.Happy);
    }

    [Theory]
    [InlineData(VisitorKind.Pip, 0.8f, 0f, 0f, VisitorProp.None)]
    [InlineData(VisitorKind.Tuck, 0.8f, 0f, 0f, VisitorProp.Umbrella)]
    [InlineData(VisitorKind.Nim, 0.8f, 0f, 0f, VisitorProp.None)]
    [InlineData(VisitorKind.Pip, 0f, 0.6f, 0f, VisitorProp.Snowball)]
    [InlineData(VisitorKind.Tuck, 0f, 0f, 0.9f, VisitorProp.Sunglasses)]
    [InlineData(VisitorKind.Sol, 0f, 0f, 0.9f, VisitorProp.None)]
    [InlineData(VisitorKind.Sol, 0.1f, 0.1f, 0.1f, VisitorProp.None)]
    public void PropFor_FollowsTheStrongestWeather(VisitorKind kind, float rain, float snow, float sun, VisitorProp prop)
    {
        VisitorSystem.PropFor(kind, new WeatherMood(rain, snow, 0, 0, sun, 0, 0)).Should().Be(prop);
    }

    [Fact]
    public void FrameLoop_DoesNotAllocateOnceWarm()
    {
        (SimulationEngine engine, VisitorSystem visitors) = Create(windows: [s_window, s_neighbour]);
        visitors.Mood = new WeatherMood(0.5f, 0, 0, 0, 0, 0, 0);
        visitors.SpawnAt(VisitorKind.Drift, engine.Context, s_top, 700);
        visitors.SpawnAt(VisitorKind.Lumen, engine.Context, s_top, 500);
        WindowSnapshot windows = WindowSnapshot.Of(0, s_window, s_neighbour);
        void Frame()
        {
            engine.SetWindows(windows);
            engine.Advance(FrameTime);
        }

        for (int i = 0; i < 600; i++)
        {
            Frame();
        }

        long before = GC.GetAllocatedBytesForCurrentThread();
        for (int i = 0; i < 300; i++)
        {
            Frame();
        }

        (GC.GetAllocatedBytesForCurrentThread() - before).Should().Be(0);
    }

    [Fact]
    public void HighDpi_ScalesSizeAndSpeed()
    {
        var monitor = s_monitor with { Dpi = 192 };
        (SimulationEngine engine, VisitorSystem visitors) = Create(monitor: monitor);
        visitors.SpawnAt(VisitorKind.Tuck, engine.Context, s_top, 700);
        Run(engine, 1);

        engine.Frame.Visitors[0].Height.Should().BeApproximately(VisitorCatalog.StandardHeight * 2f, 0.01f);
    }

    private static (SimulationEngine Engine, VisitorSystem Visitors) Create(ulong seed = 3, TrackedWindow[]? windows = null, MonitorInfo? monitor = null)
    {
        var engine = new SimulationEngine(seed: 5);
        var visitors = new VisitorSystem(seed);
        engine.Add(visitors);
        engine.SetMonitors([monitor ?? s_monitor]);
        engine.SetWindows(WindowSnapshot.Of(0, windows ?? [s_window]));
        engine.Advance(FrameTime);
        return (engine, visitors);
    }

    private static void Run(SimulationEngine engine, double seconds)
    {
        int frames = Math.Max(1, (int)Math.Round(seconds / FrameTime));
        for (int i = 0; i < frames; i++)
        {
            engine.Advance(FrameTime);
        }
    }

    /// <summary>Turns a walking visitor to the right by dragging it there and back down: the public API has no steering.</summary>
    private static void ForceFacingRight(VisitorSystem visitors, SimulationEngine engine)
    {
        if (visitors.Count > 0 && visitors[0].State == VisitorState.Idle && visitors[0].Surface == s_top)
        {
            visitors.TurnTowards(visitors[0].Id, 1f);
        }
    }
}
