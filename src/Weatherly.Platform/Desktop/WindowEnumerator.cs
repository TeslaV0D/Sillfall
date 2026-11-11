using Weatherly.Core.Desktop;
using Weatherly.Core.Geometry;
using Windows.Win32;
using Windows.Win32.Foundation;
using Windows.Win32.UI.WindowsAndMessaging;

namespace Weatherly.Platform.Desktop;

/// <summary>
/// Lists the application windows the effects interact with, in z-order (PLAN.md, section 3.2).
/// <para>
/// Only geometry is read: position, size, z-order, maximized and topmost state. The class name is
/// read to skip the shell's own windows. Titles and contents are never read.
/// </para>
/// </summary>
internal sealed class WindowEnumerator
{
    /// <summary>Smaller windows are tooltips, notification toasts or similar and get no surfaces.</summary>
    public const int MinimumWidth = 100;

    /// <summary>See <see cref="MinimumWidth"/>.</summary>
    public const int MinimumHeight = 60;

    private const int MaxClassNameLength = 256;

    /// <summary>
    /// Windows of the shell that cover the desktop or are handled separately: the desktop itself
    /// (Progman, WorkerW) and the taskbars (their top edge is the floor, taken from the work area).
    /// </summary>
    private static readonly string[] s_ignoredClasses = ["Progman", "WorkerW", "Shell_TrayWnd", "Shell_SecondaryTrayWnd"];

    private readonly string _ownOverlayClassPrefix;
    private readonly WNDENUMPROC _callback;
    private WindowSnapshot? _target;

    /// <summary>
    /// Decides per process id whether the user's app rules hide the weather over its windows. <see langword="null"/>: none do.
    /// </summary>
    public Func<uint, bool>? IsWeatherHidden { get; set; }

    /// <param name="ownOverlayClassPrefix">Class name prefix of our own overlay windows, which are never tracked.</param>
    public WindowEnumerator(string ownOverlayClassPrefix)
    {
        _ownOverlayClassPrefix = ownOverlayClassPrefix;
        _callback = Visit;
    }

    /// <summary>Refills <paramref name="output"/> with the current windows, topmost first.</summary>
    public void Capture(WindowSnapshot output, double timestamp)
    {
        output.Clear();
        output.Timestamp = timestamp;
        _target = output;
        try
        {
            // EnumWindows walks the top-level windows from the top of the z-order to the bottom.
            PInvoke.EnumWindows(_callback, 0);
        }
        finally
        {
            _target = null;
        }
    }

    private BOOL Visit(HWND window, LPARAM parameter)
    {
        if (IsTracked(window, out TrackedWindow tracked))
        {
            _target!.Add(tracked);
        }

        return true;
    }

    private unsafe bool IsTracked(HWND window, out TrackedWindow tracked)
    {
        tracked = default;

        if (!PInvoke.IsWindowVisible(window) || PInvoke.IsIconic(window) || WindowGeometry.IsCloaked(window))
        {
            return false;
        }

        var exStyle = (WINDOW_EX_STYLE)PInvoke.GetWindowLong(window, WINDOW_LONG_PTR_INDEX.GWL_EXSTYLE);

        // Tool windows are palettes and floating toolbars. Click-through layered windows are overlays like ours.
        const WINDOW_EX_STYLE ClickThrough = WINDOW_EX_STYLE.WS_EX_LAYERED | WINDOW_EX_STYLE.WS_EX_TRANSPARENT;
        if ((exStyle & WINDOW_EX_STYLE.WS_EX_TOOLWINDOW) != 0 || (exStyle & ClickThrough) == ClickThrough)
        {
            return false;
        }

        char* buffer = stackalloc char[MaxClassNameLength];
        int length = PInvoke.GetClassName(window, buffer, MaxClassNameLength);
        var className = new ReadOnlySpan<char>(buffer, Math.Max(length, 0));
        if (IsIgnoredClass(className))
        {
            return false;
        }

        PixelRect bounds = WindowGeometry.GetVisibleBounds(window);
        if (bounds.Width < MinimumWidth || bounds.Height < MinimumHeight)
        {
            return false;
        }

        WindowTraits traits = WindowTraits.None;
        if (PInvoke.IsZoomed(window))
        {
            traits |= WindowTraits.Maximized;
        }

        if ((exStyle & WINDOW_EX_STYLE.WS_EX_TOPMOST) != 0)
        {
            traits |= WindowTraits.Topmost;
        }

        if (IsWeatherHidden is { } isHidden && isHidden(Shell.SystemInfo.ProcessIdOf((nint)window.Value)))
        {
            traits |= WindowTraits.WeatherHidden;
        }

        tracked = new TrackedWindow((nint)window.Value, bounds, traits);
        return true;
    }

    private bool IsIgnoredClass(ReadOnlySpan<char> className)
    {
        if (className.StartsWith(_ownOverlayClassPrefix, StringComparison.Ordinal))
        {
            return true;
        }

        foreach (string ignored in s_ignoredClasses)
        {
            if (className.Equals(ignored, StringComparison.Ordinal))
            {
                return true;
            }
        }

        return false;
    }
}
