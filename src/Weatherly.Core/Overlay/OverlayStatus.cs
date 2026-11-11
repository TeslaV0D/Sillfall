using Weatherly.Core.Displays;
using Weatherly.Core.Geometry;

namespace Weatherly.Core.Overlay;

/// <summary>Measured state of one overlay window. Everything is read back from Windows, nothing is assumed.</summary>
/// <param name="Monitor">The monitor this overlay covers.</param>
/// <param name="WindowHandle">Native handle of the overlay window.</param>
/// <param name="WindowBounds">Actual window rectangle in physical pixels.</param>
/// <param name="HasOverlayStyles">Whether the window carries all styles an overlay needs (layered, transparent, topmost, tool window, no-activate, no redirection bitmap).</param>
/// <param name="IsClickThrough">Whether a hit test in the monitor center misses the overlay, so clicks reach the window below.</param>
/// <param name="IsExcludedFromCapture">Whether screenshots and recordings skip the overlay.</param>
/// <param name="PresentedFrames">Number of frames shown since the overlay was created.</param>
public sealed record OverlayWindowStatus(
    MonitorInfo Monitor,
    nint WindowHandle,
    PixelRect WindowBounds,
    bool HasOverlayStyles,
    bool IsClickThrough,
    bool IsExcludedFromCapture,
    long PresentedFrames)
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
    string? LastError);
