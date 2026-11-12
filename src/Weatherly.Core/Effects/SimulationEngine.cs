using System.Numerics;
using Weatherly.Core.Desktop;
using Weatherly.Core.Displays;
using Weatherly.Core.Geometry;
using Weatherly.Core.Rendering;
using Weatherly.Core.Simulation;
using Weatherly.Core.Surfaces;

namespace Weatherly.Core.Effects;

/// <summary>
/// Drives all effects: keeps the world (windows, surfaces, wind) up to date, runs the fixed-step
/// simulation and builds the <see cref="RenderFrame"/> for the renderer. Platform-neutral and
/// allocation-free per frame, so it runs the same in the app, the sandbox, FrameDump and tests.
/// </summary>
public sealed class SimulationEngine
{
    private readonly List<IEffectSystem> _effects = [];
    private readonly SurfaceExtractor _extractor = new();
    private readonly WindowMotionTracker _motion = new();
    private readonly List<WindowEvent> _events = [];
    private readonly RenderFrame _frame = new();
    private readonly Dictionary<WindowEventKind, long> _eventCounts = [];
    private bool _surfacesDirty = true;

    public SimulationEngine(uint seed = 1, double step = FixedStepClock.DefaultStep)
    {
        Context = new SimulationContext(seed);
        Clock = new FixedStepClock(step);
    }

    public SimulationContext Context { get; }

    public FixedStepClock Clock { get; }

    public IReadOnlyList<IEffectSystem> Effects => _effects;

    /// <summary>Whether any effect has something to do. If not, the render loop can stop.</summary>
    public bool IsActive
    {
        get
        {
            foreach (IEffectSystem effect in _effects)
            {
                if (effect.IsActive)
                {
                    return true;
                }
            }

            return false;
        }
    }

    /// <summary>The frame built by the last <see cref="Advance"/>.</summary>
    public RenderFrame Frame => _frame;

    /// <summary>Number of window events delivered to the effects so far.</summary>
    public long WindowEventCount { get; private set; }

    /// <summary>Window events delivered so far, per kind.</summary>
    public IReadOnlyDictionary<WindowEventKind, long> WindowEventCounts => _eventCounts;

    /// <summary>Number of window snapshots that differed from the previous one.</summary>
    public long WindowUpdates { get; private set; }

    public void Add(IEffectSystem effect)
    {
        ArgumentNullException.ThrowIfNull(effect);
        _effects.Add(effect);
    }

    public void Remove(IEffectSystem effect) => _effects.Remove(effect);

    /// <summary>Sets the monitor layout. Surfaces are recomputed on the next frame.</summary>
    public void SetMonitors(IReadOnlyList<MonitorInfo> monitors)
    {
        ArgumentNullException.ThrowIfNull(monitors);
        Context.Monitors = monitors;
        Context.DesktopBounds = Union(monitors);
        Context.PixelScale = PrimaryScale(monitors);
        _surfacesDirty = true;
    }

    /// <summary>
    /// Updates the windows. Derives window events (moved, shaken, …) and hands them to the effects.
    /// Cheap when nothing changed: an identical snapshot is ignored. Returns whether anything changed.
    /// </summary>
    public bool SetWindows(WindowSnapshot windows)
    {
        ArgumentNullException.ThrowIfNull(windows);
        bool sameWindows = windows.HasSameWindows(Context.Windows);
        if (sameWindows && !_surfacesDirty)
        {
            return false;
        }

        if (!sameWindows)
        {
            WindowUpdates++;
        }

        Context.Windows.CopyFrom(windows);
        _events.Clear();
        _motion.Update(Context.Windows, _events);
        RebuildSurfaces();

        foreach (WindowEvent windowEvent in _events)
        {
            WindowEventCount++;
            System.Runtime.InteropServices.CollectionsMarshal.GetValueRefOrAddDefault(_eventCounts, windowEvent.Kind, out _)++;
            foreach (IEffectSystem effect in _effects)
            {
                effect.OnWindowEvent(windowEvent, Context);
            }
        }

        return true;
    }

    /// <summary>Forgets the real time that passed while the loop was stopped, so it is not simulated later.</summary>
    public void ResumeAfterIdle() => Clock.Reset();

    public void SetCursor(Vector2 position, Vector2 velocity)
    {
        Context.Cursor = position;
        Context.CursorVelocity = velocity;
    }

    /// <summary>
    /// Simulates the real time <paramref name="elapsedSeconds"/> in fixed steps and builds the frame.
    /// Returns the number of steps that ran.
    /// </summary>
    public int Advance(double elapsedSeconds)
    {
        if (_surfacesDirty)
        {
            RebuildSurfaces();
        }

        int steps = Clock.Advance(elapsedSeconds);
        float dt = (float)Clock.Step;
        for (int i = 0; i < steps; i++)
        {
            Context.Wind.Step(dt);
            Context.Time += Clock.Step;
            foreach (IEffectSystem effect in _effects)
            {
                effect.Simulate(dt, Context);
            }
        }

        _frame.Clear();
        _frame.Time = Context.Time;
        _frame.Surfaces = Context.Surfaces;
        foreach (IEffectSystem effect in _effects)
        {
            effect.Emit(_frame, Context);
        }

        // App rules: no weather over the windows of some processes.
        foreach (TrackedWindow window in Context.Windows.Windows)
        {
            if ((window.Traits & WindowTraits.WeatherHidden) != 0)
            {
                _frame.AddCutout(window.Bounds);
            }
        }

        // Fog and sunlight need to know where the windows are.
        if (_frame.HasAtmosphere)
        {
            foreach (TrackedWindow window in Context.Windows.Windows)
            {
                _frame.AddOccluder(window.Bounds);
            }
        }

        return steps;
    }

    private void RebuildSurfaces()
    {
        _extractor.Extract(Context.Windows, Context.Monitors, Context.Surfaces);
        Context.SurfaceIndex.Build(Context.Surfaces);
        _surfacesDirty = false;
    }

    private static float PrimaryScale(IReadOnlyList<MonitorInfo> monitors)
    {
        foreach (MonitorInfo monitor in monitors)
        {
            if (monitor.IsPrimary)
            {
                return monitor.ScaleFactor;
            }
        }

        return monitors.Count > 0 ? monitors[0].ScaleFactor : 1f;
    }

    private static PixelRect Union(IReadOnlyList<MonitorInfo> monitors)
    {
        if (monitors.Count == 0)
        {
            return default;
        }

        PixelRect union = monitors[0].Bounds;
        foreach (MonitorInfo monitor in monitors)
        {
            union = new PixelRect(
                Math.Min(union.Left, monitor.Bounds.Left),
                Math.Min(union.Top, monitor.Bounds.Top),
                Math.Max(union.Right, monitor.Bounds.Right),
                Math.Max(union.Bottom, monitor.Bounds.Bottom));
        }

        return union;
    }
}
