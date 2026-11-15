namespace Sillfall.Core.Desktop;

/// <summary>
/// Which particles the windows hide (ADR-0006 amendment 2026-11, finding F-05). Windows are listed topmost first.
/// <para>
/// A particle has a <em>home</em>: the window whose top edge it came from or belongs to (a splash, a drip, water or snow
/// sliding off), or none (0) for everything that falls from the sky or rests on the floor. Two rules:
/// </para>
/// <list type="bullet">
/// <item><see cref="HidesFalling"/>: a falling particle (drop, flake, clump, leaf) is drawn over a window only if that
/// window is its home. Rain never falls across the content of a window it did not land on: not over a maximized window,
/// not below a covered top edge, and gutter streams end at the first window below.</item>
/// <item><see cref="HidesAttached"/>: a particle attached to an edge (splash, ripple, drip) is covered only by windows in
/// front of its home. Above a window's top edge it stays visible over the windows behind.</item>
/// </list>
/// Both are plain loops over the window list: no allocation, cheap for the few dozen windows of a desktop.
/// </summary>
public static class WindowOcclusion
{
    /// <summary>No home: the particle falls from the sky or belongs to the floor.</summary>
    public const long NoHome = 0;

    /// <summary>
    /// Whether a falling particle at (<paramref name="x"/>, <paramref name="y"/>) is hidden: the frontmost window there is
    /// not its <paramref name="home"/>.
    /// </summary>
    public static bool HidesFalling(ReadOnlySpan<TrackedWindow> windowsTopmostFirst, float x, float y, long home)
    {
        int px = (int)MathF.Floor(x);
        int py = (int)MathF.Floor(y);
        foreach (TrackedWindow window in windowsTopmostFirst)
        {
            if (window.Bounds.Contains(px, py))
            {
                return home == NoHome || window.Id != home;
            }
        }

        return false;
    }

    /// <summary>
    /// Whether an edge-attached particle at (<paramref name="x"/>, <paramref name="y"/>) is hidden: a window in front of
    /// its <paramref name="home"/> covers the point. Without a home (the floor) any window covers it.
    /// </summary>
    public static bool HidesAttached(ReadOnlySpan<TrackedWindow> windowsTopmostFirst, float x, float y, long home)
    {
        int px = (int)MathF.Floor(x);
        int py = (int)MathF.Floor(y);
        foreach (TrackedWindow window in windowsTopmostFirst)
        {
            if (home != NoHome && window.Id == home)
            {
                return false;
            }

            if (window.Bounds.Contains(px, py))
            {
                return true;
            }
        }

        return false;
    }
}
