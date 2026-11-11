namespace Weatherly.Core.Desktop;

/// <summary>
/// The windows on the desktop at one point in time, in z-order: the first window is the topmost.
/// Reusable: the tracker refills one instance instead of allocating a new one per update.
/// </summary>
public sealed class WindowSnapshot
{
    private readonly List<TrackedWindow> _windows;

    public WindowSnapshot(int capacity = 64)
    {
        _windows = new List<TrackedWindow>(capacity);
    }

    /// <summary>Capture time in seconds on a monotonic clock.</summary>
    public double Timestamp { get; set; }

    public int Count => _windows.Count;

    /// <summary>The windows, topmost first.</summary>
    public ReadOnlySpan<TrackedWindow> Windows => System.Runtime.InteropServices.CollectionsMarshal.AsSpan(_windows);

    public TrackedWindow this[int index] => _windows[index];

    /// <summary>Creates a snapshot from windows given topmost first.</summary>
    public static WindowSnapshot Of(double timestamp, params ReadOnlySpan<TrackedWindow> windowsTopmostFirst)
    {
        var snapshot = new WindowSnapshot(Math.Max(windowsTopmostFirst.Length, 4)) { Timestamp = timestamp };
        foreach (TrackedWindow window in windowsTopmostFirst)
        {
            snapshot.Add(window);
        }

        return snapshot;
    }

    public void Clear() => _windows.Clear();

    /// <summary>Appends a window below all windows added so far.</summary>
    public void Add(TrackedWindow window) => _windows.Add(window);

    /// <summary>Whether both snapshots list the same windows with the same bounds, traits and order. The timestamp is ignored.</summary>
    public bool HasSameWindows(WindowSnapshot other)
    {
        ArgumentNullException.ThrowIfNull(other);
        return Windows.SequenceEqual(other.Windows);
    }

    /// <summary>Makes this snapshot an exact copy of <paramref name="source"/> without allocating (once capacity suffices).</summary>
    public void CopyFrom(WindowSnapshot source)
    {
        ArgumentNullException.ThrowIfNull(source);

        _windows.Clear();
        _windows.AddRange(source.Windows);
        Timestamp = source.Timestamp;
    }

    /// <summary>Index of the window with <paramref name="id"/>, or -1.</summary>
    public int IndexOf(long id)
    {
        ReadOnlySpan<TrackedWindow> windows = Windows;
        for (int i = 0; i < windows.Length; i++)
        {
            if (windows[i].Id == id)
            {
                return i;
            }
        }

        return -1;
    }
}
