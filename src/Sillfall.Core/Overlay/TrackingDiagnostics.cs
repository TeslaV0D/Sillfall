using System.Globalization;
using Sillfall.Core.Desktop;
using Sillfall.Core.Diagnostics;
using Sillfall.Core.Geometry;
using Sillfall.Core.Surfaces;

namespace Sillfall.Core.Overlay;

/// <summary>
/// Checks the window tracking against a window whose true position is known: the app's own window.
/// Its bounds are measured independently (DWM frame bounds) and must match the tracked bounds exactly.
/// </summary>
public static class TrackingDiagnostics
{
    /// <summary>Whether the tracker lists the app window at exactly <paramref name="appWindowBounds"/>.</summary>
    public static DiagnosticResult EvaluateWindow(string name, OverlayStatus status, long appWindowId, PixelRect appWindowBounds)
    {
        ArgumentNullException.ThrowIfNull(status);

        TrackedWindow? tracked = Find(status, appWindowId);
        string summary = string.Create(CultureInfo.InvariantCulture, $"{status.Windows.Count} window(s) tracked, {status.WindowUpdates} update(s)");

        if (tracked is null)
        {
            return new DiagnosticResult(name, false, $"The app window is not tracked. {summary}");
        }

        return tracked.Value.Bounds == appWindowBounds
            ? new DiagnosticResult(name, true, $"App window tracked at {appWindowBounds}. {summary}")
            : new DiagnosticResult(name, false, $"App window tracked at {tracked.Value.Bounds}, but it is at {appWindowBounds}. {summary}");
    }

    /// <summary>
    /// Whether the top edge of the app window is a surface, right on its frame and over its full width.
    /// The app window is the foreground window in this check, so nothing may hide its top edge.
    /// </summary>
    public static DiagnosticResult EvaluateTopEdge(OverlayStatus status, long appWindowId, PixelRect appWindowBounds)
    {
        ArgumentNullException.ThrowIfNull(status);
        const string CheckName = "Surfaces";

        var key = new SurfaceKey(appWindowId, SurfaceEdge.Top);
        var pieces = status.Surfaces.Where(segment => segment.Key == key).ToList();
        string summary = string.Create(
            CultureInfo.InvariantCulture,
            $"{status.Surfaces.Count} segment(s), {status.Surfaces.Count(segment => segment.Key.Edge == SurfaceEdge.Floor)} floor piece(s)");

        if (pieces.Count == 0)
        {
            return new DiagnosticResult(CheckName, false, $"No top edge for the app window. {summary}");
        }

        int covered = pieces.Sum(piece => piece.Length);
        bool onFrame = pieces.All(piece => piece.Position == appWindowBounds.Top && piece.Origin == appWindowBounds.Left);
        bool complete = covered == appWindowBounds.Width;

        return onFrame && complete
            ? new DiagnosticResult(CheckName, true, string.Create(CultureInfo.InvariantCulture, $"App window top edge at row {appWindowBounds.Top}, {covered} px wide. {summary}"))
            : new DiagnosticResult(CheckName, false, string.Create(CultureInfo.InvariantCulture, $"App window top edge covers {covered} of {appWindowBounds.Width} px, rows {string.Join(", ", pieces.Select(piece => piece.Position).Distinct())} (expected {appWindowBounds.Top}). {summary}"));
    }

    /// <summary>Whether the move of the app window produced a move event.</summary>
    public static DiagnosticResult EvaluateMoveEvents(OverlayStatus before, OverlayStatus after)
    {
        ArgumentNullException.ThrowIfNull(before);
        ArgumentNullException.ThrowIfNull(after);

        long moves = Count(after, WindowEventKind.Moved) - Count(before, WindowEventKind.Moved);
        return new DiagnosticResult(
            "Move events",
            moves > 0,
            string.Create(CultureInfo.InvariantCulture, $"{moves} move event(s) while the app window was moved"));
    }

    private static long Count(OverlayStatus status, WindowEventKind kind) =>
        status.WindowEvents.TryGetValue(kind, out long count) ? count : 0;

    private static TrackedWindow? Find(OverlayStatus status, long id)
    {
        foreach (TrackedWindow window in status.Windows)
        {
            if (window.Id == id)
            {
                return window;
            }
        }

        return null;
    }
}
