using System.Numerics;
using Sillfall.Core.Desktop;
using Sillfall.Core.Displays;
using Sillfall.Core.Effects;
using Sillfall.Core.Effects.Leaves;
using Sillfall.Core.Geometry;
using Sillfall.Core.Rendering;

namespace Sillfall.Core.Tests.Effects.Leaves;

public class LeafEffectTests
{
    private const double FrameTime = 1.0 / 60;

    private static readonly MonitorInfo s_monitor = new(
        @"\\.\DISPLAY1", PixelRect.FromSize(0, 0, 1920, 1080), PixelRect.FromSize(0, 0, 1920, 1032), MonitorInfo.DefaultDpi, IsPrimary: true);

    private static readonly TrackedWindow s_window = new(7, PixelRect.FromSize(400, 300, 1000, 500));

    [Fact]
    public void Leaves_FallAndStayWithinTheBudget()
    {
        (SimulationEngine engine, LeafEffect leaves) = Create(intensity: 1f);

        Run(engine, seconds: 6);

        leaves.Count.Should().BeInRange(20, LeafEffect.MaxLeaves);
        engine.Frame.Sprites.ToArray().Should().Contain(s => s.Shape == SpriteShape.Leaf);
    }

    [Fact]
    public void Leaves_LighterIntensity_AreFewer()
    {
        (SimulationEngine light, LeafEffect lightLeaves) = Create(intensity: 0.2f);
        (SimulationEngine heavy, LeafEffect heavyLeaves) = Create(intensity: 1f);

        Run(light, seconds: 8);
        Run(heavy, seconds: 8);

        lightLeaves.Count.Should().BeLessThan(heavyLeaves.Count / 2);
    }

    [Fact]
    public void Leaves_LandOnTheWindowAndTheFloor()
    {
        (SimulationEngine engine, LeafEffect leaves) = Create(intensity: 1f);

        Run(engine, seconds: 25);

        leaves.LyingOn(s_window.Id).Should().BePositive();
        leaves.LyingOnFloor(0).Should().BePositive();
    }

    [Fact]
    public void Leaves_SwingFromSideToSideWhileFalling()
    {
        (SimulationEngine engine, LeafEffect leaves) = Create(intensity: 1f);
        engine.Context.Wind.Base = Vector2.Zero;
        engine.Context.Wind.Gustiness = 0f;
        Run(engine, seconds: 5);

        // Without wind, falling leaves still lean in both directions: the flutter.
        float[] angles = [.. engine.Frame.Sprites.ToArray().Where(s => s.Shape == SpriteShape.Leaf).Select(s => s.HalfSize.Y / s.HalfSize.X)];
        angles.Min().Should().BeLessThan(0.3f, "some leaves are seen edge-on while turning over");
        angles.Max().Should().BeGreaterThan(0.5f);
        leaves.FallingCount.Should().BePositive();
    }

    [Fact]
    public void ASurface_HoldsALimitedNumberOfLeavesPer100Pixels()
    {
        (SimulationEngine engine, LeafEffect leaves) = Create(intensity: 1f);

        Run(engine, seconds: 120);

        // 1000 px wide: at most MaxPer100Pixels on each 100 px stretch.
        leaves.LyingOn(s_window.Id).Should().BeInRange(LeafEffect.MaxPer100Pixels * 5, LeafEffect.MaxPer100Pixels * 10);
    }

    [Fact]
    public void LyingLeaves_MoveWithTheirWindow()
    {
        (SimulationEngine engine, LeafEffect leaves) = Create(intensity: 0f);
        leaves.Scatter(engine.Context, 3f);
        int lying = leaves.LyingOn(s_window.Id);
        lying.Should().BePositive();

        for (int i = 1; i <= 30; i++)
        {
            engine.SetWindows(WindowSnapshot.Of(i * FrameTime, s_window with { Bounds = PixelRect.FromSize(400 + (4 * i), 300 - (2 * i), 1000, 500) }));
            engine.Advance(FrameTime);
        }

        leaves.LyingOn(s_window.Id).Should().Be(lying);
        engine.Frame.Sprites.ToArray().Where(s => s.Shape == SpriteShape.Leaf && s.Center.Y < 260).Should().NotBeEmpty("they rose with the window");
    }

    [Fact]
    public void Shaking_ThrowsTheLeavesOff()
    {
        (SimulationEngine engine, LeafEffect leaves) = Create(intensity: 0f);
        leaves.Scatter(engine.Context, 3f);
        leaves.LyingOn(s_window.Id).Should().BePositive();

        int[] offsets = [30, -30, 30, -30];
        for (int i = 0; i < offsets.Length; i++)
        {
            engine.SetWindows(WindowSnapshot.Of(0.05 * (i + 1), s_window with { Bounds = PixelRect.FromSize(400 + offsets[i], 300, 1000, 500) }));
            engine.Advance(0.05);
        }

        leaves.LyingOn(s_window.Id).Should().Be(0);
        Run(engine, seconds: 1);
        leaves.LyingOn(s_window.Id).Should().Be(0, "thrown leaves do not land back on the window they came from");
    }

    [Fact]
    public void ClosingTheWindow_DropsItsLeaves()
    {
        // Barely on, so the lying leaves do not fade while the dropped ones fall.
        (SimulationEngine engine, LeafEffect leaves) = Create(intensity: 1e-6f);
        leaves.Scatter(engine.Context, 1f);
        int onWindow = leaves.LyingOn(s_window.Id);
        int floorBefore = leaves.LyingOnFloor(0);
        onWindow.Should().BePositive();

        engine.SetWindows(WindowSnapshot.Of(0.1));
        Run(engine, seconds: 15);

        leaves.LyingOn(s_window.Id).Should().Be(0);
        leaves.LyingOnFloor(0).Should().BeGreaterThan(floorBefore + (onWindow / 2), "most of them land on the floor below");
    }

    [Fact]
    public void AQuickMouseMovement_BlowsLyingLeavesAway()
    {
        (SimulationEngine engine, LeafEffect leaves) = Create(intensity: 0f);
        leaves.Scatter(engine.Context, 3f);
        int lying = leaves.LyingOn(s_window.Id);

        // Sweeping along the top edge of the window at 2000 px/s.
        for (int i = 0; i < 30; i++)
        {
            engine.SetCursor(new Vector2(400 + (i * 33), 295), new Vector2(2000, 0));
            engine.Advance(FrameTime);
        }

        engine.SetCursor(new Vector2(1400, 295), Vector2.Zero);
        leaves.LyingOn(s_window.Id).Should().BeLessThan(lying / 2);
        leaves.FallingCount.Should().BePositive();
    }

    [Fact]
    public void ASlowMouseMovement_LeavesThemAlone()
    {
        (SimulationEngine engine, LeafEffect leaves) = Create(intensity: 0f);
        leaves.Scatter(engine.Context, 3f);
        int lying = leaves.LyingOn(s_window.Id);

        for (int i = 0; i < 30; i++)
        {
            engine.SetCursor(new Vector2(400 + (i * 5), 295), new Vector2(300, 0));
            engine.Advance(FrameTime);
        }

        leaves.LyingOn(s_window.Id).Should().Be(lying);
    }

    [Fact]
    public void LyingLeaves_FadeAndTheEffectStopsAfterIntensityZero()
    {
        (SimulationEngine engine, LeafEffect leaves) = Create(intensity: 1f);
        Run(engine, seconds: 20);
        leaves.LyingOn(s_window.Id).Should().BePositive();

        leaves.Settings = new EffectSettings { Intensity = 0f };
        Run(engine, seconds: LeafEffect.FadeSeconds + 15);

        leaves.IsActive.Should().BeFalse();
        engine.IsActive.Should().BeFalse();
    }

    [Fact]
    public void FarLeaves_AreHiddenBehindWindows()
    {
        (SimulationEngine engine, LeafEffect leaves) = Create(intensity: 1f);
        Run(engine, seconds: 12);

        engine.Frame.Sprites.ToArray()
            .Where(s => s.Shape == SpriteShape.Leaf && s.HalfSize.X < 5.2f)
            .Should().NotContain(s => s.Center.Y > 320 && s_window.Bounds.Contains((int)s.Center.X, (int)s.Center.Y));
        leaves.Count.Should().BePositive();
    }

    [Fact]
    public void CustomColor_ReplacesTheAutumnPalette()
    {
        (SimulationEngine engine, LeafEffect leaves) = Create(intensity: 1f);
        leaves.Settings = new EffectSettings { Intensity = 1f, Color = new Vector3(0f, 0f, 1f) };

        Run(engine, seconds: 4);

        engine.Frame.Sprites.ToArray().Where(s => s.Shape == SpriteShape.Leaf).Should().OnlyContain(s => s.Color.X == 0f && s.Color.Y == 0f && s.Color.Z > 0f);
    }

    [Fact]
    public void Simulation_IsDeterministic()
    {
        (SimulationEngine first, _) = Create(intensity: 0.7f);
        (SimulationEngine second, _) = Create(intensity: 0.7f);

        Run(first, seconds: 6);
        Run(second, seconds: 6);

        first.Frame.Sprites.ToArray().Should().Equal(second.Frame.Sprites.ToArray());
    }

    [Fact]
    public void FrameLoop_DoesNotAllocateOnceWarm()
    {
        (SimulationEngine engine, LeafEffect leaves) = Create(intensity: 1f);
        leaves.Scatter(engine.Context, 2f);
        WindowSnapshot windows = WindowSnapshot.Of(0, s_window);
        void Frame()
        {
            engine.SetWindows(windows);
            engine.SetCursor(new Vector2(800, 290), new Vector2(900, 0));
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
    public void HighDpi_ScalesTheLeaves()
    {
        (SimulationEngine normal, _) = Create(intensity: 1f);
        (SimulationEngine doubled, _) = Create(intensity: 1f, monitor: s_monitor with { Dpi = 192 });

        Run(normal, seconds: 4);
        Run(doubled, seconds: 4);

        static float Largest(SimulationEngine engine) =>
            engine.Frame.Sprites.ToArray().Where(s => s.Shape == SpriteShape.Leaf).Max(s => s.HalfSize.X);

        Largest(doubled).Should().BeApproximately(2 * Largest(normal), 1.5f);
    }

    private static (SimulationEngine Engine, LeafEffect Leaves) Create(float intensity, MonitorInfo? monitor = null)
    {
        var engine = new SimulationEngine(seed: 5);
        var leaves = new LeafEffect(seed: 5) { Settings = new EffectSettings { Intensity = intensity } };
        engine.Add(leaves);
        engine.SetMonitors([monitor ?? s_monitor]);
        engine.SetWindows(WindowSnapshot.Of(0, s_window));
        return (engine, leaves);
    }

    private static void Run(SimulationEngine engine, double seconds)
    {
        int frames = (int)Math.Round(seconds / FrameTime);
        for (int i = 0; i < frames; i++)
        {
            engine.Advance(FrameTime);
        }
    }
}
