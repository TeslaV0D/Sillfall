using Weatherly.Core.Displays;
using Weatherly.Core.Geometry;
using Weatherly.Core.Overlay;
using Windows.Win32;
using Windows.Win32.Foundation;
using Windows.Win32.UI.WindowsAndMessaging;

namespace Weatherly.Platform.Overlay;

/// <summary>
/// One transparent, click-through, always-on-top window that covers a monitor, together with its
/// drawing surface. Lives on the overlay thread.
/// </summary>
internal sealed class OverlayWindow : IDisposable
{
    /// <summary>
    /// The styles from PLAN.md, section 3.1. Layered + transparent lets mouse input fall through,
    /// tool window keeps it out of Alt+Tab and the taskbar, no-activate keeps the focus where it is,
    /// and no-redirection-bitmap leaves all drawing to DirectComposition.
    /// </summary>
    private const WINDOW_EX_STYLE OverlayExStyle =
        WINDOW_EX_STYLE.WS_EX_LAYERED
        | WINDOW_EX_STYLE.WS_EX_TRANSPARENT
        | WINDOW_EX_STYLE.WS_EX_TOPMOST
        | WINDOW_EX_STYLE.WS_EX_TOOLWINDOW
        | WINDOW_EX_STYLE.WS_EX_NOACTIVATE
        | WINDOW_EX_STYLE.WS_EX_NOREDIRECTIONBITMAP;

    private readonly HWND _window;
    private IOverlaySurface? _surface;

    private OverlayWindow(HWND window, MonitorInfo monitor)
    {
        _window = window;
        Monitor = monitor;
    }

    public MonitorInfo Monitor { get; set; }

    public nint Handle => _window;

    public long PresentedFrames { get; private set; }

    /// <summary>Creates the window for <paramref name="monitor"/>. It stays hidden until <see cref="Show"/>.</summary>
    public static unsafe OverlayWindow Create(string windowClass, HINSTANCE module, MonitorInfo monitor)
    {
        PixelRect bounds = monitor.Bounds;
        HWND window;
        fixed (char* className = windowClass)
        {
            window = PInvoke.CreateWindowEx(
                OverlayExStyle,
                className,
                null,
                WINDOW_STYLE.WS_POPUP,
                bounds.Left,
                bounds.Top,
                bounds.Width,
                bounds.Height,
                HWND.Null,
                HMENU.Null,
                module,
                null);
        }

        if (window.IsNull)
        {
            throw new Win32ErrorException("CreateWindowEx (overlay)");
        }

        // A layered window draws nothing until its attributes are set. Fully opaque here means:
        // "use the per-pixel alpha of the composition content as it is".
        if (!PInvoke.SetLayeredWindowAttributes(window, new COLORREF(0), 255, LAYERED_WINDOW_ATTRIBUTES_FLAGS.LWA_ALPHA))
        {
            var error = new Win32ErrorException("SetLayeredWindowAttributes");
            PInvoke.DestroyWindow(window);
            throw error;
        }

        return new OverlayWindow(window, monitor);
    }

    public void CreateSurface(IOverlayRenderer renderer)
    {
        _surface?.Dispose();
        _surface = renderer.CreateSurface(_window, Monitor.Bounds.Size);
    }

    public void DisposeSurface()
    {
        _surface?.Dispose();
        _surface = null;
    }

    public PresentResult Present(OverlayContent content)
    {
        if (_surface is null)
        {
            return PresentResult.DeviceLost;
        }

        PresentResult result = _surface.Present(new OverlayFrame(content, Monitor.ScaleFactor));
        if (result == PresentResult.Presented)
        {
            PresentedFrames++;
        }

        return result;
    }

    /// <summary>Shows the window above all others without activating it.</summary>
    public void Show()
    {
        PixelRect bounds = Monitor.Bounds;
        PInvoke.SetWindowPos(
            _window,
            HWND.HWND_TOPMOST,
            bounds.Left,
            bounds.Top,
            bounds.Width,
            bounds.Height,
            SET_WINDOW_POS_FLAGS.SWP_NOACTIVATE | SET_WINDOW_POS_FLAGS.SWP_SHOWWINDOW);
    }

    /// <summary>
    /// Moves the window back to the top of the topmost band. Windows that are activated later,
    /// or other topmost windows like the taskbar, can otherwise end up above the overlay.
    /// </summary>
    public void BringToTop()
    {
        PInvoke.SetWindowPos(
            _window,
            HWND.HWND_TOPMOST,
            0,
            0,
            0,
            0,
            SET_WINDOW_POS_FLAGS.SWP_NOMOVE | SET_WINDOW_POS_FLAGS.SWP_NOSIZE | SET_WINDOW_POS_FLAGS.SWP_NOACTIVATE | SET_WINDOW_POS_FLAGS.SWP_NOOWNERZORDER);
    }

    /// <summary>Includes the window in or excludes it from screenshots and screen recordings.</summary>
    public void SetExcludedFromCapture(bool excluded)
    {
        WINDOW_DISPLAY_AFFINITY affinity = excluded
            ? WINDOW_DISPLAY_AFFINITY.WDA_EXCLUDEFROMCAPTURE
            : WINDOW_DISPLAY_AFFINITY.WDA_NONE;

        if (!PInvoke.SetWindowDisplayAffinity(_window, affinity))
        {
            throw new Win32ErrorException("SetWindowDisplayAffinity");
        }
    }

    /// <summary>Reads the state of the window back from Windows.</summary>
    public OverlayWindowStatus Inspect()
    {
        var exStyle = (WINDOW_EX_STYLE)PInvoke.GetWindowLong(_window, WINDOW_LONG_PTR_INDEX.GWL_EXSTYLE);

        PixelRect windowBounds = PInvoke.GetWindowRect(_window, out RECT rect)
            ? new PixelRect(rect.left, rect.top, rect.right, rect.bottom)
            : default;

        // The same hit test the mouse uses. A click-through window is skipped by it.
        PixelRect bounds = Monitor.Bounds;
        var center = new System.Drawing.Point(bounds.Left + (bounds.Width / 2), bounds.Top + (bounds.Height / 2));
        bool clickThrough = PInvoke.WindowFromPoint(center) != _window;

        bool excluded = PInvoke.GetWindowDisplayAffinity(_window, out uint affinity)
            && affinity == (uint)WINDOW_DISPLAY_AFFINITY.WDA_EXCLUDEFROMCAPTURE;

        return new OverlayWindowStatus(
            Monitor,
            Handle,
            windowBounds,
            (exStyle & OverlayExStyle) == OverlayExStyle,
            clickThrough,
            excluded,
            PresentedFrames,
            ZOrderProbe.FindWindowsAbove(_window, bounds));
    }

    public void Dispose()
    {
        DisposeSurface();
        PInvoke.DestroyWindow(_window);
    }
}
