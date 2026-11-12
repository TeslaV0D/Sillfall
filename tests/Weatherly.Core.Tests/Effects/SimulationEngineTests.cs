using System.Numerics;
using Weatherly.Core.Desktop;
using Weatherly.Core.Displays;
using Weatherly.Core.Effects;
using Weatherly.Core.Geometry;
using Weatherly.Core.Rendering;

namespace Weatherly.Core.Tests.Effects;

public class SimulationEngineTests
{
    private const double FrameTime = 1.0 / 60;

    private static readonly MonitorInfo s_monitor = new(
        @"\\.\DISPLAY1", PixelRect.FromSize(0, 0, 1920, 1080), PixelRect.FromSize(0, 0, 1920, 1032), MonitorInfo.DefaultDpi, IsPrimary: true);

    private static readonly TrackedWindow s_window = new(7, PixelRect.FromSize(400, 300, 1000, 500));

    [Fact]
    public void Drops_LandOnTheTopEdgeOfAWindow()
    {
        (SimulationEngine engine, TestDropsEffect drops) = Create();

        Run(engine, seconds: 3);

        drops.RestingCount.Should().BePositive();
        RestingOnWindow(engine).Should().NotBeEmpty();
        RestingOnWindow(engine).Should().OnlyContain(sprite => sprite.Center.Y == 297f, "a resting drop sits right on the edge row 300");
    }

    [Fact]
    public void Drops_NeverRestInsideAWindow()
    {
        (SimulationEngine engine, _) = Create();

        Run(engine, seconds: 5);

        // The lowest pixel row a drop covers is center + radius - 1 (the bottom edge itself is exclusive).
        engine.Frame.Sprites.ToArray()
            .Where(sprite => s_window.Bounds.Contains((int)sprite.Center.X, (int)MathF.Floor(sprite.Center.Y + 3f - 0.5f)))
            .Should().BeEmpty();
    }

    [Fact]
    public void RestingDrops_MoveWithTheirWindow()
    {
        (SimulationEngine engine, TestDropsEffect drops) = Create();
        Run(engine, seconds: 2);
        float[] before = [.. RestingOnWindow(engine).Select(sprite => sprite.Center.X)];
        drops.Intensity = 0f;

        var moved = s_window with { Bounds = PixelRect.FromSize(450, 320, 1000, 500) };
        engine.SetWindows(WindowSnapshot.Of(2.1, moved));
        engine.Advance(0);

        Sprite[] after = [.. engine.Frame.Sprites.ToArray().Where(sprite => sprite.Center.Y == 317f)];
        after.Length.Should().BeGreaterThanOrEqualTo(before.Length);
        after.Select(sprite => sprite.Center.X).Should().Contain(before.Select(x => x + 50f));
    }

    [Fact]
    public void Shaking_ReleasesTheRestingDrops()
    {
        (SimulationEngine engine, TestDropsEffect drops) = Create();
        Run(engine, seconds: 2);
        drops.Intensity = 0f;
        int restingBefore = drops.RestingCount;
        restingBefore.Should().BePositive();

        // Fast back and forth: three reversals well within 600 ms.
        int[] offsets = [60, -60, 60, -60];
        for (int i = 0; i < offsets.Length; i++)
        {
            var shaken = s_window with { Bounds = PixelRect.FromSize(400 + offsets[i], 300, 1000, 500) };
            engine.SetWindows(WindowSnapshot.Of(2.0 + (0.05 * (i + 1)), shaken));
        }

        engine.Advance(FrameTime);
        drops.RestingCount.Should().BeLessThan(restingBefore);
    }

    [Fact]
    public void ClosingTheWindow_DropsFallToTheFloor()
    {
        (SimulationEngine engine, TestDropsEffect drops) = Create();
        Run(engine, seconds: 2);
        drops.Intensity = 0f;

        engine.SetWindows(WindowSnapshot.Of(2.1));
        Run(engine, seconds: 1.2);

        engine.Frame.Sprites.ToArray().Should().Contain(sprite => sprite.Center.Y == 1029f, "the drops end up on the floor at row 1032");
    }

    [Fact]
    public void Engine_WithIntensityZero_BecomesIdleOnceAllDropsAreGone()
    {
        (SimulationEngine engine, TestDropsEffect drops) = Create();
        Run(engine, seconds: 1);
        engine.IsActive.Should().BeTrue();

        drops.Intensity = 0f;
        Run(engine, seconds: 6);

        drops.Count.Should().Be(0);
        engine.IsActive.Should().BeFalse();
    }

    [Fact]
    public void Engine_WithoutEffects_IsIdle()
    {
        new SimulationEngine().IsActive.Should().BeFalse();
    }

    [Fact]
    public void Simulation_IsDeterministicForTheSameSeed()
    {
        (SimulationEngine first, _) = Create(seed: 5);
        (SimulationEngine second, _) = Create(seed: 5);

        Run(first, seconds: 2);
        Run(second, seconds: 2);

        first.Frame.Sprites.ToArray().Should().Equal(second.Frame.Sprites.ToArray());
    }

    [Fact]
    public void Simulation_DoesNotDependOnTheFrameRate()
    {
        (SimulationEngine at60, _) = Create(seed: 5);
        (SimulationEngine at30, _) = Create(seed: 5);

        Run(at60, seconds: 2, frameTime: 1.0 / 60);
        Run(at30, seconds: 2, frameTime: 1.0 / 30);

        at30.Frame.Sprites.ToArray().Should().Equal(at60.Frame.Sprites.ToArray());
    }

    [Fact]
    public void Wind_DriftsTheDrops()
    {
        (SimulationEngine calm, _) = Create(seed: 3);
        (SimulationEngine windy, _) = Create(seed: 3);
        windy.Context.Wind.Base = new Vector2(300, 0);
        calm.SetWindows(WindowSnapshot.Of(0));
        windy.SetWindows(WindowSnapshot.Of(0));

        Run(calm, seconds: 0.8);
        Run(windy, seconds: 0.8);

        windy.Frame.Sprites.ToArray().Average(sprite => sprite.Center.X)
            .Should().BeGreaterThan(calm.Frame.Sprites.ToArray().Average(sprite => sprite.Center.X) + 50);
    }

    [Fact]
    public void FrameLoop_DoesNotAllocateOnceWarm()
    {
        (SimulationEngine engine, TestDropsEffect drops) = Create();
        drops.Intensity = 1f;
        WindowSnapshot windows = WindowSnapshot.Of(0, s_window);

        // The warm-up runs exactly the measured loop body: first uses (JIT, lazily created
        // equality comparers, buffer growth) must happen before the measurement, whatever ran before.
        void Frame(int frame)
        {
            engine.SetWindows(windows);
            engine.SetCursor(new Vector2(frame, frame), Vector2.Zero);
            engine.Advance(FrameTime);
        }

        for (int frame = 0; frame < 300; frame++)
        {
            Frame(frame);
        }

        long before = GC.GetAllocatedBytesForCurrentThread();
        for (int frame = 0; frame < 600; frame++)
        {
            Frame(frame);
        }

        (GC.GetAllocatedBytesForCurrentThread() - before).Should().Be(0);
        engine.Frame.SpriteCount.Should().BePositive();
    }

    private static (SimulationEngine Engine, TestDropsEffect Drops) Create(ulong seed = 1)
    {
        var engine = new SimulationEngine((uint)seed);
        var drops = new TestDropsEffect(seed: seed) { Intensity = 1f };
        engine.Add(drops);
        engine.SetMonitors([s_monitor]);
        engine.SetWindows(WindowSnapshot.Of(0, s_window));
        return (engine, drops);
    }

    private static void Run(SimulationEngine engine, double seconds, double frameTime = FrameTime)
    {
        int frames = (int)Math.Round(seconds / frameTime);
        for (int i = 0; i < frames; i++)
        {
            engine.Advance(frameTime);
        }
    }

    private static List<Sprite> RestingOnWindow(SimulationEngine engine) =>
        [.. engine.Frame.Sprites.ToArray().Where(sprite =>
            sprite.Center.X >= s_window.Bounds.Left && sprite.Center.X < s_window.Bounds.Right && Math.Abs(sprite.Center.Y - 297f) < 0.01f)];
}
