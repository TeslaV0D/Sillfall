using System.Numerics;
using Sillfall.Core.Desktop;
using Sillfall.Core.Displays;
using Sillfall.Core.Effects;
using Sillfall.Core.Geometry;
using Sillfall.Core.Rendering;
using Sillfall.Core.Surfaces;

namespace Sillfall.Core.Previews;

/// <summary>One captured frame of a scene.</summary>
/// <param name="Time">Simulated time in seconds.</param>
/// <param name="Index">Position in <see cref="PreviewScene.Captures"/>.</param>
/// <param name="Scene">Sprites of the frame.</param>
/// <param name="Surfaces">Surfaces at that time.</param>
/// <param name="Windows">Window layout at that time, topmost first.</param>
public readonly record struct PreviewCapture(double Time, int Index, RenderFrame Scene, SurfaceSet Surfaces, WindowSnapshot Windows);

/// <summary>
/// Plays a <see cref="PreviewScene"/> through the simulation engine with a fixed frame rate, so the
/// same scene always yields the same frames. Rendering is up to the caller (FrameDump, golden tests).
/// </summary>
public static class PreviewSceneRunner
{
    /// <summary>The monitor of a scene as the engine sees it: at the desktop origin.</summary>
    public static MonitorInfo MonitorOf(PreviewScene scene)
    {
        ArgumentNullException.ThrowIfNull(scene);
        PreviewMonitor monitor = scene.Monitor;
        PixelRect bounds = PixelRect.FromSize(0, 0, monitor.Width, monitor.Height);
        PixelRect workArea = PixelRect.FromSize(0, 0, monitor.Width, monitor.Height - monitor.Taskbar);
        return new MonitorInfo(@"\\.\PREVIEW1", bounds, workArea, (int)MathF.Round(MonitorInfo.DefaultDpi * monitor.Scale), IsPrimary: true);
    }

    /// <summary>Runs the scene and calls <paramref name="onCapture"/> for every capture time, in order.</summary>
    public static void Run(PreviewScene scene, Action<PreviewCapture> onCapture)
    {
        ArgumentNullException.ThrowIfNull(scene);
        ArgumentNullException.ThrowIfNull(onCapture);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(scene.FrameRate);

        var engine = new SimulationEngine(scene.Seed);

        // Sun and fog first: they lie behind everything (PLAN.md, section 4.7).
        var sun = new Effects.Sun.SunEffect(seed: scene.Seed)
        {
            Settings = new EffectSettings { Intensity = scene.Effects.Sun },
            Side = string.Equals(scene.Effects.SunSide, "left", StringComparison.OrdinalIgnoreCase) ? Effects.Sun.SunSide.Left : Effects.Sun.SunSide.Right,
        };
        var fog = new Effects.Fog.FogEffect(seed: scene.Seed) { Settings = new EffectSettings { Intensity = scene.Effects.Fog } };
        if (scene.Effects.SkipFadeIn)
        {
            sun.SkipTransition();
            fog.SkipTransition();
        }

        engine.Add(sun);
        engine.Add(fog);
        var grass = new Effects.Grass.GrassEffect(seed: scene.Seed)
        {
            Settings = new EffectSettings { Intensity = scene.Effects.Grass },
            RainIntensity = scene.Effects.Rain,
        };
        engine.Add(grass);
        var drops = new TestDropsEffect(seed: scene.Seed) { Intensity = scene.Effects.TestDrops };
        engine.Add(drops);
        engine.Add(new Effects.Rain.RainEffect(seed: scene.Seed)
        {
            Settings = new EffectSettings { Intensity = scene.Effects.Rain },
            Depth = scene.Effects.RainDepth,
        });
        var snow = new Effects.Snow.SnowEffect(seed: scene.Seed)
        {
            Settings = new EffectSettings { Intensity = scene.Effects.Snow },
            RainIntensity = scene.Effects.Rain,
        };
        engine.Add(snow);
        var leaves = new Effects.Leaves.LeafEffect(seed: scene.Seed)
        {
            Settings = new EffectSettings { Intensity = scene.Effects.Leaves },
        };
        engine.Add(leaves);
        engine.Context.Wind.Base = new Vector2(scene.Wind.X, scene.Wind.Y);
        engine.Context.Wind.Gustiness = scene.Wind.Gustiness;
        engine.SetMonitors([MonitorOf(scene)]);

        var windows = scene.Windows.ToDictionary(window => window.Id, ToTracked);
        var order = scene.Windows.Select(window => window.Id).ToList();
        var snapshot = new WindowSnapshot();
        Publish(engine, snapshot, windows, order, 0);
        if (scene.Effects.SnowFill > 0f)
        {
            snow.Fill(engine.Context, scene.Effects.SnowFill);
        }

        if (scene.Effects.GrassGrown > 0f)
        {
            grass.Grow(engine.Context, scene.Effects.GrassGrown);
        }

        if (scene.Effects.LeavesScatter > 0f)
        {
            leaves.Scatter(engine.Context, scene.Effects.LeavesScatter);
        }

        // The cursor rests far away until an event moves it; its velocity comes from the positions, frame by frame.
        var cursor = new Simulation.CursorMotion();
        var cursorPosition = new Vector2(-10_000, -10_000);

        var events = scene.Events.OrderBy(e => e.At).ToList();
        var captures = scene.Captures.Select((time, index) => (time, index)).OrderBy(capture => capture.time).ToList();
        double frameTime = 1.0 / scene.FrameRate;
        int nextEvent = 0;
        int nextCapture = 0;
        int frame = 0;

        while (nextCapture < captures.Count)
        {
            double time = frame * frameTime;

            bool windowsChanged = false;
            while (nextEvent < events.Count && events[nextEvent].At <= time + 1e-9)
            {
                PreviewEvent e = events[nextEvent++];
                if (e.CursorX is { } cursorX && e.CursorY is { } cursorY)
                {
                    cursorPosition = new Vector2(cursorX, cursorY);
                    continue;
                }

                if (e.Close)
                {
                    windows.Remove(e.Window);
                    order.Remove(e.Window);
                }
                else if (windows.TryGetValue(e.Window, out TrackedWindow window))
                {
                    windows[e.Window] = window with { Bounds = PixelRect.FromSize(e.X, e.Y, window.Bounds.Width, window.Bounds.Height) };
                }

                windowsChanged = true;
            }

            if (windowsChanged)
            {
                Publish(engine, snapshot, windows, order, time);
            }

            cursor.Update(cursorPosition, time);
            engine.SetCursor(cursor.Position, cursor.Velocity);
            engine.Advance(frame == 0 ? 0 : frameTime);

            while (nextCapture < captures.Count && captures[nextCapture].time <= time + 1e-9)
            {
                onCapture(new PreviewCapture(time, captures[nextCapture].index, engine.Frame, engine.Context.Surfaces, engine.Context.Windows));
                nextCapture++;
            }

            frame++;
        }
    }

    private static TrackedWindow ToTracked(PreviewWindow window) =>
        new(window.Id, PixelRect.FromSize(window.X, window.Y, window.Width, window.Height), window.Maximized ? WindowTraits.Maximized : WindowTraits.None);

    private static void Publish(SimulationEngine engine, WindowSnapshot snapshot, Dictionary<long, TrackedWindow> windows, List<long> order, double time)
    {
        snapshot.Clear();
        snapshot.Timestamp = time;
        foreach (long id in order)
        {
            snapshot.Add(windows[id]);
        }

        engine.SetWindows(snapshot);
    }
}
