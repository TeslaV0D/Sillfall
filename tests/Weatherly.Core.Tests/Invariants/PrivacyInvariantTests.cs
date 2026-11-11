using Weatherly.Core.Tests.Support;

namespace Weatherly.Core.Tests.Invariants;

/// <summary>
/// Rule 4 of the project rules. Weatherly reads window geometry only: never titles, contents or
/// the screen itself, and it talks to the network only through weather providers.
/// </summary>
public class PrivacyInvariantTests
{
    /// <summary>The only place that may contain network code: implementations of <c>IWeatherProvider</c>.</summary>
    private const string WeatherProviderDirectory = "src/Weatherly.Core/Weather/Providers/";

    /// <summary>Win32 ways to read a window title or its text content.</summary>
    private static readonly string[] s_windowTextApis = ["GetWindowText", "WM_GETTEXT"];

    /// <summary>Ways to read pixels of the screen or of other windows.</summary>
    private static readonly string[] s_screenCaptureApis =
    [
        "BitBlt",
        "StretchBlt",
        "PrintWindow",
        "CopyFromScreen",
        "GetPixel",
        "Windows.Graphics.Capture",
        "GraphicsCapture",
        "DuplicateOutput",
        "OutputDuplication",
    ];

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
    public void ProductCode_NeverReadsTheScreen()
    {
        FindUsages(Repo.ProductSourceFiles(), s_screenCaptureApis)
            .Should().BeEmpty("the overlay draws on top of the desktop but never looks at it");
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
