using Vortice.Direct3D11;
using Weatherly.Platform;
using Weatherly.Render;

namespace Weatherly.App;

/// <summary>Outcome of one startup check.</summary>
/// <param name="Name">What was checked.</param>
/// <param name="Passed">Whether the check succeeded.</param>
/// <param name="Detail">Measured value or error message.</param>
public sealed record DiagnosticResult(string Name, bool Passed, string Detail)
{
    public string StatusText => Passed ? "OK" : "FAILED";

    public override string ToString() => $"{StatusText}\t{Name}\t{Detail}";
}

/// <summary>
/// Checks at startup that the layers below the UI work on this machine.
/// The results are shown in the window and written to the smoke test report.
/// </summary>
internal static class StartupDiagnostics
{
    public static IReadOnlyList<DiagnosticResult> Run() =>
    [
        Check("DPI awareness", CheckDpiAwareness),
        Check("Direct3D 11 device", CheckGraphicsDevice),
    ];

    private static (bool Passed, string Detail) CheckDpiAwareness() =>
        DpiAwareness.IsPerMonitorV2()
            ? (true, "Per-Monitor V2")
            : (false, "Not Per-Monitor V2. Overlay coordinates would be wrong on scaled displays.");

    private static (bool Passed, string Detail) CheckGraphicsDevice()
    {
        using ID3D11Device device = GraphicsDeviceFactory.Create(GraphicsDriver.Hardware);
        return (true, $"Feature level {device.FeatureLevel.ToString().Replace("Level_", string.Empty).Replace('_', '.')}");
    }

    private static DiagnosticResult Check(string name, Func<(bool Passed, string Detail)> check)
    {
        try
        {
            (bool passed, string detail) = check();
            return new DiagnosticResult(name, passed, detail);
        }
#pragma warning disable CA1031 // A diagnostic must report every failure instead of crashing the app.
        catch (Exception exception)
#pragma warning restore CA1031
        {
            return new DiagnosticResult(name, false, $"{exception.GetType().Name}: {exception.Message}");
        }
    }
}
