using Weatherly.Core.Desktop;
using Weatherly.Core.Displays;
using Weatherly.Core.Geometry;
using Weatherly.Core.Surfaces;

namespace Weatherly.Core.Overlay;

/// <summary>A visible window that lies above an overlay and overlaps its monitor.</summary>
/// <param name="ClassName">Window class (never the title).</param>
/// <param name="IsTopmost">Whether the window is in the topmost band itself.</param>
public sealed record CoveringWindow(string ClassName, bool IsTopmost)
{
    public override string ToString() => IsTopmost ? $"{ClassName} (topmost)" : ClassName;
}

/// <summary>Measured state of one overlay window. Everything is read back from Windows, nothing is assumed.</summary>
/// <param name="Monitor">The monitor this overlay covers.</param>
/// <param name="WindowHandle">Native handle of the overlay window.</param>
/// <param name="WindowBounds">Actual window rectangle in physical pixels.</param>
/// <param name="HasOverlayStyles">Whether the window carries all styles an overlay needs (layered, transparent, topmost, tool window, no-activate, no redirection bitmap).</param>
/// <param name="IsClickThrough">Whether a hit test in the monitor center misses the overlay, so clicks reach the window below.</param>
/// <param name="IsExcludedFromCapture">Whether screenshots and recordings skip the overlay.</param>
/// <param name="PresentedFrames">Number of frames shown since the overlay was created.</param>
/// <param name="CoveredBy">Visible windows above the overlay on its monitor, topmost first.</param>
public sealed record OverlayWindowStatus(
    MonitorInfo Monitor,
    nint WindowHandle,
    PixelRect WindowBounds,
    bool HasOverlayStyles,
    bool IsClickThrough,
    bool IsExcludedFromCapture,
    long PresentedFrames,
    IReadOnlyList<CoveringWindow> CoveredBy)
{
    /// <summary>Whether the window sits exactly on its monitor.</summary>
    public bool CoversMonitor => WindowBounds == Monitor.Bounds;
}

/// <summary>Snapshot of the overlay system for diagnostics, the settings UI and the smoke test.</summary>
/// <param name="Monitors">All monitors of the desktop, whether they have an overlay or not.</param>
/// <param name="Overlays">The overlay windows that exist right now.</param>
/// <param name="AdapterName">Graphics adapter that renders the overlays, or <see langword="null"/> while no overlay exists.</param>
/// <param name="OverlayHasFocus">Whether an overlay is the foreground window. Must never be true.</param>
/// <param name="LastError">Most recent failure on the overlay thread, or <see langword="null"/>.</param>
public sealed record OverlayStatus(
    IReadOnlyList<MonitorInfo> Monitors,
    IReadOnlyList<OverlayWindowStatus> Overlays,
    string? AdapterName,
    bool OverlayHasFocus,
    string? LastError)
{
    /// <summary>Tracked application windows, topmost first.</summary>
    public IReadOnlyList<TrackedWindow> Windows { get; init; } = [];

    /// <summary>Visible surfaces computed from <see cref="Windows"/>.</summary>
    public IReadOnlyList<SurfaceSegment> Surfaces { get; init; } = [];

    /// <summary>Number of window snapshots that differed from the previous one.</summary>
    public long WindowUpdates { get; init; }

    /// <summary>Window events seen so far, per kind.</summary>
    public IReadOnlyDictionary<WindowEventKind, long> WindowEvents { get; init; } = new Dictionary<WindowEventKind, long>();

    /// <summary>Whether the render loop is running. False while no effect is active.</summary>
    public bool IsAnimating { get; init; }

    /// <summary>Frames rendered by the render loop since start.</summary>
    public long AnimationFrames { get; init; }

    /// <summary>Average time between animation frames over the last second, in milliseconds. 0 when idle.</summary>
    public double FrameMilliseconds { get; init; }

    /// <summary>Average CPU time of simulating and building one frame over the last second, in milliseconds.</summary>
    public double SimulationMilliseconds { get; init; }

    /// <summary>Sprites in the last frame.</summary>
    public int Sprites { get; init; }

    /// <summary>Frames of the render loop whose allocations were measured (frames after the warm-up).</summary>
    public long MeasuredLoopFrames { get; init; }

    /// <summary>Bytes allocated by the overlay thread during the measured loop frames. Must be 0.</summary>
    public long LoopAllocatedBytes { get; init; }
}
