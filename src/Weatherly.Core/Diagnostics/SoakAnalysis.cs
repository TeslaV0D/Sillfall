using System.Globalization;

namespace Weatherly.Core.Diagnostics;

/// <summary>One measurement of the running app in the soak test.</summary>
/// <param name="Minutes">Time since the start of the run.</param>
/// <param name="PrivateMegabytes">Private memory of the process.</param>
/// <param name="ManagedMegabytes">Managed heap after a full collection.</param>
/// <param name="Handles">Kernel handles.</param>
/// <param name="GdiObjects">GDI objects.</param>
/// <param name="UserObjects">USER objects (windows, menus, cursors).</param>
public readonly record struct SoakSample(double Minutes, double PrivateMegabytes, double ManagedMegabytes, double Handles, double GdiObjects, double UserObjects);

/// <summary>How much a measure may grow: a fixed allowance for warm-up noise plus a rate over time.</summary>
/// <param name="Name">What is measured, for the report.</param>
/// <param name="Unit">Unit for the report ("MB", "handles").</param>
/// <param name="Allowance">Growth that is always fine (caches settling, the runtime's own pools).</param>
/// <param name="PerHour">Growth per hour that is still fine.</param>
/// <param name="Read">Takes the measure from a sample.</param>
public sealed record SoakLimit(string Name, string Unit, double Allowance, double PerHour, Func<SoakSample, double> Read);

/// <summary>The verdict on one measure.</summary>
public sealed record SoakResult(string Name, bool Passed, double Growth, double Allowed, string Unit)
{
    public string Detail => string.Create(CultureInfo.InvariantCulture, $"grew {Growth:0.0} {Unit}, at most {Allowed:0.0} {Unit} allowed");
}

/// <summary>
/// Judges a soak run (PLAN.md, M13: 8 h without a leak). The first quarter is warm-up and ignored; then the median of the
/// first and of the last third of the remaining samples are compared, which is robust against single spikes (a garbage
/// collection, a resize). A leak shows as steady growth that exceeds the allowance plus the rate times the duration.
/// </summary>
public static class SoakAnalysis
{
    /// <summary>
    /// The limits of the app: generous enough for noise, tight enough that over 8 hours a leak of a handle a minute or of
    /// a megabyte every few minutes shows. A short run (the 5-minute CI soak) only catches fast leaks.
    /// </summary>
    public static IReadOnlyList<SoakLimit> DefaultLimits { get; } =
    [
        new("Private memory", "MB", Allowance: 30, PerHour: 8, sample => sample.PrivateMegabytes),
        new("Managed heap", "MB", Allowance: 8, PerHour: 2, sample => sample.ManagedMegabytes),
        new("Handles", "handles", Allowance: 100, PerHour: 10, sample => sample.Handles),
        new("GDI objects", "objects", Allowance: 20, PerHour: 5, sample => sample.GdiObjects),
        new("USER objects", "objects", Allowance: 20, PerHour: 5, sample => sample.UserObjects),
    ];

    public static IReadOnlyList<SoakResult> Judge(IReadOnlyList<SoakSample> samples, IReadOnlyList<SoakLimit>? limits = null)
    {
        ArgumentNullException.ThrowIfNull(samples);
        limits ??= DefaultLimits;
        SoakSample[] measured = [.. samples.Skip(samples.Count / 4)];
        if (measured.Length < 6)
        {
            return [.. limits.Select(limit => new SoakResult(limit.Name, false, 0, 0, limit.Unit))];
        }

        int third = measured.Length / 3;
        double hours = (measured[^1].Minutes - measured[0].Minutes) / 60.0;
        var results = new List<SoakResult>();
        foreach (SoakLimit limit in limits)
        {
            double start = Median(measured.Take(third).Select(limit.Read));
            double end = Median(measured.Skip(measured.Length - third).Select(limit.Read));
            double allowed = limit.Allowance + (limit.PerHour * hours);
            results.Add(new SoakResult(limit.Name, end - start <= allowed, end - start, allowed, limit.Unit));
        }

        return results;
    }

    /// <summary>The samples as CSV, for a look in a spreadsheet.</summary>
    public static string ToCsv(IEnumerable<SoakSample> samples)
    {
        ArgumentNullException.ThrowIfNull(samples);
        var lines = new List<string> { "minutes,private_mb,managed_mb,handles,gdi,user" };
        lines.AddRange(samples.Select(s => string.Create(CultureInfo.InvariantCulture,
            $"{s.Minutes:0.00},{s.PrivateMegabytes:0.0},{s.ManagedMegabytes:0.0},{s.Handles:0},{s.GdiObjects:0},{s.UserObjects:0}")));
        return string.Join(Environment.NewLine, lines) + Environment.NewLine;
    }

    private static double Median(IEnumerable<double> values)
    {
        double[] sorted = [.. values.Order()];
        return sorted.Length == 0 ? 0 : sorted.Length % 2 == 1 ? sorted[sorted.Length / 2] : (sorted[(sorted.Length / 2) - 1] + sorted[sorted.Length / 2]) / 2;
    }
}
