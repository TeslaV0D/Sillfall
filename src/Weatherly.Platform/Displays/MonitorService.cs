using Weatherly.Core.Displays;
using Weatherly.Core.Geometry;
using Windows.Win32;
using Windows.Win32.Foundation;
using Windows.Win32.Graphics.Gdi;
using Windows.Win32.UI.HiDpi;

namespace Weatherly.Platform.Displays;

/// <summary>Reads the monitor setup of the desktop.</summary>
public static class MonitorService
{
    private const uint PrimaryMonitorFlag = 0x1; // MONITORINFOF_PRIMARY

    /// <summary>
    /// Returns all monitors with bounds in physical pixels. The calling thread must be
    /// Per-Monitor-V2 DPI aware, otherwise Windows reports scaled coordinates.
    /// </summary>
    public static unsafe IReadOnlyList<MonitorInfo> GetMonitors()
    {
        var monitors = new List<MonitorInfo>();

        BOOL Collect(HMONITOR monitor, HDC deviceContext, RECT* clip, LPARAM data)
        {
            if (TryDescribe(monitor) is { } info)
            {
                monitors.Add(info);
            }

            return true;
        }

        PInvoke.EnumDisplayMonitors(HDC.Null, (RECT*)null, Collect, 0);

        // Stable order for diagnostics and tests: primary first, then left to right, top to bottom.
        monitors.Sort(static (a, b) =>
        {
            int byPrimary = b.IsPrimary.CompareTo(a.IsPrimary);
            if (byPrimary != 0)
            {
                return byPrimary;
            }

            int byLeft = a.Bounds.Left.CompareTo(b.Bounds.Left);
            return byLeft != 0 ? byLeft : a.Bounds.Top.CompareTo(b.Bounds.Top);
        });

        return monitors;
    }

    private static unsafe MonitorInfo? TryDescribe(HMONITOR monitor)
    {
        var info = default(MONITORINFOEXW);
        info.monitorInfo.cbSize = (uint)sizeof(MONITORINFOEXW);

        // The monitor can disappear between enumeration and this call (cable pulled). Then it is simply skipped.
        if (!PInvoke.GetMonitorInfo(monitor, (MONITORINFO*)&info))
        {
            return null;
        }

        int dpi = PInvoke.GetDpiForMonitor(monitor, MONITOR_DPI_TYPE.MDT_EFFECTIVE_DPI, out uint dpiX, out _).Succeeded
            ? (int)dpiX
            : MonitorInfo.DefaultDpi;

        return new MonitorInfo(
            info.szDevice.ToString(),
            ToPixelRect(info.monitorInfo.rcMonitor),
            ToPixelRect(info.monitorInfo.rcWork),
            dpi,
            (info.monitorInfo.dwFlags & PrimaryMonitorFlag) != 0);
    }

    private static PixelRect ToPixelRect(RECT rect) => new(rect.left, rect.top, rect.right, rect.bottom);
}
