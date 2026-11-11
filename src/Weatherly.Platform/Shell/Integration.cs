using Microsoft.Win32;
using Windows.Win32;
using Windows.Win32.Foundation;

namespace Weatherly.Platform.Shell;

/// <summary>
/// How the app hooks into Windows when it runs without a package (unpackaged builds, development): autostart via
/// the Run key and the <c>weatherly://</c> protocol in the user's classes. A packaged (MSIX) app declares both in
/// its manifest instead and must not touch the registry for them.
/// </summary>
public static class Integration
{
    private const string RunKey = @"Software\Microsoft\Windows\CurrentVersion\Run";
    private const string ProtocolKey = @"Software\Classes\weatherly";

    /// <summary>Command-line switch of an autostart: start in the notification area, without a window.</summary>
    public const string BackgroundSwitch = "--background";

    /// <summary>Whether this process runs with a package identity (installed from MSIX).</summary>
    public static unsafe bool IsPackaged()
    {
        uint length = 0;
        WIN32_ERROR result = PInvoke.GetCurrentPackageFullName(&length, null);
        return result != WIN32_ERROR.APPMODEL_ERROR_NO_PACKAGE;
    }

    /// <summary>Whether the Run key starts this app at sign-in (unpackaged only).</summary>
    public static bool IsAutostartRegistered(string valueName)
    {
        using RegistryKey? key = Registry.CurrentUser.OpenSubKey(RunKey);
        return key?.GetValue(valueName) is string;
    }

    /// <summary>Adds or removes the app from the Run key (unpackaged only). Starts it in the background.</summary>
    public static void SetAutostart(string valueName, string executablePath, bool enabled)
    {
        using RegistryKey key = Registry.CurrentUser.CreateSubKey(RunKey);
        if (enabled)
        {
            key.SetValue(valueName, $"\"{executablePath}\" {BackgroundSwitch}");
        }
        else
        {
            key.DeleteValue(valueName, throwOnMissingValue: false);
        }
    }

    /// <summary>
    /// Registers <c>weatherly://</c> for the current user so scene links open this executable (unpackaged only).
    /// Rewritten on every start, so a moved build keeps working.
    /// </summary>
    public static void RegisterProtocol(string displayName, string executablePath)
    {
        using RegistryKey key = Registry.CurrentUser.CreateSubKey(ProtocolKey);
        key.SetValue(string.Empty, $"URL:{displayName} scene");
        key.SetValue("URL Protocol", string.Empty);
        using RegistryKey command = key.CreateSubKey(@"shell\open\command");
        command.SetValue(string.Empty, $"\"{executablePath}\" \"%1\"");
    }
}
