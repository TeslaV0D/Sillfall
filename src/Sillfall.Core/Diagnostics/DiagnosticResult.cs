namespace Sillfall.Core.Diagnostics;

/// <summary>Outcome of one self-check of the running app.</summary>
/// <param name="Name">What was checked.</param>
/// <param name="Passed">Whether the check succeeded.</param>
/// <param name="Detail">Measured value or error message.</param>
public sealed record DiagnosticResult(string Name, bool Passed, string Detail)
{
    /// <summary>The state in words, so it never depends on color alone.</summary>
    public string StatusText => Passed ? "OK" : "FAILED";

    /// <summary>One line for reports: status, name and detail separated by tabs.</summary>
    public override string ToString() => $"{StatusText}\t{Name}\t{Detail}";
}
