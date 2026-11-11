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
/// <item><c>--show-surfaces</c> starts with the surface debug layer on.</item>
/// <item><c>--no-test-pattern</c> starts without the calibration pattern.</item>
/// <item><c>--rain</c> starts with rain and checks the render loop like <c>--test-drops</c>. Before the
/// screenshot the rain is off for a moment, so the script can compare a dry and a wet desktop.</item>
/// <item><c>--snow</c> does the same with snow: render loop check (including melting until the loop stops),
/// then a snow-free and a snowy screenshot.</item>
/// <item><c>--test-drops</c> starts with the calibration drops and checks the render loop: frame rate,
/// zero allocations, and that it stops once the drops are switched off.</item>
/// </list>
/// In smoke test mode the app also moves its own window once and checks that the tracking follows.
/// </summary>
internal sealed class SmokeTest
{
    private const string ReportSwitch = "--smoke-test=";
    private const string HoldSwitch = "--smoke-hold=";
    private const string ExcludeSwitch = "--exclude-from-capture";
    private const string SurfacesSwitch = "--show-surfaces";
    private const string NoPatternSwitch = "--no-test-pattern";
    private const string DropsSwitch = "--test-drops";
    private const string RainSwitch = "--rain";
    private const string SnowSwitch = "--snow";

    private readonly string _reportPath;

    private SmokeTest(string reportPath, TimeSpan hold, bool excludeFromCapture, bool showSurfaces, bool testPattern, bool testDrops, bool rain, bool snow)
    {
        TestDrops = testDrops;
        Rain = rain;
        Snow = snow;
        _reportPath = reportPath;
        Hold = hold;
        ExcludeFromCapture = excludeFromCapture;
        ShowSurfaces = showSurfaces;
        TestPattern = testPattern;
    }

    /// <summary>How long the app stays on screen after the report was written.</summary>
    public TimeSpan Hold { get; }

    public bool ExcludeFromCapture { get; }

    public bool ShowSurfaces { get; }

    public bool TestPattern { get; }

    public bool TestDrops { get; }

    public bool Rain { get; }

    public bool Snow { get; }

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
            args.Contains(ExcludeSwitch, StringComparer.OrdinalIgnoreCase),
            args.Contains(SurfacesSwitch, StringComparer.OrdinalIgnoreCase),
            !args.Contains(NoPatternSwitch, StringComparer.OrdinalIgnoreCase),
            args.Contains(DropsSwitch, StringComparer.OrdinalIgnoreCase),
            args.Contains(RainSwitch, StringComparer.OrdinalIgnoreCase),
            args.Contains(SnowSwitch, StringComparer.OrdinalIgnoreCase));
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
