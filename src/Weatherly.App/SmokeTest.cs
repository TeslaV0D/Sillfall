using System.Text;

namespace Weatherly.App;

/// <summary>
/// Headless self-check for CI: <c>Weatherly.App.exe --smoke-test=&lt;report file&gt;</c> starts the app,
/// shows the window, writes the startup diagnostics to the report file and exits.
/// The exit code is 0 when every check passed. This is how changes get verified on Windows
/// runners without anyone sitting in front of a desktop.
/// </summary>
internal sealed class SmokeTest
{
    private const string Switch = "--smoke-test=";

    private readonly string _reportPath;

    private SmokeTest(string reportPath)
    {
        _reportPath = reportPath;
    }

    /// <summary>Returns the smoke test when the switch is present, otherwise <see langword="null"/>.</summary>
    public static SmokeTest? FromCommandLine(IEnumerable<string> args)
    {
        string? argument = args.FirstOrDefault(arg => arg.StartsWith(Switch, StringComparison.OrdinalIgnoreCase));
        return argument is null ? null : new SmokeTest(Path.GetFullPath(argument[Switch.Length..]));
    }

    /// <summary>Writes the report and ends the process.</summary>
    public void Complete(IReadOnlyList<DiagnosticResult> diagnostics)
    {
        var report = new StringBuilder();
        report.AppendLine("OK\tWindow\tActivated");
        foreach (DiagnosticResult result in diagnostics)
        {
            report.AppendLine(result.ToString());
        }

        File.WriteAllText(_reportPath, report.ToString());
        Environment.Exit(diagnostics.All(result => result.Passed) ? 0 : 1);
    }

    /// <summary>Records a crash and ends the process.</summary>
    public void Fail(Exception exception)
    {
        File.WriteAllText(_reportPath, $"FAILED\tUnhandled exception\t{exception}{Environment.NewLine}");
        Environment.Exit(2);
    }
}
