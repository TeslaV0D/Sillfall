using Windows.Win32;
using Windows.Win32.UI.HiDpi;

namespace Sillfall.Platform;

/// <summary>
/// DPI awareness diagnostics. The overlay works in physical pixels throughout, which only holds
/// when the process runs as Per-Monitor-V2 aware (declared in the app manifest).
/// </summary>
public static class DpiAwareness
{
    /// <summary>Returns <see langword="true"/> when the calling thread is Per-Monitor-V2 DPI aware.</summary>
    public static bool IsPerMonitorV2()
    {
        DPI_AWARENESS_CONTEXT current = PInvoke.GetThreadDpiAwarenessContext();
        return PInvoke.AreDpiAwarenessContextsEqual(
            current,
            DPI_AWARENESS_CONTEXT.DPI_AWARENESS_CONTEXT_PER_MONITOR_AWARE_V2);
    }
}
