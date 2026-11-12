using Sillfall.Core.Desktop;
using Sillfall.Core.Displays;
using Sillfall.Core.Geometry;

namespace Sillfall.Core.Surfaces;

/// <summary>
/// Computes the visible surfaces of the desktop: window top edges, window sides and the floor of
/// every monitor, minus everything that a window higher in the z-order hides.
/// <para>
/// Rules (PLAN.md, sections 2 and 3.2):
/// <list type="bullet">
/// <item>A top edge is visible where neither its row nor the row above it is covered by a higher window.
/// A higher window that sits directly on the edge leaves no room to pile anything up.</item>
/// <item>A top edge on the upper border of a monitor (maximized window) has no room above it and is dropped.</item>
/// <item>Side surfaces are the columns just outside the window, from its top to its bottom.</item>
/// <item>The floor of a monitor is the bottom of its work area (the top of a bottom taskbar), hidden where a window reaches it.</item>
/// <item>Everything is clipped to the monitors. A window spanning two monitors yields one piece per monitor.</item>
/// </list>
/// </para>
/// Reusable and allocation-free after warm-up, so it can run on every window movement.
/// </summary>
public sealed class SurfaceExtractor
{
    private readonly List<Interval> _visible = new(32);
    private readonly List<Interval> _scratch = new(32);

    /// <summary>Fills <paramref name="output"/> with the visible surfaces of <paramref name="windows"/> on <paramref name="monitors"/>.</summary>
    public void Extract(WindowSnapshot windows, IReadOnlyList<MonitorInfo> monitors, SurfaceSet output)
    {
        ArgumentNullException.ThrowIfNull(windows);
        ArgumentNullException.ThrowIfNull(monitors);
        ArgumentNullException.ThrowIfNull(output);

        output.Clear();
        ReadOnlySpan<TrackedWindow> all = windows.Windows;

        for (int index = 0; index < all.Length; index++)
        {
            TrackedWindow window = all[index];
            if (window.Bounds.IsEmpty)
            {
                continue;
            }

            ReadOnlySpan<TrackedWindow> above = all[..index];
            AddTopEdge(window, above, monitors, output);
            AddSide(window, SurfaceEdge.Left, window.Bounds.Left - 1, above, monitors, output);
            AddSide(window, SurfaceEdge.Right, window.Bounds.Right, above, monitors, output);
        }

        for (int monitorIndex = 0; monitorIndex < monitors.Count; monitorIndex++)
        {
            AddFloor(monitorIndex, monitors[monitorIndex], all, output);
        }
    }

    private void AddTopEdge(in TrackedWindow window, ReadOnlySpan<TrackedWindow> above, IReadOnlyList<MonitorInfo> monitors, SurfaceSet output)
    {
        PixelRect bounds = window.Bounds;
        int row = bounds.Top;
        var key = new SurfaceKey(window.Id, SurfaceEdge.Top);

        for (int monitorIndex = 0; monitorIndex < monitors.Count; monitorIndex++)
        {
            PixelRect monitor = monitors[monitorIndex].Bounds;

            // The edge row must be on this monitor, with at least one row of room above it.
            if (row <= monitor.Top || row >= monitor.Bottom)
            {
                continue;
            }

            if (!Begin(Math.Max(bounds.Left, monitor.Left), Math.Min(bounds.Right, monitor.Right)))
            {
                continue;
            }

            foreach (TrackedWindow higher in above)
            {
                // Covers the edge row or the row above it.
                if (higher.Bounds.Top <= row && higher.Bounds.Bottom >= row)
                {
                    Subtract(higher.Bounds.Left, higher.Bounds.Right);
                }
            }

            Emit(key, row, bounds.Left, monitorIndex, output);
        }
    }

    private void AddSide(in TrackedWindow window, SurfaceEdge edge, int column, ReadOnlySpan<TrackedWindow> above, IReadOnlyList<MonitorInfo> monitors, SurfaceSet output)
    {
        PixelRect bounds = window.Bounds;
        var key = new SurfaceKey(window.Id, edge);

        // The window's own outermost column (inside the frame) next to the surface column.
        int innerColumn = edge == SurfaceEdge.Left ? bounds.Left : bounds.Right - 1;

        for (int monitorIndex = 0; monitorIndex < monitors.Count; monitorIndex++)
        {
            PixelRect monitor = monitors[monitorIndex].Bounds;
            if (column < monitor.Left || column >= monitor.Right)
            {
                continue;
            }

            if (!Begin(Math.Max(bounds.Top, monitor.Top), Math.Min(bounds.Bottom, monitor.Bottom)))
            {
                continue;
            }

            int low = Math.Min(column, innerColumn);
            int high = Math.Max(column, innerColumn);
            foreach (TrackedWindow higher in above)
            {
                // Covers the surface column or the window's own border column next to it.
                if (higher.Bounds.Left <= high && higher.Bounds.Right > low)
                {
                    Subtract(higher.Bounds.Top, higher.Bounds.Bottom);
                }
            }

            Emit(key, column, bounds.Top, monitorIndex, output);
        }
    }

    private void AddFloor(int monitorIndex, MonitorInfo monitor, ReadOnlySpan<TrackedWindow> windows, SurfaceSet output)
    {
        PixelRect workArea = monitor.WorkArea.IsEmpty ? monitor.Bounds : monitor.WorkArea;
        int row = workArea.Bottom;
        var key = new SurfaceKey(monitorIndex, SurfaceEdge.Floor);

        if (!Begin(workArea.Left, workArea.Right))
        {
            return;
        }

        foreach (TrackedWindow window in windows)
        {
            // A window that reaches the floor (touches it or overlaps it) hides it.
            if (window.Bounds.Top <= row && window.Bounds.Bottom >= row)
            {
                Subtract(window.Bounds.Left, window.Bounds.Right);
            }
        }

        Emit(key, row, workArea.Left, monitorIndex, output);
    }

    /// <summary>Starts a new visible range [start, end). Returns false when it is empty.</summary>
    private bool Begin(int start, int end)
    {
        _visible.Clear();
        if (end <= start)
        {
            return false;
        }

        _visible.Add(new Interval(start, end));
        return true;
    }

    /// <summary>Removes [start, end) from the visible ranges.</summary>
    private void Subtract(int start, int end)
    {
        if (end <= start || _visible.Count == 0)
        {
            return;
        }

        _scratch.Clear();
        foreach (Interval interval in _visible)
        {
            if (end <= interval.Start || start >= interval.End)
            {
                _scratch.Add(interval);
                continue;
            }

            if (start > interval.Start)
            {
                _scratch.Add(new Interval(interval.Start, start));
            }

            if (end < interval.End)
            {
                _scratch.Add(new Interval(end, interval.End));
            }
        }

        _visible.Clear();
        _visible.AddRange(_scratch);
    }

    private void Emit(SurfaceKey key, int position, int origin, int monitorIndex, SurfaceSet output)
    {
        foreach (Interval interval in _visible)
        {
            output.Add(new SurfaceSegment(key, position, interval.Start, interval.End, origin, monitorIndex));
        }
    }

    private readonly record struct Interval(int Start, int End);
}
