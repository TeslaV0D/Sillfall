using System.Numerics;
using Weatherly.Core.Desktop;
using Weatherly.Core.Displays;
using Weatherly.Core.Effects;
using Weatherly.Core.Effects.Rain;
using Weatherly.Core.Geometry;
using Weatherly.Core.Rendering;

namespace Weatherly.Core.Tests.Effects.Rain;

public class RainEffectTests
{
    private const double FrameTime = 1.0 / 60;

    private static readonly MonitorInfo s_monitor = new(
        @"\\.\DISPLAY1", PixelRect.FromSize(0, 0, 1920, 1080), PixelRect.FromSize(0, 0, 1920, 1032), MonitorInfo.DefaultDpi, IsPrimary: true);

    private static readonly TrackedWindow s_window = new(7, PixelRect.FromSize(400, 300, 1000, 500));

    [Fact]
    public void Rain_FillsTheScreenWithDropsUpToTheBudget()
    {
        (SimulationEngine engine, RainEffect rain) = Create(intensity: 1f);

        Run(engine, seconds: 2);

        rain.DropCount.Should().BeInRange(2000, RainEffect.MaxDrops);
        engine.Frame.SpriteCount.Should().BeGreaterThan(rain.DropCount);
    }

    [Fact]
    public void Rain_LighterIntensity_HasFewerDrops()
    {
        (SimulationEngine light, RainEffect lightRain) = Create(intensity: 0.2f);
        (SimulationEngine heavy, RainEffect heavyRain) = Create(intensity: 0.9f);

        Run(light, seconds: 2);
        Run(heavy, seconds: 2);

        lightRain.DropCount.Should().BeLessThan(heavyRain.DropCount / 3);
    }

    [Fact]
    public void NearDrops_HitTheWindowAndLeaveWaterOnIt()
    {
        (SimulationEngine engine, RainEffect rain) = Create(intensity: 0.8f);

        Run(engine, seconds: 2);

        WaterField? puddle = rain.PuddleOf(s_window.Id);
        puddle.Should().NotBeNull();
        puddle!.Total.Should().BePositive();
        puddle.Width.Should().Be(1000);
        rain.HitsPerSecond.Should().BePositive();
    }

    [Fact]
    public void Depth_Zero_LetsEveryDropPassBehindTheWindows()
    {
        (SimulationEngine engine, RainEffect rain) = Create(intensity: 0.8f, depth: 0f);

        Run(engine, seconds: 2);

        rain.PuddleOf(s_window.Id).Should().BeNull();
        rain.SplashCount.Should().Be(0);
    }

    [Fact]
    public void Water_RunsOffTheCornersAsDripsDownTheSides()
    {
        (SimulationEngine engine, RainEffect rain) = Create(intensity: 0.7f);

        Run(engine, seconds: 6);

        rain.DripCount.Should().BePositive();
        Sprite[] heads = [.. engine.Frame.Sprites.ToArray().Where(s => s.Shape == SpriteShape.Disc && s.HalfSize == new Vector2(1.8f, 2.2f))];
        heads.Should().NotBeEmpty();
        heads.Should().OnlyContain(s => Math.Abs(s.Center.X - 399.5f) < 2.5f || Math.Abs(s.Center.X - 1400.5f) < 2.5f, "drips run down the columns just outside the window");
        heads.Should().OnlyContain(s => s.Center.Y >= 300 && s.Center.Y <= 800);
    }

    [Fact]
    public void HeavyRain_PoursFromTheCorners()
    {
        (SimulationEngine engine, RainEffect rain) = Create(intensity: 1f);
        Run(engine, seconds: 5);

        // Sheet drops start slowly right below the corners.
        Sprite[] nearCorners = [.. engine.Frame.Sprites.ToArray().Where(s =>
            s.Shape == SpriteShape.Streak && s.Center.Y > 300 && s.Center.Y < 360
            && (Math.Abs(s.Center.X - 399f) < 4f || Math.Abs(s.Center.X - 1400f) < 4f))];
        nearCorners.Length.Should().BeGreaterThan(3);
        rain.DropCount.Should().BeLessThanOrEqualTo(RainEffect.MaxDrops);
    }

    [Fact]
    public void Splashes_StayWithinTheirShortLifetime()
    {
        (SimulationEngine engine, RainEffect rain) = Create(intensity: 1f);
        Run(engine, seconds: 2);
        int splashes = rain.SplashCount;
        splashes.Should().BePositive();

        // No new rain and no drop near enough to hit anything: the splashes in the air settle within their lifetime.
        rain.Settings = new EffectSettings { Intensity = 0f };
        rain.Depth = 0f;
        Run(engine, seconds: 1);

        rain.SplashCount.Should().Be(0);
    }

    [Fact]
    public void MovingTheWindow_TakesTheDripsAlong()
    {
        (SimulationEngine engine, RainEffect rain) = Create(intensity: 0.7f);
        Run(engine, seconds: 6);
        rain.Settings = new EffectSettings { Intensity = 0f };

        var moved = s_window with { Bounds = PixelRect.FromSize(600, 300, 1000, 500) };
        engine.SetWindows(WindowSnapshot.Of(6.1, moved));
        engine.Advance(1.0 / 120);

        Sprite[] heads = [.. engine.Frame.Sprites.ToArray().Where(s => s.Shape == SpriteShape.Disc && s.HalfSize == new Vector2(1.8f, 2.2f))];
        heads.Should().OnlyContain(s => Math.Abs(s.Center.X - 599.5f) < 2.5f || Math.Abs(s.Center.X - 1600.5f) < 2.5f);
    }

    [Fact]
    public void ResizingTheWindow_ResamplesItsWater()
    {
        (SimulationEngine engine, RainEffect rain) = Create(intensity: 0.8f);
        Run(engine, seconds: 2);

        var wider = s_window with { Bounds = PixelRect.FromSize(400, 300, 1200, 500) };
        engine.SetWindows(WindowSnapshot.Of(2.1, wider));

        rain.PuddleOf(s_window.Id)!.Width.Should().Be(1200);
    }

    [Fact]
    public void ClosingTheWindow_DropsItsWater()
    {
        (SimulationEngine engine, RainEffect rain) = Create(intensity: 0.8f);
        Run(engine, seconds: 2);
        rain.PuddleOf(s_window.Id).Should().NotBeNull();

        engine.SetWindows(WindowSnapshot.Of(2.1));

        rain.PuddleOf(s_window.Id).Should().BeNull();
        rain.DripCount.Should().Be(0);
    }

    [Fact]
    public void Shaking_EmptiesThePuddle()
    {
        (SimulationEngine engine, RainEffect rain) = Create(intensity: 0.8f);
        Run(engine, seconds: 2);
        rain.Settings = new EffectSettings { Intensity = 0f };

        int[] offsets = [60, -60, 60, -60];
        for (int i = 0; i < offsets.Length; i++)
        {
            engine.SetWindows(WindowSnapshot.Of(2.0 + (0.05 * (i + 1)), s_window with { Bounds = PixelRect.FromSize(400 + offsets[i], 300, 1000, 500) }));
        }

        rain.PuddleOf(s_window.Id)!.Total.Should().Be(0f);
    }

    [Fact]
    public void Wind_SlantsTheDrops()
    {
        (SimulationEngine engine, _) = Create(intensity: 0.5f);
        engine.Context.Wind.Base = new Vector2(400, 0);
        engine.Context.Wind.Gustiness = 0f;

        Run(engine, seconds: 1);

        float[] angles = [.. engine.Frame.Sprites.ToArray().Where(s => s.Shape == SpriteShape.Streak).Select(s => s.Rotation)];
        angles.Average().Should().BeLessThan(MathF.PI / 2 - 0.1f, "with wind to the right the drops lean right");
    }

    [Fact]
    public void CustomColorAndBrightness_AreApplied()
    {
        (SimulationEngine engine, RainEffect rain) = Create(intensity: 0.5f);
        rain.Settings = new EffectSettings { Intensity = 0.5f, Color = new Vector3(1f, 0f, 0f), Brightness = 2f };

        Run(engine, seconds: 1);

        Sprite drop = engine.Frame.Sprites.ToArray().Last(s => s.Shape == SpriteShape.Streak);
        drop.Color.Y.Should().Be(0f);
        drop.Color.X.Should().Be(drop.Color.W, "pure red, premultiplied");
    }

    [Fact]
    public void Rain_StopsCompletelyAfterIntensityZero()
    {
        (SimulationEngine engine, RainEffect rain) = Create(intensity: 1f);
        Run(engine, seconds: 3);

        rain.Settings = new EffectSettings { Intensity = 0f };
        Run(engine, seconds: 40);

        rain.IsActive.Should().BeFalse();
        engine.IsActive.Should().BeFalse();
    }

    [Fact]
    public void Simulation_IsDeterministic()
    {
        (SimulationEngine first, _) = Create(intensity: 0.7f);
        (SimulationEngine second, _) = Create(intensity: 0.7f);

        Run(first, seconds: 3);
        Run(second, seconds: 3);

        first.Frame.Sprites.ToArray().Should().Equal(second.Frame.Sprites.ToArray());
    }

    [Fact]
    public void FrameLoop_DoesNotAllocateOnceWarm()
    {
        (SimulationEngine engine, _) = Create(intensity: 1f);
        WindowSnapshot windows = WindowSnapshot.Of(0, s_window);
        void Frame()
        {
            engine.SetWindows(windows);
            engine.Advance(FrameTime);
        }

        for (int i = 0; i < 400; i++)
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
    public void SimulationStep_StaysWithinTheBudget()
    {
        // PLAN.md, section 9: simulation step below 1 ms (p95) at 50 % intensity. Measured generously here,
        // CI machines are noisy; the real budget is checked on reference hardware.
        (SimulationEngine engine, _) = Create(intensity: 0.5f);
        Run(engine, seconds: 3);

        var stopwatch = System.Diagnostics.Stopwatch.StartNew();
        const int Frames = 120;
        for (int i = 0; i < Frames; i++)
        {
            engine.Advance(1.0 / 120);
        }

        (stopwatch.Elapsed.TotalMilliseconds / Frames).Should().BeLessThan(4.0);
    }

    private static (SimulationEngine Engine, RainEffect Rain) Create(float intensity, float depth = 0.5f)
    {
        var engine = new SimulationEngine(seed: 5);
        var rain = new RainEffect(seed: 5) { Settings = new EffectSettings { Intensity = intensity }, Depth = depth };
        engine.Add(rain);
        engine.SetMonitors([s_monitor]);
        engine.SetWindows(WindowSnapshot.Of(0, s_window));
        return (engine, rain);
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
