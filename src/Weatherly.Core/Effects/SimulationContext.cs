using System.Numerics;
using Weatherly.Core.Desktop;
using Weatherly.Core.Displays;
using Weatherly.Core.Simulation;
using Weatherly.Core.Surfaces;

namespace Weatherly.Core.Effects;

/// <summary>
/// The world as the effects see it during a step: monitors, windows, surfaces, wind, cursor and time.
/// One instance is reused for the lifetime of the engine.
/// </summary>
public sealed class SimulationContext
{
    public SimulationContext(uint seed = 1)
    {
        Wind = new Wind(seed);
    }

    public IReadOnlyList<MonitorInfo> Monitors { get; internal set; } = [];

    /// <summary>Windows in z-order, topmost first.</summary>
    public WindowSnapshot Windows { get; } = new();

    /// <summary>Visible surfaces of <see cref="Windows"/> on <see cref="Monitors"/>.</summary>
    public SurfaceSet Surfaces { get; } = new();

    /// <summary>Lookup for "where does a falling particle land".</summary>
    public SurfaceIndex SurfaceIndex { get; } = new();

    public Wind Wind { get; }

    /// <summary>Cursor position in desktop pixels.</summary>
    public Vector2 Cursor { get; internal set; }

    /// <summary>Cursor velocity in pixels per second.</summary>
    public Vector2 CursorVelocity { get; internal set; }

    /// <summary>Simulated time in seconds.</summary>
    public double Time { get; internal set; }

    /// <summary>Union of all monitor bounds: where things can be seen at all.</summary>
    public Geometry.PixelRect DesktopBounds { get; internal set; }

    /// <summary>
    /// Physical pixels per device-independent pixel, from the primary monitor (1 at 100 %, 2 at 200 %).
    /// Effects multiply sizes, distances and speeds with it, so they look the same at every scaling.
    /// </summary>
    public float PixelScale { get; internal set; } = 1f;
}
