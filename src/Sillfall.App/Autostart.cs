using Sillfall.Core;
using Sillfall.Platform.Shell;

namespace Sillfall.App;

/// <summary>
/// Starts the app at sign-in (PLAN.md, section 7, page "Advanced"): the package's StartupTask when installed from
/// MSIX, the Run key of the current user otherwise. Failures are reported, never thrown: autostart is a nicety.
/// </summary>
internal static class Autostart
{
    private const string TaskId = AppInfo.StartupTaskId;

    /// <summary>Why the last change failed, or <see langword="null"/>.</summary>
    public static string? LastError { get; private set; }

    public static async Task<bool> SetAsync(bool enabled)
    {
        LastError = null;
        try
        {
            if (Integration.IsPackaged())
            {
                Windows.ApplicationModel.StartupTask task = await Windows.ApplicationModel.StartupTask.GetAsync(TaskId);
                if (!enabled)
                {
                    task.Disable();
                    return true;
                }

                Windows.ApplicationModel.StartupTaskState state = await task.RequestEnableAsync();
                if (state is Windows.ApplicationModel.StartupTaskState.Enabled or Windows.ApplicationModel.StartupTaskState.EnabledByPolicy)
                {
                    return true;
                }

                LastError = state == Windows.ApplicationModel.StartupTaskState.DisabledByUser
                    ? "Autostart was switched off in Windows (Settings > Apps > Startup). Switch it on there."
                    : $"Windows did not allow autostart ({state}).";
                return false;
            }

            if (Environment.ProcessPath is not { } path)
            {
                LastError = "The app's path is unknown.";
                return false;
            }

            Integration.SetAutostart(AppInfo.Name, path, enabled);
            return true;
        }
#pragma warning disable CA1031 // Autostart failing must not break the settings.
        catch (Exception exception)
#pragma warning restore CA1031
        {
            LastError = $"{exception.GetType().Name}: {exception.Message}";
            return false;
        }
    }
}
