using Windows.Win32;
using Windows.Win32.Foundation;
using Windows.Win32.Graphics.Gdi;
using Windows.Win32.System.Power;
using Windows.Win32.System.Threading;
using Windows.Win32.UI.Accessibility;
using Windows.Win32.UI.Shell;
using Windows.Win32.UI.WindowsAndMessaging;

namespace Sillfall.Platform.Shell;

/// <summary>
/// The system facts the runtime policy needs: power source, the "show animations" setting and the executable
/// name of a window's process. Process names are only used locally, for the user's app rules (PLAN.md, section 0:
/// privacy). Window titles are never read.
/// </summary>
public static class SystemInfo
{
    /// <summary>Running on battery: the AC line is reported as offline.</summary>
    public static bool IsOnBattery() => PInvoke.GetSystemPowerStatus(out SYSTEM_POWER_STATUS status) && status.ACLineStatus == 0;

    /// <summary>Windows' "show animations in Windows" setting. True when it cannot be read.</summary>
    public static unsafe bool AnimationsEnabled()
    {
        BOOL enabled = true;
        return !PInvoke.SystemParametersInfo(SYSTEM_PARAMETERS_INFO_ACTION.SPI_GETCLIENTAREAANIMATION, 0, &enabled, 0) || enabled;
    }

    /// <summary>
    /// Classes of the shell's desktop and taskbar. With one of them in front nothing runs full screen, although the
    /// desktop window covers the monitor (finding F-18).
    /// </summary>
    private static readonly string[] s_desktopClasses = ["Progman", "WorkerW", "Shell_TrayWnd", "Shell_SecondaryTrayWnd"];

    /// <summary>
    /// What the shell says about the screen (PLAN.md, section 3.4) and the geometry of the foreground window, for
    /// <see cref="Core.Settings.ScreenUseRules"/>. Only the class name and the rectangles are read, never the title.
    /// </summary>
    public static unsafe Core.Settings.ScreenUseSample ScreenUse()
    {
        QUERY_USER_NOTIFICATION_STATE state;
        if (PInvoke.SHQueryUserNotificationState(&state).Failed)
        {
            return default;
        }

        HWND foreground = PInvoke.GetForegroundWindow();
        if (foreground.IsNull)
        {
            return new Core.Settings.ScreenUseSample((Core.Settings.ShellNotificationState)state, default, default);
        }

        var monitorInfo = new MONITORINFO { cbSize = (uint)sizeof(MONITORINFO) };
        Core.Geometry.PixelRect monitor = PInvoke.GetMonitorInfo(PInvoke.MonitorFromWindow(foreground, MONITOR_FROM_FLAGS.MONITOR_DEFAULTTONEAREST), &monitorInfo)
            ? new Core.Geometry.PixelRect(monitorInfo.rcMonitor.left, monitorInfo.rcMonitor.top, monitorInfo.rcMonitor.right, monitorInfo.rcMonitor.bottom)
            : default;

        var style = (WINDOW_STYLE)PInvoke.GetWindowLong(foreground, WINDOW_LONG_PTR_INDEX.GWL_STYLE);
        return new Core.Settings.ScreenUseSample(
            (Core.Settings.ShellNotificationState)state,
            Desktop.WindowGeometry.GetVisibleBounds(foreground),
            monitor,
            IsOwnProcess: ProcessIdOf(foreground) == PInvoke.GetCurrentProcessId(),
            IsDesktop: IsDesktopWindow(foreground),
            IsMaximized: PInvoke.IsZoomed(foreground),
            HasCaption: (style & WINDOW_STYLE.WS_CAPTION) == WINDOW_STYLE.WS_CAPTION);
    }

    /// <summary>Class of the content window inside the clipboard history and emoji panel (and other modern apps).</summary>
    public const string CoreWindowClass = "Windows.UI.Core.CoreWindow";

    /// <summary>Class of the top-level frame around a modern app's or panel's <see cref="CoreWindowClass"/> window.</summary>
    public const string FrameWindowClass = "ApplicationFrameWindow";

    /// <summary>
    /// Whether the clipboard history (Win+V) or the emoji panel (Win+.) is open (finding F-17). Both are shown in an
    /// <see cref="FrameWindowClass"/> window whose child is a <see cref="CoreWindowClass"/> of <c>TextInputHost.exe</c>;
    /// the frame stays visible but cloaked while the panel is closed, and neither takes the foreground. Only class and
    /// process names are read.
    /// </summary>
    public static unsafe bool IsTextInputPanelOpen()
    {
        HWND frame = HWND.Null;
        fixed (char* frameClass = FrameWindowClass)
        fixed (char* coreClass = CoreWindowClass)
        {
            while (true)
            {
                frame = PInvoke.FindWindowEx(HWND.Null, frame, frameClass, (char*)null);
                if (frame.IsNull)
                {
                    return false;
                }

                if (!PInvoke.IsWindowVisible(frame) || Desktop.WindowGeometry.IsCloaked(frame))
                {
                    continue;
                }

                HWND content = PInvoke.FindWindowEx(frame, HWND.Null, coreClass, (char*)null);
                if (!content.IsNull && string.Equals(ProcessNameOf(content), "TextInputHost.exe", StringComparison.OrdinalIgnoreCase))
                {
                    return true;
                }
            }
        }
    }

    private static unsafe bool IsDesktopWindow(HWND window)
    {
        const int Capacity = 64;
        char* buffer = stackalloc char[Capacity];
        int length = PInvoke.GetClassName(window, buffer, Capacity);
        var className = new ReadOnlySpan<char>(buffer, Math.Max(length, 0));
        foreach (string desktop in s_desktopClasses)
        {
            if (className.Equals(desktop, StringComparison.Ordinal))
            {
                return true;
            }
        }

        return false;
    }

    /// <summary>GDI and USER objects of this process, for the soak test's leak check.</summary>
    public static (int Gdi, int User) GuiResources()
    {
        HANDLE process = PInvoke.GetCurrentProcess();
        return ((int)PInvoke.GetGuiResources(process, GET_GUI_RESOURCES_FLAGS.GR_GDIOBJECTS), (int)PInvoke.GetGuiResources(process, GET_GUI_RESOURCES_FLAGS.GR_USEROBJECTS));
    }

    /// <summary>Whether this session is shown through Remote Desktop.</summary>
    public static bool IsRemoteSession() => PInvoke.GetSystemMetrics(SYSTEM_METRICS_INDEX.SM_REMOTESESSION) != 0;

    /// <summary>Whether Windows' high contrast mode is on.</summary>
    public static unsafe bool HighContrastOn()
    {
        var contrast = new HIGHCONTRASTW { cbSize = (uint)sizeof(HIGHCONTRASTW) };
        return PInvoke.SystemParametersInfo(SYSTEM_PARAMETERS_INFO_ACTION.SPI_GETHIGHCONTRAST, contrast.cbSize, &contrast, 0)
            && contrast.dwFlags.HasFlag(HIGHCONTRASTW_FLAGS.HCF_HIGHCONTRASTON);
    }

    /// <summary>The executable name of the process that owns <paramref name="window"/> ("notepad.exe"), or <see langword="null"/>.</summary>
    public static string? ProcessNameOf(nint window)
    {
        uint processId = ProcessIdOf(window);
        return processId == 0 ? null : ProcessName(processId);
    }

    /// <summary>The id of the process that owns <paramref name="window"/>, or 0.</summary>
    public static unsafe uint ProcessIdOf(nint window)
    {
        uint processId = 0;
        uint thread = PInvoke.GetWindowThreadProcessId((HWND)window, &processId);
        return thread == 0 ? 0 : processId;
    }

    /// <summary>The executable name of a process, or <see langword="null"/> when it cannot be queried (elevated, gone).</summary>
    public static unsafe string? ProcessName(uint processId)
    {
        HANDLE process = PInvoke.OpenProcess(PROCESS_ACCESS_RIGHTS.PROCESS_QUERY_LIMITED_INFORMATION, false, processId);
        if (process.IsNull)
        {
            return null;
        }

        try
        {
            const int Capacity = 1024;
            char* buffer = stackalloc char[Capacity];
            uint length = Capacity;
            return PInvoke.QueryFullProcessImageName(process, PROCESS_NAME_FORMAT.PROCESS_NAME_WIN32, buffer, &length)
                ? Path.GetFileName(new string(buffer, 0, (int)length))
                : null;
        }
        finally
        {
            PInvoke.CloseHandle(process);
        }
    }
}
