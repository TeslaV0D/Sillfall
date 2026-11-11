using System.Globalization;
using Weatherly.Core.Diagnostics;
using Weatherly.Core.Displays;
using Weatherly.Core.Overlay;
using Weatherly.Platform;

namespace Weatherly.App;

/// <summary>
/// Checks that the layers below the UI work on this machine. The results are shown in the window
/// and written to the smoke test report.
/// </summary>
internal static class StartupDiagnostics
{
    /// <summary>Evaluates the process and the measured overlay state.</summary>
    /// <param name="status">Overlay state, or <see langword="null"/> when the overlay could not be started.</param>
    /// <param name="options">The options the overlay was asked to apply.</param>
    /// <param name="overlayError">Why the overlay could not be started or queried, if it could not.</param>
    public static IReadOnlyList<DiagnosticResult> Evaluate(OverlayStatus? status, OverlayOptions options, string? overlayError)
    {
        var results = new List<DiagnosticResult>
        {
            DpiAwareness.IsPerMonitorV2()
                ? new DiagnosticResult("DPI awareness", true, "Per-Monitor V2")
                : new DiagnosticResult("DPI awareness", false, "Not Per-Monitor V2. Overlay coordinates would be wrong on scaled displays."),
        };

        if (status is null)
        {
            results.Add(new DiagnosticResult("Overlay", false, overlayError ?? "Not running"));
            return results;
        }

        if (options.Enabled)
        {
            results.Add(new DiagnosticResult(
                "Graphics adapter",
                status.AdapterName is not null,
                status.AdapterName ?? "No graphics device"));
        }

        results.AddRange(OverlayDiagnostics.Evaluate(status, options));
        return results;
    }

    /// <summary>One line per monitor for the window and the report.</summary>
    public static IReadOnlyList<string> DescribeMonitors(OverlayStatus status)
    {
        var lines = new List<string>(status.Monitors.Count);
        foreach (MonitorInfo monitor in status.Monitors)
        {
            OverlayWindowStatus? overlay = status.Overlays.FirstOrDefault(
                candidate => string.Equals(candidate.Monitor.DeviceName, monitor.DeviceName, StringComparison.OrdinalIgnoreCase));

            string overlayText = overlay is null
                ? "no overlay"
                : string.Create(CultureInfo.InvariantCulture, $"overlay shown, {overlay.PresentedFrames} frame(s) presented");

            lines.Add(string.Create(
                CultureInfo.InvariantCulture,
                $"{monitor.DeviceName.TrimStart('\\', '.')}{(monitor.IsPrimary ? " (primary)" : string.Empty)}: {monitor.Bounds.Width} x {monitor.Bounds.Height} at ({monitor.Bounds.Left}, {monitor.Bounds.Top}), {monitor.ScaleFactor:P0} scaling, {overlayText}"));
        }

        return lines;
    }
}
