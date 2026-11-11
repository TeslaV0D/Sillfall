using Weatherly.Core.Tests.Support;

namespace Weatherly.Core.Tests.Invariants;

/// <summary>
/// Rule 4 of the project rules. Weatherly reads window geometry only: never titles or contents,
/// and it talks to the network only through weather providers.
/// </summary>
public class PrivacyInvariantTests
{
    /// <summary>The only place that may contain network code: implementations of <c>IWeatherProvider</c>.</summary>
    private const string WeatherProviderDirectory = "src/Weatherly.Core/Weather/Providers/";

    /// <summary>Win32 ways to read a window title or its text content.</summary>
    private static readonly string[] s_windowTextApis = ["GetWindowText", "WM_GETTEXT"];

    private static readonly string[] s_networkApis =
    [
        "System.Net",
        "HttpClient",
        "HttpMessageHandler",
        "WebRequest",
        "WebSocket",
        "TcpClient",
        "UdpClient",
        "Windows.Web",
        "Windows.Networking",
    ];

    [Fact]
    public void ProductCode_NeverReadsWindowTitlesOrText()
    {
        FindUsages(Repo.ProductSourceFiles(), s_windowTextApis)
            .Should().BeEmpty("only window geometry may be read, never titles or contents");
    }

    [Fact]
    public void ProductCode_UsesTheNetworkOnlyInWeatherProviders()
    {
        var filesOutsideProviders = Repo.ProductSourceFiles()
            .Where(file => !file.StartsWith(WeatherProviderDirectory, StringComparison.Ordinal));

        FindUsages(filesOutsideProviders, s_networkApis)
            .Should().BeEmpty($"network access is only allowed in {WeatherProviderDirectory}");
    }

    [Fact]
    public void Scan_CoversTheInteropDeclarations()
    {
        // Guards the scan itself: if the CsWin32 list moves or is renamed, the tests above would pass vacuously.
        Repo.ProductSourceFiles().Should().Contain("src/Weatherly.Platform/NativeMethods.txt");
    }

    private static List<string> FindUsages(IEnumerable<string> files, string[] needles)
    {
        var usages = new List<string>();
        foreach (string file in files)
        {
            string[] lines = File.ReadAllLines(Repo.PathOf(file));
            for (int i = 0; i < lines.Length; i++)
            {
                // The CsWin32 list documents the ban in a comment. Comments there are not API requests.
                if (file.EndsWith("NativeMethods.txt", StringComparison.Ordinal)
                    && lines[i].TrimStart().StartsWith("//", StringComparison.Ordinal))
                {
                    continue;
                }

                foreach (string needle in needles)
                {
                    if (lines[i].Contains(needle, StringComparison.OrdinalIgnoreCase))
                    {
                        usages.Add($"{file}:{i + 1}: {needle}");
                    }
                }
            }
        }

        return usages;
    }
}
