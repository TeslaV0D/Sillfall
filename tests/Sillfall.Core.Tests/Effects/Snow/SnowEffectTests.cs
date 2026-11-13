using System.Numerics;
using Sillfall.Core.Desktop;
using Sillfall.Core.Displays;
using Sillfall.Core.Effects;
using Sillfall.Core.Effects.Snow;
using Sillfall.Core.Geometry;
using Sillfall.Core.Rendering;

namespace Sillfall.Core.Tests.Effects.Snow;

public class SnowEffectTests
{
    private const double FrameTime = 1.0 / 60;

    private static readonly MonitorInfo s_monitor = new(
        @"\\.\DISPLAY1", PixelRect.FromSize(0, 0, 1920, 1080), PixelRect.FromSize(0, 0, 1920, 1032), MonitorInfo.DefaultDpi, IsPrimary: true);

    private static readonly TrackedWindow s_window = new(7, PixelRect.FromSize(400, 300, 1000, 500));

    [Fact]
    public void Snow_FillsTheScreenWithFlakesWithinTheBudget()
    {
        (SimulationEngine engine, SnowEffect snow) = Create(intensity: 1f);

        Run(engine, seconds: 8);

        snow.FlakeCount.Should().BeInRange(1000, SnowEffect.MaxFlakes);
        engine.Frame.Sprites.ToArray().Count(s => s.Shape == SpriteShape.Flake).Should().BeGreaterThan(500);
    }

    [Fact]
    public void Snow_LighterIntensity_HasFewerFlakes()
    {
        (SimulationEngine light, SnowEffect lightSnow) = Create(intensity: 0.2f);
        (SimulationEngine heavy, SnowEffect heavySnow) = Create(intensity: 0.9f);

        Run(light, seconds: 6);
        Run(heavy, seconds: 6);

        lightSnow.FlakeCount.Should().BeLessThan(heavySnow.FlakeCount / 3);
    }

    [Fact]
    public void Flakes_SettleOnTheWindowAndTheFloor()
    {
        (SimulationEngine engine, SnowEffect snow) = Create(intensity: 1f);

        Run(engine, seconds: 20);

        SnowField? pile = snow.PileOf(s_window.Id);
        pile.Should().NotBeNull();
        pile!.Volume.Should().BePositive();
        pile.Width.Should().Be(1000);
        pile.BinWidth.Should().Be(SnowEffect.BinWidth);
        snow.FloorOf(0)!.Volume.Should().BePositive();
    }

    [Fact]
    public void Piles_NeverExceedTheMaximumHeightOrTheAngleOfRepose()
    {
        (SimulationEngine engine, SnowEffect snow) = Create(intensity: 1f);
        snow.Fill(engine.Context, 27f);

        Run(engine, seconds: 30);

        SnowField pile = snow.PileOf(s_window.Id)!;
        pile.Peak.Should().BeLessThanOrEqualTo(SnowEffect.MaxPileHeight);
        ReadOnlySpan<float> h = pile.Heights;
        for (int i = 0; i < h.Length - 1; i++)
        {
            MathF.Abs(h[i] - h[i + 1]).Should().BeLessThanOrEqualTo((pile.MaxSlope * 1.5f) + 0.5f);
        }

        snow.ClumpCount.Should().BePositive("a full pile sheds clumps over its ends");
    }

    [Fact]
    public void ClosingTheWindow_DropsItsSnowOntoTheFloor_WithoutLosingAny()
    {
        (SimulationEngine engine, SnowEffect snow) = Create(intensity: 0f);
        snow.Fill(engine.Context, 20f);
        snow.FloorOf(0)!.TakeAll();
        float onWindow = snow.PileOf(s_window.Id)!.Volume;

        engine.SetWindows(WindowSnapshot.Of(0.1));
        snow.PileOf(s_window.Id).Should().BeNull();
        snow.ClumpCount.Should().BePositive();
        float inTheAir = snow.FallingVolume;
        inTheAir.Should().BeApproximately(onWindow, onWindow * 1e-3f);

        // The clumps land on the floor before anything melts noticeably.
        RunWithoutMelting(engine, snow, seconds: 2);

        snow.ClumpCount.Should().Be(0);
        SnowField floor = snow.FloorOf(0)!;
        (floor.Volume + floor.LeftShed + floor.RightShed).Should().BeApproximately(onWindow, onWindow * 0.02f, "snow is conserved: what slid off lies on the floor");
    }

    [Fact]
    public void Shaking_ThrowsTheSnowOff()
    {
        (SimulationEngine engine, SnowEffect snow) = Create(intensity: 0f);
        snow.Fill(engine.Context, 20f);
        float before = snow.PileOf(s_window.Id)!.Volume;

        // Small moves (below the jerk threshold), quick reversals: a shake.
        int[] offsets = [30, -30, 30, -30];
        for (int i = 0; i < offsets.Length; i++)
        {
            engine.SetWindows(WindowSnapshot.Of(0.05 * (i + 1), s_window with { Bounds = PixelRect.FromSize(400 + offsets[i], 300, 1000, 500) }));
            engine.Advance(0.05);
        }

        snow.PileOf(s_window.Id)!.Volume.Should().BeLessThan(before * 0.05f);
        snow.ClumpCount.Should().BePositive();

        RunWithoutMelting(engine, snow, seconds: 0.5);
        snow.PileOf(s_window.Id)!.Volume.Should().BeLessThan(before * 0.05f, "the clumps do not land back on the window they came from");
    }

    [Fact]
    public void SlowlyMovingTheWindow_TakesTheSnowAlong()
    {
        (SimulationEngine engine, SnowEffect snow) = Create(intensity: 0f);
        snow.Fill(engine.Context, 20f);
        float before = snow.PileOf(s_window.Id)!.Volume;

        // 300 px/s to the right for a second.
        for (int i = 1; i <= 60; i++)
        {
            engine.SetWindows(WindowSnapshot.Of(i * FrameTime, s_window with { Bounds = PixelRect.FromSize(400 + (5 * i), 300, 1000, 500) }));
            engine.Advance(FrameTime);
        }

        engine.Advance(0.5);

        snow.ClumpCount.Should().Be(0);
        snow.PileOf(s_window.Id)!.Volume.Should().BeGreaterThan(before * 0.9f, "only melting takes a little");
        engine.Frame.Sprites.ToArray().Where(s => s.Shape == SpriteShape.Rectangle && s.Center.Y < 400).Min(s => s.Center.X).Should().BeGreaterThanOrEqualTo(700, "the pile is drawn on the moved window");
    }

    [Fact]
    public void JerkingTheWindow_ThrowsSnowOff()
    {
        (SimulationEngine engine, SnowEffect snow) = Create(intensity: 0f);
        snow.Fill(engine.Context, 20f);
        float before = snow.PileOf(s_window.Id)!.Volume;

        // From rest to 4800 px/s in one frame, then a sudden stop.
        engine.SetWindows(WindowSnapshot.Of(FrameTime, s_window with { Bounds = PixelRect.FromSize(480, 300, 1000, 500) }));
        engine.Advance(FrameTime);
        engine.Advance(0.3);

        snow.PileOf(s_window.Id)!.Volume.Should().BeLessThan(before * 0.6f);
        snow.ClumpCount.Should().BePositive();
    }

    [Fact]
    public void ResizingTheWindow_ResamplesItsSnow()
    {
        (SimulationEngine engine, SnowEffect snow) = Create(intensity: 0f);
        snow.Fill(engine.Context, 10f);
        float before = snow.PileOf(s_window.Id)!.Volume;

        engine.SetWindows(WindowSnapshot.Of(0.1, s_window with { Bounds = PixelRect.FromSize(400, 300, 1300, 500) }));

        SnowField pile = snow.PileOf(s_window.Id)!;
        pile.Width.Should().Be(1300);
        pile.Volume.Should().BeApproximately(before, before * 1e-3f);
    }

    [Fact]
    public void FarFlakes_AreHiddenBehindWindows()
    {
        (SimulationEngine engine, _) = Create(intensity: 1f);
        Run(engine, seconds: 10);

        Sprite[] flakes = [.. engine.Frame.Sprites.ToArray().Where(s => s.Shape == SpriteShape.Flake && s.HalfSize.X > 1.5f)];
        Sprite[] small = [.. engine.Frame.Sprites.ToArray().Where(s => s.Shape == SpriteShape.Flake && s.HalfSize.X < 0.85f)];
        flakes.Should().Contain(s => s_window.Bounds.Contains((int)s.Center.X, (int)s.Center.Y), "near flakes fall in front of windows");
        small.Should().NotContain(s => s_window.Bounds.Contains((int)s.Center.X, (int)s.Center.Y), "far flakes fall behind them");
    }

    [Fact]
    public void BigFlakes_ShowTheirShape_SmallOnesAreBlobs()
    {
        (SimulationEngine engine, _) = Create(intensity: 1f);
        Run(engine, seconds: 10);

        // Above the window: no glitter from the piles.
        Sprite[] flakes = [.. engine.Frame.Sprites.ToArray().Where(s => s.Shape == SpriteShape.Flake && s.Center.Y is > 0 and < 250)];
        flakes.Where(s => s.HalfSize.X < 2f).Should().OnlyContain(s => s.Variant == 0);
        flakes.Where(s => s.HalfSize.X > 2.5f).Select(s => s.Variant).Distinct().Count().Should().BeGreaterThan(3);
    }

    [Fact]
    public void Snow_MeltsAndStopsCompletelyAfterIntensityZero()
    {
        (SimulationEngine engine, SnowEffect snow) = Create(intensity: 1f);
        snow.Fill(engine.Context, 28f);
        Run(engine, seconds: 3);

        snow.Settings = new EffectSettings { Intensity = 0f };
        Run(engine, seconds: 60);

        snow.IsActive.Should().BeFalse();
        engine.IsActive.Should().BeFalse();
    }

    [Fact]
    public void Frost_KeepsTheSnowAfterIntensityZero_UntilItRains()
    {
        (SimulationEngine engine, SnowEffect snow) = Create(intensity: 0f);
        snow.Fill(engine.Context, 20f);
        float volume = snow.PileOf(s_window.Id)!.Volume;
        snow.Frozen = true;

        Run(engine, seconds: 20);
        snow.PileOf(s_window.Id)!.Volume.Should().BeApproximately(volume, volume * 0.05f, "frost keeps the snow");

        snow.RainIntensity = 1f;
        Run(engine, seconds: 4);
        snow.PileOf(s_window.Id)!.Volume.Should().BeLessThan(volume * 0.5f, "rain melts it all the same");
    }

    [Fact]
    public void Rain_MeltsTheSnowFaster()
    {
        (SimulationEngine dry, SnowEffect drySnow) = Create(intensity: 0f);
        (SimulationEngine wet, SnowEffect wetSnow) = Create(intensity: 0f);
        drySnow.Fill(dry.Context, 20f);
        wetSnow.Fill(wet.Context, 20f);
        wetSnow.RainIntensity = 1f;

        Run(dry, seconds: 4);
        Run(wet, seconds: 4);

        wetSnow.PileOf(s_window.Id)!.Volume.Should().BeLessThan(drySnow.PileOf(s_window.Id)!.Volume * 0.5f);
    }

    [Fact]
    public void Wind_BlowsTheFlakesSideways()
    {
        (SimulationEngine engine, SnowEffect snow) = Create(intensity: 1f);
        engine.Context.Wind.Base = new Vector2(200, 0);
        engine.Context.Wind.Gustiness = 0f;

        Run(engine, seconds: 6);
        Sprite[] before = [.. engine.Frame.Sprites.ToArray()];
        Run(engine, seconds: FrameTime);
        Sprite[] after = [.. engine.Frame.Sprites.ToArray()];

        // Each flake one frame later: the nearest one slightly below it (sprites are not in a stable order).
        Vector2[] earlier = [.. before.Where(s => s.Shape == SpriteShape.Flake).Select(s => s.Center)];
        Vector2[] later = [.. after.Where(s => s.Shape == SpriteShape.Flake).Select(s => s.Center)];
        var steps = new List<float>();
        foreach (Vector2 point in earlier.Take(800))
        {
            Vector2 best = default;
            float bestDistance = float.MaxValue;
            foreach (Vector2 candidate in later)
            {
                Vector2 d = candidate - point;
                if (d.Y is > 0 and < 3 && MathF.Abs(d.X) < 6 && d.LengthSquared() < bestDistance)
                {
                    bestDistance = d.LengthSquared();
                    best = d;
                }
            }

            if (bestDistance < float.MaxValue)
            {
                steps.Add(best.X);
            }
        }

        steps.Count.Should().BeGreaterThan(500);
        steps.Average().Should().BeGreaterThan(1.2f, "200 px/s of wind moves the flakes about 2 px per frame to the right");
        snow.FlakeCount.Should().BePositive();
    }

    [Fact]
    public void CustomColorAndBrightness_AreApplied()
    {
        (SimulationEngine engine, SnowEffect snow) = Create(intensity: 0.5f);
        snow.Settings = new EffectSettings { Intensity = 0.5f, Color = new Vector3(0f, 1f, 0f), Brightness = 2f };

        Run(engine, seconds: 3);

        Sprite flake = engine.Frame.Sprites.ToArray().Last(s => s.Shape == SpriteShape.Flake);
        flake.Color.X.Should().Be(0f);
        flake.Color.Y.Should().Be(flake.Color.W, "pure green, premultiplied");
    }

    [Fact]
    public void Simulation_IsDeterministic()
    {
        (SimulationEngine first, _) = Create(intensity: 0.7f);
        (SimulationEngine second, _) = Create(intensity: 0.7f);

        Run(first, seconds: 5);
        Run(second, seconds: 5);

        first.Frame.Sprites.ToArray().Should().Equal(second.Frame.Sprites.ToArray());
    }

    [Fact]
    public void FrameLoop_DoesNotAllocateOnceWarm()
    {
        (SimulationEngine engine, SnowEffect snow) = Create(intensity: 1f);
        snow.Fill(engine.Context, 28f);
        WindowSnapshot windows = WindowSnapshot.Of(0, s_window);
        void Frame()
        {
            engine.SetWindows(windows);
            engine.Advance(FrameTime);
        }

        for (int i = 0; i < 600; i++)
        {
            Frame();
        }

        Allocations.Measure(() =>
        {
            for (int i = 0; i < 300; i++)
            {
                Frame();
            }
        }).Should().Be(0);
    }

    [Fact]
    public void SimulationStep_StaysWithinTheBudget()
    {
        (SimulationEngine engine, SnowEffect snow) = Create(intensity: 0.5f);
        snow.Fill(engine.Context, 20f);
        Run(engine, seconds: 10);

        var stopwatch = System.Diagnostics.Stopwatch.StartNew();
        const int Frames = 120;
        for (int i = 0; i < Frames; i++)
        {
            engine.Advance(1.0 / 120);
        }

        (stopwatch.Elapsed.TotalMilliseconds / Frames).Should().BeLessThan(4.0);
    }

    [Fact]
    public void HighDpi_ScalesFlakesBinsAndPiles()
    {
        (SimulationEngine normal, _) = Create(intensity: 0.5f);
        (SimulationEngine doubled, SnowEffect snow) = Create(intensity: 0.5f, monitor: s_monitor with { Dpi = 192 });

        Run(normal, seconds: 6);
        Run(doubled, seconds: 6);

        static float Largest(SimulationEngine engine) =>
            engine.Frame.Sprites.ToArray().Where(s => s.Shape == SpriteShape.Flake).Max(s => s.HalfSize.X);

        Largest(doubled).Should().BeApproximately(2 * Largest(normal), 0.3f);
        SnowField pile = snow.PileOf(s_window.Id)!;
        pile.BinWidth.Should().Be(2 * SnowEffect.BinWidth);
        pile.MaxHeight.Should().Be(2 * SnowEffect.MaxPileHeight);
    }

    private static (SimulationEngine Engine, SnowEffect Snow) Create(float intensity, MonitorInfo? monitor = null)
    {
        var engine = new SimulationEngine(seed: 5);
        var snow = new SnowEffect(seed: 5) { Settings = new EffectSettings { Intensity = intensity } };
        engine.Add(snow);
        engine.SetMonitors([monitor ?? s_monitor]);
        engine.SetWindows(WindowSnapshot.Of(0, s_window));
        return (engine, snow);
    }

    private static void Run(SimulationEngine engine, double seconds)
    {
        int frames = (int)Math.Round(seconds / FrameTime);
        for (int i = 0; i < frames; i++)
        {
            engine.Advance(FrameTime);
        }
    }

    /// <summary>Runs while it keeps snowing invisibly (intensity just above zero), so nothing melts.</summary>
    private static void RunWithoutMelting(SimulationEngine engine, SnowEffect snow, double seconds)
    {
        EffectSettings settings = snow.Settings;
        snow.Settings = new EffectSettings { Intensity = 1e-6f };
        Run(engine, seconds);
        snow.Settings = settings;
    }
}
