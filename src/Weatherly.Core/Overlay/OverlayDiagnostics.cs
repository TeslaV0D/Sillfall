using System.Globalization;
using Weatherly.Core.Diagnostics;

namespace Weatherly.Core.Overlay;

/// <summary>
/// Turns a measured <see cref="OverlayStatus"/> into pass/fail checks. These are the properties
/// that make an overlay acceptable: it covers every monitor exactly, lets clicks through,
/// never takes the focus, honors the screenshot setting and does not render while idle.
/// </summary>
public static class OverlayDiagnostics
{
    /// <summary>Checks <paramref name="status"/> against what <paramref name="options"/> ask for.</summary>
    public static IReadOnlyList<DiagnosticResult> Evaluate(OverlayStatus status, OverlayOptions options)
    {
        ArgumentNullException.ThrowIfNull(status);
        ArgumentNullException.ThrowIfNull(options);

        var results = new List<DiagnosticResult>();
        IReadOnlyList<OverlayWindowStatus> overlays = status.Overlays;

        if (!options.Enabled)
        {
            results.Add(new DiagnosticResult(
                "Overlay windows",
                overlays.Count == 0,
                overlays.Count == 0 ? "Switched off, no overlay window exists" : $"Switched off, but {overlays.Count} overlay window(s) still exist"));
        }
        else
        {
            results.Add(CheckCoverage(status));
            results.Add(CheckAll(
                "Overlay window styles",
                overlays,
                overlay => overlay.HasOverlayStyles,
                "Layered, transparent, topmost, tool window, no-activate, no redirection bitmap",
                "Missing overlay styles"));
            results.Add(CheckAll(
                "Click-through",
                overlays,
                overlay => overlay.IsClickThrough,
                "Hit tests pass through the overlay",
                "The overlay catches mouse input"));
            results.Add(CheckZOrder(overlays));
            results.Add(CheckAll(
                "First frame",
                overlays,
                overlay => overlay.PresentedFrames > 0,
                "Every overlay has presented",
                "Nothing presented yet"));
            results.Add(CheckAll(
                "Screenshot exclusion",
                overlays,
                overlay => overlay.IsExcludedFromCapture == options.ExcludeFromCapture,
                options.ExcludeFromCapture ? "Overlays are hidden from screenshots" : "Overlays are visible in screenshots",
                options.ExcludeFromCapture ? "Still visible in screenshots" : "Still hidden from screenshots"));
        }

        results.Add(new DiagnosticResult(
            "Keyboard focus",
            !status.OverlayHasFocus,
            status.OverlayHasFocus ? "An overlay is the foreground window" : "No overlay has the focus"));

        results.Add(new DiagnosticResult(
            "Overlay errors",
            status.LastError is null,
            status.LastError ?? "None"));

        return results;
    }

    /// <summary>
    /// Compares two snapshots taken some time apart while the content did not change.
    /// An idle overlay must not present: every extra frame costs GPU time and battery.
    /// </summary>
    public static DiagnosticResult EvaluateIdle(OverlayStatus earlier, OverlayStatus later)
    {
        ArgumentNullException.ThrowIfNull(earlier);
        ArgumentNullException.ThrowIfNull(later);

        long before = earlier.Overlays.Sum(overlay => overlay.PresentedFrames);
        long after = later.Overlays.Sum(overlay => overlay.PresentedFrames);
        bool sameWindows = earlier.Overlays.Select(overlay => overlay.WindowHandle)
            .SequenceEqual(later.Overlays.Select(overlay => overlay.WindowHandle));

        if (!sameWindows)
        {
            return new DiagnosticResult("Idle rendering", false, "The overlays were rebuilt between the two measurements");
        }

        return new DiagnosticResult(
            "Idle rendering",
            after == before,
            after == before
                ? "No frame presented while the content was static"
                : string.Create(CultureInfo.InvariantCulture, $"{after - before} frame(s) presented although nothing changed"));
    }

    /// <summary>
    /// A normal window above an overlay hides the weather on that window, so it fails the check.
    /// Other topmost windows (taskbar, tool palettes) can legitimately end up above for a moment:
    /// they are reported but tolerated.
    /// </summary>
    private static DiagnosticResult CheckZOrder(IReadOnlyList<OverlayWindowStatus> overlays)
    {
        const string CheckName = "Z-order";

        if (overlays.Count == 0)
        {
            return new DiagnosticResult(CheckName, false, "No overlay window exists");
        }

        var normal = overlays
            .SelectMany(overlay => overlay.CoveredBy.Where(window => !window.IsTopmost).Select(window => $"{overlay.Monitor.DeviceName}: {window}"))
            .ToList();
        if (normal.Count > 0)
        {
            return new DiagnosticResult(CheckName, false, $"Normal windows above the overlay: {string.Join(", ", normal)}");
        }

        var topmost = overlays.SelectMany(overlay => overlay.CoveredBy).Select(window => window.ToString()).Distinct().ToList();
        return new DiagnosticResult(
            CheckName,
            true,
            topmost.Count == 0 ? "Overlays are above every window" : $"Above every normal window. Topmost windows above: {string.Join(", ", topmost)}");
    }

    private static DiagnosticResult CheckCoverage(OverlayStatus status)
    {
        const string CheckName = "Overlay windows";

        var covered = status.Overlays.Select(overlay => overlay.Monitor.DeviceName).ToHashSet(StringComparer.OrdinalIgnoreCase);
        var missing = status.Monitors.Where(monitor => !covered.Contains(monitor.DeviceName)).Select(monitor => monitor.DeviceName).ToList();
        if (missing.Count > 0)
        {
            return new DiagnosticResult(CheckName, false, $"No overlay on {string.Join(", ", missing)}");
        }

        OverlayWindowStatus? misplaced = status.Overlays.FirstOrDefault(overlay => !overlay.CoversMonitor);
        if (misplaced is not null)
        {
            return new DiagnosticResult(
                CheckName,
                false,
                $"{misplaced.Monitor.DeviceName}: window is at {misplaced.WindowBounds}, monitor is at {misplaced.Monitor.Bounds}");
        }

        return status.Monitors.Count == 0
            ? new DiagnosticResult(CheckName, false, "Windows reports no monitor")
            : new DiagnosticResult(CheckName, true, string.Create(CultureInfo.InvariantCulture, $"{status.Overlays.Count} of {status.Monitors.Count} monitor(s) covered exactly"));
    }

    private static DiagnosticResult CheckAll(
        string name,
        IReadOnlyList<OverlayWindowStatus> overlays,
        Func<OverlayWindowStatus, bool> predicate,
        string passedDetail,
        string failedDetail)
    {
        var failing = overlays.Where(overlay => !predicate(overlay)).Select(overlay => overlay.Monitor.DeviceName).ToList();

        // Without any overlay there is nothing that could have the property. The coverage check reports that case.
        if (overlays.Count == 0)
        {
            return new DiagnosticResult(name, false, "No overlay window exists");
        }

        return failing.Count == 0
            ? new DiagnosticResult(name, true, passedDetail)
            : new DiagnosticResult(name, false, $"{failedDetail}: {string.Join(", ", failing)}");
    }
}
