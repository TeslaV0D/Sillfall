using System.Text.Json;

namespace Weatherly.Core.Settings;

/// <summary>Loads and saves <see cref="UserSettings"/> as a JSON file.</summary>
public sealed class SettingsStore
{
    public SettingsStore(string path)
    {
        ArgumentException.ThrowIfNullOrEmpty(path);
        Path = path;
    }

    /// <summary>Settings file of the current user: %LOCALAPPDATA%\&lt;app name&gt;\settings.json on Windows.</summary>
    public static string DefaultPath => System.IO.Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), AppInfo.Name, "settings.json");

    public string Path { get; }

    /// <summary>Why the last <see cref="Load"/> fell back to defaults, or <see langword="null"/>.</summary>
    public string? LastError { get; private set; }

    /// <summary>The stored settings. A missing, unreadable or damaged file gives the defaults.</summary>
    public UserSettings Load()
    {
        LastError = null;
        if (!File.Exists(Path))
        {
            return new UserSettings();
        }

        try
        {
            UserSettings? settings = JsonSerializer.Deserialize(File.ReadAllText(Path), UserSettingsJsonContext.Default.UserSettings);
            return (settings ?? new UserSettings()).Sanitized();
        }
        catch (Exception exception) when (exception is JsonException or IOException or UnauthorizedAccessException)
        {
            LastError = $"{exception.GetType().Name}: {exception.Message}";
            return new UserSettings();
        }
    }

    /// <summary>Writes the settings. Written to a temporary file first and then swapped in, so a crash never leaves half a file.</summary>
    public void Save(UserSettings settings)
    {
        ArgumentNullException.ThrowIfNull(settings);
        string? directory = System.IO.Path.GetDirectoryName(Path);
        if (!string.IsNullOrEmpty(directory))
        {
            Directory.CreateDirectory(directory);
        }

        string temporary = Path + ".tmp";
        File.WriteAllText(temporary, JsonSerializer.Serialize(settings.Sanitized(), UserSettingsJsonContext.Default.UserSettings));
        File.Move(temporary, Path, overwrite: true);
    }
}
