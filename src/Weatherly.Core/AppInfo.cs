namespace Weatherly.Core;

/// <summary>
/// Single source of truth for the product identity. "Weatherly" is a working title:
/// everything user-visible must read the name from here so it can be swapped before release.
/// </summary>
public static class AppInfo
{
    /// <summary>User-visible product name.</summary>
    public const string Name = "Weatherly";

    /// <summary>Folder name below <c>%LOCALAPPDATA%</c> for settings, collection and logs.</summary>
    public const string DataFolderName = Name;

    /// <summary>
    /// URI scheme for shared scene links (<c>weatherly://scene?d=…</c>). Stable across a rename of <see cref="Name"/>,
    /// so links that are already shared keep working.
    /// </summary>
    public const string UriScheme = "weatherly";

    /// <summary>
    /// Id of the MSIX startup task. Stable across a rename of <see cref="Name"/>: Windows treats a new id as a
    /// different task and would forget the user's choice.
    /// </summary>
    public const string StartupTaskId = "WeatherlyStartup";

    /// <summary>Public repository. The store listing and the winget manifest link here.</summary>
    public const string RepositoryUrl = "https://github.com/TeslaV0D/weatherly_windows";

    /// <summary>Bug reports, and the support address on the store listing.</summary>
    public const string SupportUrl = "https://github.com/TeslaV0D/weatherly_windows/issues";

    /// <summary>
    /// Privacy policy published with the repository (English). The store listing points here. The repository is
    /// public, so the page is reachable without an account.
    /// </summary>
    public const string PrivacyPolicyUrl = "https://github.com/TeslaV0D/weatherly_windows/blob/main/docs/privacy.md";

    /// <summary>German text of <see cref="PrivacyPolicyUrl"/>.</summary>
    public const string PrivacyPolicyUrlGerman = "https://github.com/TeslaV0D/weatherly_windows/blob/main/docs/privacy.de.md";

    /// <summary>Product version, taken from the assembly so it follows <c>Directory.Build.props</c>.</summary>
    public static Version Version { get; } =
        typeof(AppInfo).Assembly.GetName().Version ?? new Version(0, 0, 0);

    /// <summary>Short version for the UI, for example <c>0.1.0</c>.</summary>
    public static string DisplayVersion => Version.ToString(3);
}
