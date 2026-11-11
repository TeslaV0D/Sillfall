using Weatherly.Core.Geometry;

namespace Weatherly.Core.Desktop;

/// <summary>Properties of a tracked window that matter for the effects.</summary>
[Flags]
public enum WindowTraits
{
    None = 0,

    /// <summary>The window is maximized. Its top edge sits on the monitor edge, so nothing can pile up on it.</summary>
    Maximized = 1,

    /// <summary>The window is in the topmost band (always on top).</summary>
    Topmost = 2,

    /// <summary>An app rule of the user hides the weather over this window (PLAN.md, section 7: Apps page).</summary>
    WeatherHidden = 4,
}

/// <summary>
/// One application window as the effects see it: an identity, a rectangle and a few flags.
/// Nothing about the window content, title or owner is ever recorded.
/// </summary>
/// <param name="Id">Stable identity while the window exists (the window handle).</param>
/// <param name="Bounds">Visible frame in physical pixels of the virtual desktop, without invisible resize borders.</param>
/// <param name="Traits">Additional state.</param>
public readonly record struct TrackedWindow(long Id, PixelRect Bounds, WindowTraits Traits = WindowTraits.None);
