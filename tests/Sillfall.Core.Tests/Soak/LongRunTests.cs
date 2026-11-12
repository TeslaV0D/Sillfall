using Sillfall.Core.Desktop;
using Sillfall.Core.Displays;
using Sillfall.Core.Effects;
using Sillfall.Core.Effects.Fog;
using Sillfall.Core.Effects.Grass;
using Sillfall.Core.Effects.Leaves;
using Sillfall.Core.Effects.Rain;
using Sillfall.Core.Effects.Snow;
using Sillfall.Core.Effects.Sun;
using Sillfall.Core.Geometry;
using Sillfall.Core.Simulation;
using Sillfall.Core.Visitors;

namespace Sillfall.Core.Tests.Soak;

/// <summary>
/// The simulation over a long time with everything on and windows coming, moving and going (PLAN.md, M13). Whatever a
/// window leaves behind (snow fields, puddles, grass, visitors) must go with it: the managed memory must not grow.
/// The 8-hour test of the whole app runs with <c>tools/ci/soak-test.ps1</c>.
/// </summary>
public class LongRunTests
{
    private const double FrameTime = 1.0 / 30;

    private static readonly MonitorInfo s_monitor = new(
        @"\\.\DISPLAY1", PixelRect.FromSize(0, 0, 1920, 1080), PixelRect.FromSize(0, 0, 1920, 1032), MonitorInfo.DefaultDpi, IsPrimary: true);

    [Fact]
    public void TenMinutes_WithWindowChurn_DoNotGrowTheMemory()
    {
        var engine = new SimulationEngine(seed: 9);
        var visitors = new VisitorSystem(seed: 9);
        engine.Add(new SunEffect(seed: 9) { Settings = new EffectSettings { Intensity = 0.5f } });
        engine.Add(new FogEffect(seed: 9) { Settings = new EffectSettings { Intensity = 0.4f } });
        engine.Add(new GrassEffect(seed: 9) { Settings = new EffectSettings { Intensity = 0.6f } });
        engine.Add(new RainEffect(seed: 9) { Settings = new EffectSettings { Intensity = 0.5f } });
        engine.Add(new SnowEffect(seed: 9) { Settings = new EffectSettings { Intensity = 0.4f } });
        engine.Add(new LeafEffect(seed: 9) { Settings = new EffectSettings { Intensity = 0.5f } });
        engine.Add(visitors);
        engine.SetMonitors([s_monitor]);

        var random = new Pcg32(21);
        var windows = new List<TrackedWindow>();
        long nextId = 100;
        long Memory()
        {
            GC.Collect();
            GC.WaitForPendingFinalizers();
            GC.Collect();
            return GC.GetTotalMemory(forceFullCollection: true);
        }

        long warm = 0;
        int frames = (int)(10 * 60 / FrameTime);
        for (int frame = 0; frame < frames; frame++)
        {
            // Every few seconds the desktop changes: a window opens, closes, moves or is resized.
            if (frame % 90 == 0)
            {
                Churn(windows, ref random, ref nextId);
                engine.SetWindows(WindowSnapshot.Of(frame * FrameTime, [.. windows]));
            }

            if (frame % 600 == 0 && visitors.Count < VisitorSystem.Capacity)
            {
                visitors.Spawn((VisitorKind)random.NextInt(8), engine.Context);
            }

            engine.Advance(FrameTime);

            // After two minutes the fields and pools have reached their working size.
            if (frame == (int)(2 * 60 / FrameTime))
            {
                warm = Memory();
            }
        }

        long end = Memory();
        (end - warm).Should().BeLessThan(2 * 1024 * 1024, $"the managed heap went from {warm / 1024} KB to {end / 1024} KB while windows came and went");
        engine.WindowUpdates.Should().BeGreaterThan(150, "the desktop really changed");
    }

    private static void Churn(List<TrackedWindow> windows, ref Pcg32 random, ref long nextId)
    {
        int action = windows.Count < 3 ? 0 : windows.Count > 8 ? 1 : random.NextInt(4);
        switch (action)
        {
            case 0:
                windows.Insert(0, new TrackedWindow(nextId++, PixelRect.FromSize(random.NextInt(1400), 100 + random.NextInt(600), 300 + random.NextInt(500), 200 + random.NextInt(300))));
                break;
            case 1:
                windows.RemoveAt(random.NextInt(windows.Count));
                break;
            case 2:
                {
                    int index = random.NextInt(windows.Count);
                    PixelRect bounds = windows[index].Bounds;
                    windows[index] = windows[index] with { Bounds = PixelRect.FromSize(random.NextInt(1400), 100 + random.NextInt(600), bounds.Width, bounds.Height) };
                    break;
                }

            default:
                {
                    int index = random.NextInt(windows.Count);
                    PixelRect bounds = windows[index].Bounds;
                    windows[index] = windows[index] with { Bounds = PixelRect.FromSize(bounds.Left, bounds.Top, 300 + random.NextInt(500), bounds.Height) };
                    break;
                }
        }
    }
}
