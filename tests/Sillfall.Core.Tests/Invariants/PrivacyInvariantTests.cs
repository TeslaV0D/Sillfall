using System.Text.RegularExpressions;
using Sillfall.Core.Tests.Support;
using Sillfall.Core.Weather;
using Sillfall.Core.Weather.Providers;

namespace Sillfall.Core.Tests.Invariants;

/// <summary>
/// Rule 4 of the project rules. Sillfall reads window geometry only: never titles, contents or
/// the screen itself, and it talks to the network only through weather providers.
/// </summary>
public class PrivacyInvariantTests
{
    /// <summary>The only place that may contain network code: implementations of <c>IWeatherProvider</c>.</summary>
    private const string WeatherProviderDirectory = "src/Sillfall.Core/Weather/Providers/";

    /// <summary>Win32 ways to read a window title or its text content.</summary>
    private static readonly string[] s_windowTextApis = ["GetWindowText", "WM_GETTEXT"];

    /// <summary>Ways to read pixels of the screen or of other windows.</summary>
    private static readonly string[] s_screenCaptureApis =
    [
        "BitBlt",
        "StretchBlt",
        "PrintWindow",
        "CopyFromScreen",
        // GDI's GetPixel, matched as a whole word: RenderTargetBitmap.GetPixelsAsync renders the app's own
        // controls (the smoke test's pictures of the settings pages), not the screen.
        @"\bGetPixel\b",
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

    [Theory]
    [InlineData(52.520008, 13.404954, "lat=52.5&lon=13.4")]
    [InlineData(-33.868820, 151.209290, "lat=-33.9&lon=151.2")]
    [InlineData(40.712776, -74.005974, "lat=40.7&lon=-74.0")]
    public void WeatherRequests_CarryTheLocationRoundedToOneDecimal(double latitude, double longitude, string query)
    {
        // About 11 km: enough for the weather, too rough to find a street (PLAN.md, section 5).
        Uri uri = MetNorwayProvider.RequestUri(new GeoPoint(latitude, longitude));

        uri.Query.Should().Be("?" + query);
    }

    [Fact]
    public void Scan_CoversTheInteropDeclarations()
    {
        // Guards the scan itself: if the CsWin32 list moves or is renamed, the tests above would pass vacuously.
        Repo.ProductSourceFiles().Should().Contain("src/Sillfall.Platform/NativeMethods.txt");
    }

    [Theory]
    [InlineData("COLORREF c = GetPixel(hdc, 1, 2);", true)]
    [InlineData("GetPixel", true)]
    [InlineData("byte[] p = (await bitmap.GetPixelsAsync()).ToArray();", false)]
    public void Scan_MatchesGdiGetPixelOnly(string line, bool expected)
    {
        // Guards the whole-word pattern above against a change that lets GDI's GetPixel through.
        s_screenCaptureApis.Any(needle => Matches(line, needle)).Should().Be(expected);
    }

    /// <summary>A needle is a plain substring, unless it starts with <c>\b</c>: then it is a regular expression.</summary>
    private static bool Matches(string line, string needle) => needle.StartsWith(@"\b", StringComparison.Ordinal)
        ? Regex.IsMatch(line, needle, RegexOptions.IgnoreCase | RegexOptions.CultureInvariant, TimeSpan.FromSeconds(1))
        : line.Contains(needle, StringComparison.OrdinalIgnoreCase);

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
                    if (Matches(lines[i], needle))
                    {
                        usages.Add($"{file}:{i + 1}: {needle}");
                    }
                }
            }
        }

        return usages;
    }
}
