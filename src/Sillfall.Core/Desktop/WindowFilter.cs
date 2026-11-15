namespace Sillfall.Core.Desktop;

/// <summary>What the window tracker reads about a top-level window before it decides to track it. Geometry and styles only.</summary>
/// <param name="Visible">The window is visible (<c>IsWindowVisible</c>).</param>
/// <param name="Minimized">The window is minimized.</param>
/// <param name="Cloaked">DWM hides it (another virtual desktop, a suspended store app).</param>
/// <param name="ExtendedStyle">The window's extended style (<c>GWL_EXSTYLE</c>).</param>
/// <param name="LayeredAlpha">Constant alpha of a layered window (<c>GetLayeredWindowAttributes</c> with <c>LWA_ALPHA</c>), or 255.</param>
public readonly record struct WindowFacts(bool Visible, bool Minimized, bool Cloaked, uint ExtendedStyle, byte LayeredAlpha = 255);

/// <summary>
/// Which top-level windows get weather edges (ADR-0004 §2 and its amendment 2026-11). The class name and size checks
/// stay in the enumerator; this is the part about visibility and styles, so it can be tested.
/// </summary>
public static class WindowFilter
{
    /// <summary><c>WS_EX_TRANSPARENT</c>: hit tests pass through the window.</summary>
    public const uint Transparent = 0x0000_0020;

    /// <summary><c>WS_EX_TOOLWINDOW</c>: palettes and floating toolbars.</summary>
    public const uint ToolWindow = 0x0000_0080;

    /// <summary><c>WS_EX_LAYERED</c>.</summary>
    public const uint Layered = 0x0008_0000;

    /// <summary>
    /// Whether a window counts. Not tracked: invisible, minimized or cloaked windows, tool windows, click-through overlays
    /// (layered and transparent: Sillfall's own, game and recording overlays, other apps' screen layers, finding F-21),
    /// and layered windows that are fully transparent.
    /// </summary>
    public static bool IsTracked(in WindowFacts facts)
    {
        if (!facts.Visible || facts.Minimized || facts.Cloaked)
        {
            return false;
        }

        uint style = facts.ExtendedStyle;
        if ((style & ToolWindow) != 0)
        {
            return false;
        }

        if ((style & Layered) != 0 && ((style & Transparent) != 0 || facts.LayeredAlpha == 0))
        {
            return false;
        }

        return true;
    }
}
