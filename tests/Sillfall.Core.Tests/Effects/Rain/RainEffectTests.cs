using System.Numerics;
using Sillfall.Core.Desktop;
using Sillfall.Core.Displays;
using Sillfall.Core.Effects;
using Sillfall.Core.Effects.Rain;
using Sillfall.Core.Geometry;
using Sillfall.Core.Rendering;

namespace Sillfall.Core.Tests.Effects.Rain;

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

        rain.PuddleOf(s_window.Id)!.IsDry.Should().BeTrue("no drop is near enough to hit it");
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
        heads.Should().OnlyContain(s => (s.Center.X < 400 - s.HalfSize.X + 0.5f && s.Center.X > 394) || (s.Center.X > 1400 + s.HalfSize.X - 0.5f && s.Center.X < 1406), "drips run just outside the frame, never over the window");
        heads.Should().OnlyContain(s => s.Center.Y >= 300 && s.Center.Y <= 800);
    }

    [Fact]
    public void FarDrops_AreHiddenBehindWindows()
    {
        (SimulationEngine engine, _) = Create(intensity: 1f, depth: 0.3f);

        Run(engine, seconds: 2);

        // Far drops (thin streaks) are drawn only while their tip is outside every window.
        static Vector2 Tip(Sprite s) => s.Center + (new Vector2(MathF.Cos(s.Rotation), MathF.Sin(s.Rotation)) * s.HalfSize.X);
        engine.Frame.Sprites.ToArray()
            .Where(s => s.Shape == SpriteShape.Streak && s.HalfSize.Y < 1.1f)
            .Select(Tip)
            .Where(tip => s_window.Bounds.Contains((int)MathF.Floor(tip.X), (int)MathF.Floor(tip.Y)))
            .Should().BeEmpty();
    }

    [Fact]
    public void MaximizedWindow_GetsNoRainOverItsContent()
    {
        // F-05: a maximized window has no top edge to hit. Near drops must not fall across its content.
        var maximized = new TrackedWindow(9, s_monitor.WorkArea, WindowTraits.Maximized);
        (SimulationEngine engine, RainEffect rain) = Create(intensity: 1f, depth: 1f, windows: [maximized]);

        for (int frame = 0; frame < 240; frame++)
        {
            engine.Advance(FrameTime);
            RainSpritesInside(engine, maximized.Bounds).Should().BeEmpty($"frame {frame}: nothing is drawn over the maximized window");
        }

        rain.DropCount.Should().BePositive("it still rains, just not over the window");
        rain.PuddleOf(maximized.Id).Should().BeNull("a maximized window has no top edge to hold water");
    }

    [Fact]
    public void WindowAtTheTopOfTheScreen_GetsNoRainOverItsContent()
    {
        var atTop = new TrackedWindow(9, PixelRect.FromSize(300, 0, 900, 600));
        (SimulationEngine engine, _) = Create(intensity: 1f, depth: 1f, windows: [atTop]);

        for (int frame = 0; frame < 180; frame++)
        {
            engine.Advance(FrameTime);
            RainSpritesInside(engine, atTop.Bounds).Should().BeEmpty($"frame {frame}");
        }

        // Beside the window it rains as before.
        engine.Frame.Sprites.ToArray().Count(s => s.Shape == SpriteShape.Streak && Tip(s).X > 1300).Should().BePositive();
    }

    [Fact]
    public void WindowWithCoveredTopEdge_GetsNoRainOverItsContent()
    {
        // F-05: Explorer at y = 0 covers the settings window's top edge; rain fell across the settings content.
        var front = new TrackedWindow(1, PixelRect.FromSize(200, 0, 900, 500));
        var behind = new TrackedWindow(2, PixelRect.FromSize(500, 200, 900, 700));
        (SimulationEngine engine, _) = Create(intensity: 1f, depth: 1f, windows: [front, behind]);

        for (int frame = 0; frame < 240; frame++)
        {
            engine.Advance(FrameTime);
            RainSpritesInside(engine, front.Bounds).Should().BeEmpty($"frame {frame}: over the window in front");
            RainSpritesInside(engine, behind.Bounds).Should().BeEmpty($"frame {frame}: over the window behind");
        }
    }

    [Fact]
    public void WindowInFrontOfAMaximizedOne_StillCatchesRain()
    {
        var maximized = new TrackedWindow(9, s_monitor.WorkArea, WindowTraits.Maximized);
        (SimulationEngine engine, RainEffect rain) = Create(intensity: 1f, depth: 1f, windows: [s_window, maximized]);

        for (int frame = 0; frame < 300; frame++)
        {
            engine.Advance(FrameTime);

            // Falling drops over the maximized window are hidden; the only rain there is attached to the small
            // window's top edge (splashes, ripples) or runs down its sides.
            RainSpritesInside(engine, maximized.Bounds)
                .Where(s => s.Shape == SpriteShape.Streak && !s_window.Bounds.Contains((int)Tip(s).X, (int)Tip(s).Y)
                    && !NearTheSides(Tip(s)))
                .Should().BeEmpty($"frame {frame}");
        }

        rain.PuddleOf(s_window.Id).Should().NotBeNull("drops still land on the window in front");
        rain.PuddleOf(s_window.Id)!.Total.Should().BePositive();

        static bool NearTheSides(Vector2 tip) => tip.Y <= 800 && (Math.Abs(tip.X - 399.5f) < 8 || Math.Abs(tip.X - 1400.5f) < 8);
    }

    [Fact]
    public void GutterStreams_EndAtTheFirstWindowBelow()
    {
        // F-05: streams from a window's bottom corners ran as long lines across the windows below. Here the window
        // below has no exposed top edge (it starts at the top of the screen), so nothing can stop the streams.
        var upper = new TrackedWindow(1, PixelRect.FromSize(400, 100, 600, 200));
        var lower = new TrackedWindow(2, PixelRect.FromSize(200, 0, 1200, 950));
        (SimulationEngine engine, RainEffect rain) = Create(intensity: 1f, depth: 1f, windows: [upper, lower]);

        Run(engine, seconds: 6);
        rain.DripCount.Should().BePositive("the upper window drains down its sides");
        int below = upper.Bounds.Bottom + 3;
        for (int frame = 0; frame < 240; frame++)
        {
            engine.Advance(FrameTime);
            RainSpritesInside(engine, PixelRect.FromSize(lower.Bounds.Left, below, lower.Bounds.Width, lower.Bounds.Bottom - below))
                .Should().BeEmpty($"frame {frame}: nothing falls across the lower window");
        }
    }

    [Fact]
    public void Splashes_NeverFallIntoTheWindow()
    {
        (SimulationEngine engine, _) = Create(intensity: 1f, depth: 1f);

        for (int i = 0; i < 180; i++)
        {
            engine.Advance(FrameTime);
            Sprite[] splashes = [.. engine.Frame.Sprites.ToArray().Where(s => s.Shape == SpriteShape.Disc && s.HalfSize == new Vector2(1.1f, 1.1f))];
            splashes.Where(s => s.Center.X > 405 && s.Center.X < 1395 && s.Center.Y > 300.5f && s.Center.Y < 800)
                .Should().BeEmpty("droplets that fall back onto the edge are absorbed");
        }
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
        heads.Should().OnlyContain(s => (s.Center.X < 600 && s.Center.X > 594) || (s.Center.X > 1600 && s.Center.X < 1606));
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

        Allocations.Measure(() =>
        {
            for (int i = 0; i < 300; i++)
            {
                Frame();
            }
        }).Should().Be(0);
    }

    [Fact]
    public void FrameLoop_WindowAppearingMidRun_GetsWetWithoutAllocating()
    {
        // F-02 for rain: the water field of a new edge was made on its first hit, inside the frame loop.
        (SimulationEngine engine, RainEffect rain) = Create(intensity: 1f);
        Run(engine, seconds: 3);

        var newcomer = new TrackedWindow(11, PixelRect.FromSize(1500, 500, 380, 300));
        engine.SetWindows(WindowSnapshot.Of(3.1, s_window, newcomer));
        rain.PuddleOf(newcomer.Id).Should().NotBeNull("the field exists before the first drop hits");
        rain.PuddleOf(newcomer.Id)!.IsDry.Should().BeTrue();

        Allocations.Measure(() =>
        {
            for (int i = 0; i < 300; i++)
            {
                engine.Advance(FrameTime);
            }
        }).Should().Be(0);
        rain.PuddleOf(newcomer.Id)!.IsDry.Should().BeFalse("drops hit the new window during the measured frames");
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

    [Fact]
    public void DropSizes_SmallDropsOutnumberLargeOnes()
    {
        (SimulationEngine engine, RainEffect rain) = Create(intensity: 1f);
        Run(engine, seconds: 2);

        float[] sizes = [.. rain.DropSizes];
        sizes.Length.Should().BeGreaterThan(500);
        float mean = sizes.Average();
        float median = sizes.OrderBy(s => s).ElementAt(sizes.Length / 2);
        median.Should().BeLessThan(mean, "Marshall–Palmer: many small drops, few large ones");
        (sizes.Count(s => s > 3f) / (float)sizes.Length).Should().BeLessThan(0.05f);
    }

    [Fact]
    public void TerminalVelocity_GrowsWithDiameter()
    {
        RainEffect.TerminalVelocity(1f).Should().BeLessThan(RainEffect.TerminalVelocity(2f));
        RainEffect.TerminalVelocity(2f).Should().BeLessThan(RainEffect.TerminalVelocity(3f));
    }

    [Fact]
    public void TerminalVelocity_LevelsOffForLargeDrops()
    {
        // Atlas et al. 1973 asymptote is 9.65 m/s; growth from 3→4 mm is already much smaller than 1→2 mm.
        RainEffect.TerminalVelocity(4f).Should().BeInRange(8.5f, 9.65f);
        (RainEffect.TerminalVelocity(4f) - RainEffect.TerminalVelocity(3f)).Should().BeLessThan(
            RainEffect.TerminalVelocity(2f) - RainEffect.TerminalVelocity(1f));
    }

    [Fact]
    public void WindGust_SmallDropsFollowFasterThanLargeOnes()
    {
        (SimulationEngine engine, RainEffect rain) = Create(intensity: 1f);
        engine.Context.Wind.Base = new Vector2(200f, 0f);
        engine.Context.Wind.Gustiness = 0f;
        Run(engine, seconds: 1);

        // Sudden gust: small drops should close more of the gap to the new target in one step.
        engine.Context.Wind.Base = new Vector2(500f, 0f);
        float[] sizes = [.. rain.DropSizes];
        float[] before = [.. rain.DropVx];
        engine.Advance(FrameTime);
        float[] after = [.. rain.DropVx];

        float smallGain = 0f;
        float largeGain = 0f;
        int small = 0;
        int large = 0;
        for (int i = 0; i < sizes.Length && i < after.Length; i++)
        {
            float gain = after[i] - before[i];
            if (sizes[i] < 1.2f)
            {
                smallGain += gain;
                small++;
            }
            else if (sizes[i] > 2.5f)
            {
                largeGain += gain;
                large++;
            }
        }

        small.Should().BePositive();
        large.Should().BePositive();
        (smallGain / small).Should().BeGreaterThan(largeGain / large);
    }

    [Fact]
    public void HighDpi_ScalesDropsAndPuddleBins()
    {
        (SimulationEngine normal, _) = Create(intensity: 0.5f);
        (SimulationEngine doubled, RainEffect rain) = Create(intensity: 0.5f, monitor: s_monitor with { Dpi = 192 });

        Run(normal, seconds: 1);
        Run(doubled, seconds: 1);

        static float Width(SimulationEngine engine) =>
            engine.Frame.Sprites.ToArray().Where(s => s.Shape == SpriteShape.Streak).Max(s => s.HalfSize.Y);

        Width(doubled).Should().BeApproximately(2 * Width(normal), 0.01f);
        rain.PuddleOf(s_window.Id)!.BinWidth.Should().Be(2 * RainEffect.BinWidth);
    }

    private static (SimulationEngine Engine, RainEffect Rain) Create(float intensity, float depth = 0.5f, MonitorInfo? monitor = null, TrackedWindow[]? windows = null)
    {
        var engine = new SimulationEngine(seed: 5);
        var rain = new RainEffect(seed: 5) { Settings = new EffectSettings { Intensity = intensity }, Depth = depth };
        engine.Add(rain);
        engine.SetMonitors([monitor ?? s_monitor]);
        engine.SetWindows(WindowSnapshot.Of(0, windows ?? [s_window]));
        return (engine, rain);
    }

    /// <summary>The tip of a streak (its leading end), or the centre of any other sprite.</summary>
    private static Vector2 Tip(Sprite s) => s.Shape == SpriteShape.Streak
        ? s.Center + (new Vector2(MathF.Cos(s.Rotation), MathF.Sin(s.Rotation)) * s.HalfSize.X)
        : s.Center;

    /// <summary>Falling drops (streaks) and splashes or ripples whose position lies inside <paramref name="bounds"/>.</summary>
    private static Sprite[] RainSpritesInside(SimulationEngine engine, PixelRect bounds) =>
        [.. engine.Frame.Sprites.ToArray().Where(s =>
            s.Shape is SpriteShape.Streak or SpriteShape.Disc or SpriteShape.Ring
            && bounds.Contains((int)MathF.Floor(Tip(s).X), (int)MathF.Floor(Tip(s).Y)))];

    private static void Run(SimulationEngine engine, double seconds)
    {
        int frames = (int)Math.Round(seconds / FrameTime);
        for (int i = 0; i < frames; i++)
        {
            engine.Advance(FrameTime);
        }
    }
}
