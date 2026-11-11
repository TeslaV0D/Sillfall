using Weatherly.Core.Geometry;
using Weatherly.Core.Overlay;
using Windows.Win32;
using Windows.Win32.Foundation;
using Windows.Win32.Graphics.Dwm;
using Windows.Win32.UI.WindowsAndMessaging;

namespace Weatherly.Platform.Overlay;

/// <summary>
/// Finds the windows that lie above a window in the z-order and overlap a given area.
/// Only the window class and the topmost flag are read: never titles or contents.
/// </summary>
internal static class ZOrderProbe
{
    private const int MaxClassNameLength = 256;

    public static unsafe IReadOnlyList<CoveringWindow> FindWindowsAbove(HWND window, PixelRect area)
    {
        var covering = new List<CoveringWindow>();
        char* className = stackalloc char[MaxClassNameLength];

        for (HWND above = PInvoke.GetWindow(window, GET_WINDOW_CMD.GW_HWNDPREV);
             !above.IsNull;
             above = PInvoke.GetWindow(above, GET_WINDOW_CMD.GW_HWNDPREV))
        {
            if (!PInvoke.IsWindowVisible(above) || IsCloaked(above))
            {
                continue;
            }

            if (!PInvoke.GetWindowRect(above, out RECT rect) || !Intersects(area, rect))
            {
                continue;
            }

            var exStyle = (WINDOW_EX_STYLE)PInvoke.GetWindowLong(above, WINDOW_LONG_PTR_INDEX.GWL_EXSTYLE);

            // Other click-through layered windows (like our own overlays on a neighbouring monitor) do not hide anything.
            const WINDOW_EX_STYLE ClickThrough = WINDOW_EX_STYLE.WS_EX_LAYERED | WINDOW_EX_STYLE.WS_EX_TRANSPARENT;
            if ((exStyle & ClickThrough) == ClickThrough)
            {
                continue;
            }

            int length = PInvoke.GetClassName(above, className, MaxClassNameLength);
            covering.Add(new CoveringWindow(
                length > 0 ? new string(className, 0, length) : "(unknown)",
                (exStyle & WINDOW_EX_STYLE.WS_EX_TOPMOST) != 0));
        }

        return covering;
    }

    /// <summary>Windows on another virtual desktop and suspended store apps are visible but cloaked.</summary>
    private static unsafe bool IsCloaked(HWND window)
    {
        int cloaked = 0;
        return PInvoke.DwmGetWindowAttribute(window, DWMWINDOWATTRIBUTE.DWMWA_CLOAKED, &cloaked, sizeof(int)).Succeeded
            && cloaked != 0;
    }

    private static bool Intersects(PixelRect area, RECT rect) =>
        rect.left < area.Right && rect.right > area.Left && rect.top < area.Bottom && rect.bottom > area.Top;
}
