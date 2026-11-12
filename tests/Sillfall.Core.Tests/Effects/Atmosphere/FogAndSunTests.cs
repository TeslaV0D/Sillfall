using System.Numerics;
using Sillfall.Core.Desktop;
using Sillfall.Core.Displays;
using Sillfall.Core.Effects;
using Sillfall.Core.Effects.Fog;
using Sillfall.Core.Effects.Sun;
using Sillfall.Core.Geometry;
using Sillfall.Core.Rendering;

namespace Sillfall.Core.Tests.Effects.Atmosphere;

public class FogAndSunTests
{
    private const double FrameTime = 1.0 / 60;

    private static readonly MonitorInfo s_monitor = new(
        @"\\.\DISPLAY1", PixelRect.FromSize(0, 0, 1920, 1080), PixelRect.FromSize(0, 0, 1920, 1032), MonitorInfo.DefaultDpi, IsPrimary: true);

    private static readonly TrackedWindow s_window = new(7, PixelRect.FromSize(400, 300, 1000, 500));

    [Fact]
    public void Fog_RollsInSlowlyInsteadOfSwitchingOn()
    {
        (SimulationEngine engine, FogEffect fog, _) = Create(fog: 1f);

        Run(engine, seconds: 0.5);
        float early = engine.Frame.Fog.Density;
        Run(engine, seconds: 6);

        early.Should().BeInRange(0.05f, 0.6f);
        fog.Density.Should().BeGreaterThan(0.95f);
        engine.Frame.Fog.MaxAlpha.Should().Be(FogEffect.MaxAlpha);
        engine.Frame.HasAtmosphere.Should().BeTrue();
    }

    [Fact]
    public void Fog_DriftsWithTheWind()
    {
        (SimulationEngine engine, _, _) = Create(fog: 1f);
        engine.Context.Wind.Base = new Vector2(100, 0);
        engine.Context.Wind.Gustiness = 0f;

        Run(engine, seconds: 1);
        Vector2 before = engine.Frame.Fog.Scroll;
        Run(engine, seconds: 1);

        (engine.Frame.Fog.Scroll.X - before.X).Should().BeApproximately(66f, 1f, "60 % of the wind plus a slow drift of 6 px/s");
    }

    [Fact]
    public void Fog_RollsOutAndStopsAfterIntensityZero()
    {
        (SimulationEngine engine, FogEffect fog, _) = Create(fog: 1f);
        Run(engine, seconds: 6);

        fog.Settings = new EffectSettings { Intensity = 0f };
        Run(engine, seconds: 10);

        fog.IsActive.Should().BeFalse();
        engine.IsActive.Should().BeFalse();
        engine.Frame.HasAtmosphere.Should().BeFalse();
    }

    [Fact]
    public void Fog_CustomColorAndBrightness_AreApplied()
    {
        (SimulationEngine engine, FogEffect fog, _) = Create(fog: 1f);
        fog.Settings = new EffectSettings { Intensity = 1f, Color = new Vector3(0.2f, 0.3f, 0.4f), Brightness = 0.5f };

        Run(engine, seconds: 2);

        engine.Frame.Fog.Color.Should().Be(new Vector3(0.2f, 0.3f, 0.4f));
        engine.Frame.Fog.MaxAlpha.Should().BeApproximately(FogEffect.MaxAlpha * 0.5f, 1e-6f);
    }

    [Fact]
    public void Sun_StandsAboveTheChosenUpperCorner()
    {
        (SimulationEngine engine, _, SunEffect sun) = Create(sun: 1f);
        Run(engine, seconds: 1);
        engine.Frame.Sun.Position.X.Should().BeGreaterThan(1920 * 0.75f);
        engine.Frame.Sun.Position.Y.Should().BeLessThan(0f);

        sun.Side = SunSide.Left;
        Run(engine, seconds: 1);
        engine.Frame.Sun.Position.X.Should().BeLessThan(1920 * 0.25f);
    }

    [Fact]
    public void Sun_FadesInAndOutAndStops()
    {
        (SimulationEngine engine, _, SunEffect sun) = Create(sun: 1f);
        Run(engine, seconds: 0.3);
        engine.Frame.Sun.Strength.Should().BeInRange(0.05f, 0.6f);
        Run(engine, seconds: 5);
        engine.Frame.Sun.Strength.Should().BeGreaterThan(0.95f);

        sun.Settings = new EffectSettings { Intensity = 0f };
        Run(engine, seconds: 8);

        sun.IsActive.Should().BeFalse();
        engine.IsActive.Should().BeFalse();
    }

    [Fact]
    public void Sun_DustFloatsInTheLight()
    {
        (SimulationEngine engine, _, _) = Create(sun: 1f);

        Run(engine, seconds: 5);

        Sprite[] dust = [.. engine.Frame.Sprites.ToArray().Where(s => s.Shape == SpriteShape.Disc)];
        dust.Length.Should().BeInRange(10, SunEffect.DustCount);
        dust.Should().OnlyContain(s => s.Center.X >= 0 && s.Center.X < 1920 && s.Center.Y >= 0 && s.Center.Y < 1080);
    }

    [Fact]
    public void Atmosphere_CarriesTheWindowsAsOccluders_TopmostFirst()
    {
        var below = new TrackedWindow(8, PixelRect.FromSize(100, 100, 300, 200));
        (SimulationEngine engine, _, _) = Create(fog: 1f);
        engine.SetWindows(WindowSnapshot.Of(0.1, s_window, below));

        Run(engine, seconds: 0.5);

        engine.Frame.Occluders.ToArray().Should().Equal(s_window.Bounds, below.Bounds);
    }

    [Fact]
    public void WithoutAtmosphere_TheFrameCarriesNoOccluders()
    {
        (SimulationEngine engine, _, _) = Create();

        Run(engine, seconds: 0.5);

        engine.Frame.HasAtmosphere.Should().BeFalse();
        engine.Frame.Occluders.Length.Should().Be(0);
    }

    [Fact]
    public void WindowsHiddenByAnAppRule_BecomeCutouts_WithOrWithoutAtmosphere()
    {
        var hidden = new TrackedWindow(8, PixelRect.FromSize(100, 100, 300, 200), WindowTraits.WeatherHidden);
        (SimulationEngine engine, _, _) = Create();
        engine.SetWindows(WindowSnapshot.Of(0.1, s_window, hidden));

        Run(engine, seconds: 0.1);

        engine.Frame.Cutouts.ToArray().Should().Equal(hidden.Bounds);
        engine.Frame.HasAtmosphere.Should().BeFalse();
    }

    [Fact]
    public void RenderFrame_KeepsAtMostMaxOccluders()
    {
        var frame = new RenderFrame();
        for (int i = 0; i < RenderFrame.MaxOccluders + 10; i++)
        {
            frame.AddOccluder(PixelRect.FromSize(i, 0, 10, 10));
        }

        frame.AddOccluder(default);

        frame.Occluders.Length.Should().Be(RenderFrame.MaxOccluders);
        frame.Clear();
        frame.Occluders.Length.Should().Be(0);
    }

    [Fact]
    public void FrameLoop_DoesNotAllocateOnceWarm()
    {
        (SimulationEngine engine, _, _) = Create(fog: 1f, sun: 1f);
        WindowSnapshot windows = WindowSnapshot.Of(0, s_window);
        // Long enough for tiered compilation to settle (as in the other effects' allocation tests).
        for (int i = 0; i < 800; i++)
        {
            engine.SetWindows(windows);
            engine.Advance(FrameTime);
        }

        long before = GC.GetAllocatedBytesForCurrentThread();
        for (int i = 0; i < 300; i++)
        {
            engine.SetWindows(windows);
            engine.Advance(FrameTime);
        }

        (GC.GetAllocatedBytesForCurrentThread() - before).Should().Be(0);
    }

    private static (SimulationEngine Engine, FogEffect Fog, SunEffect Sun) Create(float fog = 0f, float sun = 0f)
    {
        var engine = new SimulationEngine(seed: 5);
        var fogEffect = new FogEffect(seed: 5) { Settings = new EffectSettings { Intensity = fog } };
        var sunEffect = new SunEffect(seed: 5) { Settings = new EffectSettings { Intensity = sun } };
        engine.Add(sunEffect);
        engine.Add(fogEffect);
        engine.SetMonitors([s_monitor]);
        engine.SetWindows(WindowSnapshot.Of(0, s_window));
        return (engine, fogEffect, sunEffect);
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
