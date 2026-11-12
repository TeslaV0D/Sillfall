using Windows.Win32;
using Windows.Win32.Foundation;
using Windows.Win32.System.Power;
using Windows.Win32.System.Threading;
using Windows.Win32.UI.Accessibility;
using Windows.Win32.UI.WindowsAndMessaging;

namespace Weatherly.Platform.Shell;

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
