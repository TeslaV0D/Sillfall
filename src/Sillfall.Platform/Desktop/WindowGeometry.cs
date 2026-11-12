using Sillfall.Core.Geometry;
using Windows.Win32;
using Windows.Win32.Foundation;
using Windows.Win32.Graphics.Dwm;

namespace Sillfall.Platform.Desktop;

/// <summary>Reads where a window really is on screen.</summary>
public static class WindowGeometry
{
    /// <summary>
    /// The visible frame of a window in physical pixels. Windows 10 and 11 surround most windows with
    /// invisible resize borders of several pixels, which <c>GetWindowRect</c> includes and DWM does not.
    /// The calling thread must be Per-Monitor-V2 DPI aware.
    /// </summary>
    public static PixelRect GetVisibleBounds(nint window) => GetVisibleBounds((HWND)window);

    internal static unsafe PixelRect GetVisibleBounds(HWND window)
    {
        RECT rect;
        if (PInvoke.DwmGetWindowAttribute(window, DWMWINDOWATTRIBUTE.DWMWA_EXTENDED_FRAME_BOUNDS, &rect, (uint)sizeof(RECT)).Succeeded)
        {
            return new PixelRect(rect.left, rect.top, rect.right, rect.bottom);
        }

        return PInvoke.GetWindowRect(window, out rect)
            ? new PixelRect(rect.left, rect.top, rect.right, rect.bottom)
            : default;
    }

    /// <summary>Windows on another virtual desktop and suspended store apps are visible but cloaked.</summary>
    internal static unsafe bool IsCloaked(HWND window)
    {
        int cloaked = 0;
        return PInvoke.DwmGetWindowAttribute(window, DWMWINDOWATTRIBUTE.DWMWA_CLOAKED, &cloaked, sizeof(int)).Succeeded
            && cloaked != 0;
    }
}
