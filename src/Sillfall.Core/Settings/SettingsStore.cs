using System.Text.Json;

namespace Sillfall.Core.Settings;

/// <summary>
/// Loads and saves <see cref="AppSettings"/> as a JSON file. Older versions are migrated on load; the file is
/// written in the current version the next time something changes.
/// </summary>
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

    /// <summary>Version of the file the last <see cref="Load"/> read (0 when there was none).</summary>
    public int LoadedVersion { get; private set; }

    /// <summary>The stored settings. A missing, unreadable or damaged file gives the defaults.</summary>
    public AppSettings Load()
    {
        LastError = null;
        LoadedVersion = 0;
        if (!File.Exists(Path))
        {
            return new AppSettings();
        }

        try
        {
            string json = File.ReadAllText(Path);
            LoadedVersion = ReadVersion(json);
            if (LoadedVersion <= 1)
            {
                SettingsV1? old = JsonSerializer.Deserialize(json, SettingsV1JsonContext.Default.SettingsV1);
                return (old ?? new SettingsV1()).Migrate();
            }

            if (LoadedVersion > AppSettings.CurrentVersion)
            {
                // A newer app wrote this. Read what we know; the rest is kept out of harm's way by not saving
                // over it until the user changes something.
                LastError = $"The settings were written by a newer version (file version {LoadedVersion}).";
            }

            AppSettings? settings = JsonSerializer.Deserialize(json, AppSettingsJsonContext.Default.AppSettings);
            return (settings ?? new AppSettings()).Sanitized();
        }
        catch (Exception exception) when (exception is JsonException or IOException or UnauthorizedAccessException or InvalidOperationException)
        {
            LastError = $"{exception.GetType().Name}: {exception.Message}";
            return new AppSettings();
        }
    }

    /// <summary>Writes the settings. Written to a temporary file first and then swapped in, so a crash never leaves half a file.</summary>
    public void Save(AppSettings settings)
    {
        ArgumentNullException.ThrowIfNull(settings);
        string? directory = System.IO.Path.GetDirectoryName(Path);
        if (!string.IsNullOrEmpty(directory))
        {
            Directory.CreateDirectory(directory);
        }

        string temporary = Path + ".tmp";
        File.WriteAllText(temporary, JsonSerializer.Serialize(settings.Sanitized(), AppSettingsJsonContext.Default.AppSettings));
        File.Move(temporary, Path, overwrite: true);
    }

    /// <summary>The "version" field, 1 when it is missing (the MVP always wrote it, but hand-made files may not).</summary>
    private static int ReadVersion(string json)
    {
        using JsonDocument document = JsonDocument.Parse(json, new JsonDocumentOptions { CommentHandling = JsonCommentHandling.Skip, AllowTrailingCommas = true });
        return document.RootElement.ValueKind == JsonValueKind.Object
            && document.RootElement.TryGetProperty("version", out JsonElement version)
            && version.TryGetInt32(out int value)
            ? value
            : 1;
    }
}
