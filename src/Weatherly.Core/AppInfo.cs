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

    /// <summary>URI scheme for shared scene links (<c>weatherly://scene?d=…</c>).</summary>
    public const string UriScheme = "weatherly";

    /// <summary>Product version, taken from the assembly so it follows <c>Directory.Build.props</c>.</summary>
    public static Version Version { get; } =
        typeof(AppInfo).Assembly.GetName().Version ?? new Version(0, 0, 0);

    /// <summary>Short version for the UI, for example <c>0.1.0</c>.</summary>
    public static string DisplayVersion => Version.ToString(3);
}
