using System.Globalization;
using System.Runtime.InteropServices;
using System.Text;

namespace Sillfall.Core.Diagnostics;

/// <summary>
/// Local error reports (PLAN.md, M13): a text file per crash or error in <c>%LOCALAPPDATA%\…\logs</c>, the newest
/// <see cref="Keep"/> kept. Nothing is sent anywhere. The user name and profile folder are replaced in every report,
/// so a report can be passed on without giving away who wrote it. The same error is written once per session.
/// </summary>
public sealed class CrashLog
{
    /// <summary>Reports kept; older ones are deleted when a new one is written.</summary>
    public const int Keep = 10;

    private readonly Func<DateTimeOffset> _clock;
    private readonly HashSet<string> _written = new(StringComparer.Ordinal);
    private readonly Lock _gate = new();

    public CrashLog(string directory, Func<DateTimeOffset>? clock = null)
    {
        ArgumentException.ThrowIfNullOrEmpty(directory);
        Directory = directory;
        _clock = clock ?? (() => DateTimeOffset.UtcNow);
    }

    public static string DefaultDirectory => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), AppInfo.Name, "logs");

    public string Directory { get; }

    /// <summary>
    /// Writes a report about <paramref name="exception"/>. <paramref name="fatal"/> marks a crash (always written);
    /// other errors are written once per session. Returns the file, or <see langword="null"/> when nothing was written.
    /// Never throws: a broken log must not make a crash worse.
    /// </summary>
    public string? Write(Exception exception, bool fatal, string? context = null)
    {
        ArgumentNullException.ThrowIfNull(exception);
        try
        {
            lock (_gate)
            {
                string signature = $"{exception.GetType().FullName}|{exception.Message}|{exception.StackTrace?.Split('\n')[0].Trim()}";
                if (!fatal && !_written.Add(signature))
                {
                    return null;
                }

                DateTimeOffset now = _clock();
                System.IO.Directory.CreateDirectory(Directory);
                string kind = fatal ? "crash" : "error";
                string path = Path.Combine(Directory, string.Create(CultureInfo.InvariantCulture, $"{kind}-{now:yyyyMMdd-HHmmss-fff}.txt"));
                File.WriteAllText(path, Format(exception, fatal, context, now), Encoding.UTF8);
                Prune();
                return path;
            }
        }
#pragma warning disable CA1031 // The log is the last line of defence; its own failure is swallowed.
        catch (Exception)
#pragma warning restore CA1031
        {
            return null;
        }
    }

    /// <summary>The reports on disk, newest first.</summary>
    public IReadOnlyList<string> Reports() =>
        System.IO.Directory.Exists(Directory)
            ? [.. new DirectoryInfo(Directory).GetFiles("*.txt").OrderByDescending(file => file.Name, StringComparer.Ordinal).Select(file => file.FullName)]
            : [];

    /// <summary>The report's text: app, system, what happened, with personal paths and names replaced.</summary>
    public static string Format(Exception exception, bool fatal, string? context, DateTimeOffset time)
    {
        ArgumentNullException.ThrowIfNull(exception);
        var text = new StringBuilder();
        text.AppendLine(CultureInfo.InvariantCulture, $"{AppInfo.Name} {AppInfo.DisplayVersion} {(fatal ? "crash" : "error")} report");
        text.AppendLine(CultureInfo.InvariantCulture, $"Time (UTC): {time.UtcDateTime:yyyy-MM-dd HH:mm:ss}");
        text.AppendLine(CultureInfo.InvariantCulture, $"System: {RuntimeInformation.OSDescription}, {RuntimeInformation.OSArchitecture}, .NET {Environment.Version}");
        text.AppendLine(CultureInfo.InvariantCulture, $"Process: {RuntimeInformation.ProcessArchitecture}, up {TimeSpan.FromMilliseconds(Environment.TickCount64):d\\.hh\\:mm\\:ss} since boot");
        if (!string.IsNullOrWhiteSpace(context))
        {
            text.AppendLine(CultureInfo.InvariantCulture, $"Where: {context}");
        }

        text.AppendLine("This report stays on this PC. Nothing was sent.");
        text.AppendLine();
        text.AppendLine(exception.ToString());
        return Redact(text.ToString());
    }

    /// <summary>Replaces the user's profile folder and name, the parts of a stack trace or message that say who it is.</summary>
    public static string Redact(string text, string? profile = null, string? userName = null)
    {
        ArgumentNullException.ThrowIfNull(text);
        profile ??= Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        userName ??= Environment.UserName;
        if (!string.IsNullOrEmpty(profile))
        {
            text = text.Replace(profile, "%USERPROFILE%", StringComparison.OrdinalIgnoreCase);
        }

        if (userName is { Length: >= 3 })
        {
            text = text.Replace(userName, "<user>", StringComparison.OrdinalIgnoreCase);
        }

        return text;
    }

    private void Prune()
    {
        IReadOnlyList<string> reports = Reports();
        for (int i = Keep; i < reports.Count; i++)
        {
            File.Delete(reports[i]);
        }
    }
}
