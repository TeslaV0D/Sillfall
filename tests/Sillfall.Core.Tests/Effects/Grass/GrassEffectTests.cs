using System.Numerics;
using Sillfall.Core.Desktop;
using Sillfall.Core.Displays;
using Sillfall.Core.Effects;
using Sillfall.Core.Effects.Grass;
using Sillfall.Core.Geometry;
using Sillfall.Core.Rendering;

namespace Sillfall.Core.Tests.Effects.Grass;

public class GrassEffectTests
{
    private const double FrameTime = 1.0 / 60;

    private static readonly MonitorInfo s_monitor = new(
        @"\\.\DISPLAY1", PixelRect.FromSize(0, 0, 1920, 1080), PixelRect.FromSize(0, 0, 1920, 1032), MonitorInfo.DefaultDpi, IsPrimary: true);

    private static readonly TrackedWindow s_window = new(7, PixelRect.FromSize(400, 300, 1000, 500));

    [Fact]
    public void Grass_GrowsOnWindowEdgesAndTheFloor_DensityFollowsTheWidth()
    {
        (SimulationEngine engine, GrassEffect grass) = Create(intensity: 1f);

        Run(engine, seconds: 5);

        grass.BladesOn(s_window.Id).Should().BeInRange(200, 300, "about one blade per 4 px of the 1000 px edge");
        grass.BladesOnFloor(0).Should().BeGreaterThan(grass.BladesOn(s_window.Id));
        grass.TallestOn(s_window.Id).Should().BeApproximately(4f, 0.2f, "0.8 px per second for 5 s");
        engine.Frame.Sprites.ToArray().Should().Contain(s => s.Shape == SpriteShape.Blade);
    }

    [Fact]
    public void Grass_StopsGrowingAtItsMaximumHeight()
    {
        (SimulationEngine engine, GrassEffect grass) = Create(intensity: 1f);

        Run(engine, seconds: 40);

        grass.TallestOn(s_window.Id).Should().BeInRange(GrassEffect.MaxBladeHeight * 0.8f, GrassEffect.MaxBladeHeight);
    }

    [Fact]
    public void Rain_MakesItGrowThreeTimesAsFast()
    {
        (SimulationEngine dry, GrassEffect dryGrass) = Create(intensity: 1f);
        (SimulationEngine wet, GrassEffect wetGrass) = Create(intensity: 1f);
        wetGrass.RainIntensity = 1f;

        Run(dry, seconds: 3);
        Run(wet, seconds: 3);

        wetGrass.TallestOn(s_window.Id).Should().BeApproximately(3f * dryGrass.TallestOn(s_window.Id), 0.2f);
    }

    [Fact]
    public void Blades_StandOnTheEdgeAndMoveWithTheWindow()
    {
        (SimulationEngine engine, GrassEffect grass) = Create(intensity: 1f);
        grass.Grow(engine.Context, 1f);
        Run(engine, seconds: 0.1);
        Sprite[] before = [.. Blades(engine).Where(s => s.Center.Y < 310)];
        before.Should().OnlyContain(s => s.Center.X >= 400 && s.Center.X < 1400 && s.Center.Y < 300 && s.Center.Y + s.HalfSize.X <= 300.01f);

        engine.SetWindows(WindowSnapshot.Of(0.2, s_window with { Bounds = PixelRect.FromSize(300, 200, 1000, 500) }));
        Run(engine, seconds: 0.1);

        Sprite[] after = [.. Blades(engine).Where(s => s.Center.Y < 210)];
        after.Length.Should().Be(before.Length);
        after.Should().OnlyContain(s => s.Center.X >= 300 && s.Center.X < 1300);
    }

    [Fact]
    public void Wind_BendsTheBladesWithIt()
    {
        (SimulationEngine engine, GrassEffect grass) = Create(intensity: 1f);
        grass.Grow(engine.Context, 1f);
        engine.Context.Wind.Base = new Vector2(150, 0);
        engine.Context.Wind.Gustiness = 0f;

        Run(engine, seconds: 1);

        Blades(engine).Average(s => s.Variant).Should().BeGreaterThan(0.2f, "the tips lean to the right");
    }

    [Fact]
    public void TheCursor_PushesNearbyBladesAway()
    {
        (SimulationEngine engine, GrassEffect grass) = Create(intensity: 1f);
        grass.Grow(engine.Context, 1f);
        engine.Context.Wind.Base = Vector2.Zero;
        engine.SetCursor(new Vector2(800, 292), Vector2.Zero);

        Run(engine, seconds: 0.1);

        Sprite[] window = [.. Blades(engine).Where(s => s.Center.Y < 300)];
        window.Where(s => s.Center.X is > 803 and < 812).Should().OnlyContain(s => s.Variant > 0.2f, "right of the cursor they bend right");
        window.Where(s => s.Center.X is > 788 and < 797).Should().OnlyContain(s => s.Variant < -0.2f, "left of it they bend left");
    }

    [Fact]
    public void ClosingTheWindow_FadesItsGrass_InsteadOfThrowingItOff()
    {
        (SimulationEngine engine, GrassEffect grass) = Create(intensity: 1f);
        grass.Grow(engine.Context, 1f);

        engine.SetWindows(WindowSnapshot.Of(0.1));
        grass.BladesOn(s_window.Id).Should().Be(0);
        grass.FadingCount.Should().Be(1);
        Run(engine, seconds: 0.3);
        Blades(engine).Where(s => s.Center.Y < 300).Should().NotBeEmpty("still fading where the window was");

        Run(engine, seconds: GrassEffect.FadeSeconds);
        grass.FadingCount.Should().Be(0);
        Blades(engine).Where(s => s.Center.Y < 300).Should().BeEmpty();
    }

    [Fact]
    public void ResizingTheWindow_CutsOrSproutsBlades()
    {
        (SimulationEngine engine, GrassEffect grass) = Create(intensity: 1f);
        Run(engine, seconds: 1);
        int blades = grass.BladesOn(s_window.Id);

        engine.SetWindows(WindowSnapshot.Of(1.1, s_window with { Bounds = PixelRect.FromSize(400, 300, 500, 500) }));
        grass.BladesOn(s_window.Id).Should().BeInRange((blades / 2) - 3, (blades / 2) + 3);

        engine.SetWindows(WindowSnapshot.Of(1.2, s_window with { Bounds = PixelRect.FromSize(400, 300, 1500, 500) }));
        grass.BladesOn(s_window.Id).Should().BeInRange((int)(blades * 1.45), (int)(blades * 1.55));
    }

    [Fact]
    public void Grass_WithersAndStopsAfterIntensityZero()
    {
        (SimulationEngine engine, GrassEffect grass) = Create(intensity: 1f);
        grass.Grow(engine.Context, 1f);
        Run(engine, seconds: 1);

        grass.Settings = new EffectSettings { Intensity = 0f };
        Run(engine, seconds: GrassEffect.WitherSeconds + 0.5);

        grass.IsActive.Should().BeFalse();
        engine.IsActive.Should().BeFalse();
    }

    [Fact]
    public void Blades_HiddenWhereAHigherWindowCoversTheEdge()
    {
        var above = new TrackedWindow(9, PixelRect.FromSize(300, 200, 500, 400));
        (SimulationEngine engine, GrassEffect grass) = Create(intensity: 1f);
        engine.SetWindows(WindowSnapshot.Of(0.1, above, s_window));
        grass.Grow(engine.Context, 1f);

        Run(engine, seconds: 0.1);

        Blades(engine).Where(s => s.Center.Y is > 280 and < 300).Should().OnlyContain(s => s.Center.X >= 800, "the edge left of 800 lies under the higher window");
    }

    [Fact]
    public void FrameLoop_DoesNotAllocateOnceWarm()
    {
        (SimulationEngine engine, GrassEffect grass) = Create(intensity: 1f);
        grass.Grow(engine.Context, 1f);
        WindowSnapshot windows = WindowSnapshot.Of(0, s_window);
        for (int i = 0; i < 800; i++)
        {
            engine.SetWindows(windows);
            engine.SetCursor(new Vector2(800, 290), Vector2.Zero);
            engine.Advance(FrameTime);
        }

        Allocations.Measure(() =>
        {
            for (int i = 0; i < 300; i++)
            {
                engine.SetWindows(windows);
                engine.Advance(FrameTime);
            }
        }).Should().Be(0);
    }

    [Fact]
    public void HighDpi_ScalesTheBlades()
    {
        (SimulationEngine normal, GrassEffect normalGrass) = Create(intensity: 1f);
        (SimulationEngine doubled, GrassEffect doubledGrass) = Create(intensity: 1f, monitor: s_monitor with { Dpi = 192 });

        Run(normal, seconds: 2);
        Run(doubled, seconds: 2);

        doubledGrass.TallestOn(s_window.Id).Should().BeApproximately(2 * normalGrass.TallestOn(s_window.Id), 0.1f);
        doubledGrass.BladesOn(s_window.Id).Should().BeInRange((normalGrass.BladesOn(s_window.Id) / 2) - 2, (normalGrass.BladesOn(s_window.Id) / 2) + 2);
    }

    private static Sprite[] Blades(SimulationEngine engine) => [.. engine.Frame.Sprites.ToArray().Where(s => s.Shape == SpriteShape.Blade)];

    private static (SimulationEngine Engine, GrassEffect Grass) Create(float intensity, MonitorInfo? monitor = null)
    {
        var engine = new SimulationEngine(seed: 5);
        var grass = new GrassEffect(seed: 5) { Settings = new EffectSettings { Intensity = intensity } };
        engine.Add(grass);
        engine.SetMonitors([monitor ?? s_monitor]);
        engine.SetWindows(WindowSnapshot.Of(0, s_window));
        return (engine, grass);
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
