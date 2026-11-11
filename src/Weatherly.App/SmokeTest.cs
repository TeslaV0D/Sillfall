using System.Globalization;
using System.Text;
using Weatherly.Core.Diagnostics;

namespace Weatherly.App;

/// <summary>
/// Headless self-check for CI. <c>Weatherly.App.exe --smoke-test=&lt;report file&gt;</c> starts the app with
/// the overlay test pattern, writes all diagnostics to the report file and exits with code 0 when
/// every check passed. This is how changes get verified on Windows runners without anyone sitting
/// in front of a desktop.
/// <list type="bullet">
/// <item><c>--smoke-hold=&lt;ms&gt;</c> keeps the app on screen after the report is written, so a script can take a screenshot.</item>
/// <item><c>--exclude-from-capture</c> starts with the overlay hidden from screenshots.</item>
/// </list>
/// </summary>
internal sealed class SmokeTest
{
    private const string ReportSwitch = "--smoke-test=";
    private const string HoldSwitch = "--smoke-hold=";
    private const string ExcludeSwitch = "--exclude-from-capture";

    private readonly string _reportPath;

    private SmokeTest(string reportPath, TimeSpan hold, bool excludeFromCapture)
    {
        _reportPath = reportPath;
        Hold = hold;
        ExcludeFromCapture = excludeFromCapture;
    }

    /// <summary>How long the app stays on screen after the report was written.</summary>
    public TimeSpan Hold { get; }

    public bool ExcludeFromCapture { get; }

    /// <summary>Returns the smoke test when the switch is present, otherwise <see langword="null"/>.</summary>
    public static SmokeTest? FromCommandLine(IReadOnlyList<string> args)
    {
        string? report = ValueOf(args, ReportSwitch);
        if (report is null)
        {
            return null;
        }

        TimeSpan hold = int.TryParse(ValueOf(args, HoldSwitch), NumberStyles.None, CultureInfo.InvariantCulture, out int milliseconds)
            ? TimeSpan.FromMilliseconds(milliseconds)
            : TimeSpan.Zero;

        return new SmokeTest(
            Path.GetFullPath(report),
            hold,
            args.Contains(ExcludeSwitch, StringComparer.OrdinalIgnoreCase));
    }

    /// <summary>Writes the report. Returns the exit code: 0 when every check passed, otherwise 1.</summary>
    public int WriteReport(IEnumerable<DiagnosticResult> diagnostics, IEnumerable<string> information)
    {
        var report = new StringBuilder();
        bool passed = true;
        foreach (DiagnosticResult result in diagnostics)
        {
            report.AppendLine(result.ToString());
            passed &= result.Passed;
        }

        foreach (string line in information)
        {
            report.Append("INFO\t").AppendLine(line);
        }

        File.WriteAllText(_reportPath, report.ToString());
        return passed ? 0 : 1;
    }

    /// <summary>Records a crash and ends the process.</summary>
    public void Fail(Exception exception)
    {
        File.WriteAllText(_reportPath, $"FAILED\tUnhandled exception\t{exception}{Environment.NewLine}");
        Environment.Exit(2);
    }

    private static string? ValueOf(IReadOnlyList<string> args, string name) =>
        args.FirstOrDefault(arg => arg.StartsWith(name, StringComparison.OrdinalIgnoreCase))?[name.Length..];
}
