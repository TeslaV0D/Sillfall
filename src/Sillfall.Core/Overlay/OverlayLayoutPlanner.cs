using Sillfall.Core.Displays;

namespace Sillfall.Core.Overlay;

/// <summary>What has to happen to the overlay windows after the monitor setup changed.</summary>
/// <param name="ToRemove">Device names of overlays to destroy.</param>
/// <param name="ToCreate">Monitors that need a new overlay.</param>
/// <param name="ToKeep">Monitors whose overlay stays as it is, with their latest data.</param>
public sealed record OverlayLayoutPlan(
    IReadOnlyList<string> ToRemove,
    IReadOnlyList<MonitorInfo> ToCreate,
    IReadOnlyList<MonitorInfo> ToKeep)
{
    public bool HasChanges => ToRemove.Count > 0 || ToCreate.Count > 0;
}

/// <summary>
/// Decides which overlays to destroy, create or keep when monitors are plugged in, unplugged,
/// rearranged or rescaled. There is one overlay per monitor, identified by the device name.
/// </summary>
public static class OverlayLayoutPlanner
{
    /// <summary>
    /// Compares the overlays that exist (<paramref name="current"/>) with the monitors that should have one
    /// (<paramref name="target"/>). An overlay whose monitor changed position, size or DPI is rebuilt
    /// (removed and created) instead of being resized in place: a rebuild is rare, and it leaves no
    /// stale swap chain or window state behind.
    /// </summary>
    public static OverlayLayoutPlan Plan(IReadOnlyList<MonitorInfo> current, IReadOnlyList<MonitorInfo> target)
    {
        ArgumentNullException.ThrowIfNull(current);
        ArgumentNullException.ThrowIfNull(target);

        var wanted = new Dictionary<string, MonitorInfo>(StringComparer.OrdinalIgnoreCase);
        foreach (MonitorInfo monitor in target)
        {
            // A monitor without area cannot show anything. Mirrored displays report one entry, but
            // should a name ever repeat, the first one wins.
            if (!monitor.Bounds.IsEmpty)
            {
                wanted.TryAdd(monitor.DeviceName, monitor);
            }
        }

        var toRemove = new List<string>();
        var toKeep = new List<MonitorInfo>();
        var existing = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (MonitorInfo overlay in current)
        {
            if (!existing.Add(overlay.DeviceName))
            {
                continue;
            }

            if (wanted.TryGetValue(overlay.DeviceName, out MonitorInfo? monitor) && HasSameSurface(overlay, monitor))
            {
                toKeep.Add(monitor);
                wanted.Remove(overlay.DeviceName);
            }
            else
            {
                toRemove.Add(overlay.DeviceName);
            }
        }

        // Everything still wanted is either new or was just scheduled for removal: create it.
        var toCreate = new List<MonitorInfo>(wanted.Count);
        foreach (MonitorInfo monitor in target)
        {
            if (wanted.Remove(monitor.DeviceName, out MonitorInfo? pending))
            {
                toCreate.Add(pending);
            }
        }

        return new OverlayLayoutPlan(toRemove, toCreate, toKeep);
    }

    /// <summary>The work area and the primary flag do not affect the overlay window, only bounds and DPI do.</summary>
    private static bool HasSameSurface(MonitorInfo a, MonitorInfo b) => a.Bounds == b.Bounds && a.Dpi == b.Dpi;
}
