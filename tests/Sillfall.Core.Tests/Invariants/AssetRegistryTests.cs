using System.Text.RegularExpressions;
using Sillfall.Core.Tests.Support;

namespace Sillfall.Core.Tests.Invariants;

/// <summary>
/// Rule 5 of the project rules: every asset has an entry in ASSETS.md with source, license and author.
/// </summary>
public partial class AssetRegistryTests
{
    private const string RegistryFile = "ASSETS.md";

    /// <summary>Media file types that count as assets wherever they are in the repository.</summary>
    private static readonly string[] s_assetExtensions =
    [
        ".png", ".jpg", ".jpeg", ".gif", ".bmp", ".webp", ".ico", ".svg",
        ".wav", ".ogg", ".mp3", ".flac",
        ".ttf", ".otf", ".woff", ".woff2",
    ];

    [Fact]
    public void EveryAsset_IsRegistered()
    {
        IReadOnlyList<string> entries = RegistryEntries();

        Repo.Files()
            .Where(IsAsset)
            .Where(asset => !IsCovered(asset, entries))
            .Should().BeEmpty($"every asset needs a row in {RegistryFile} (source, license, author)");
    }

    [Fact]
    public void EveryRegistryEntry_PointsToExistingFiles()
    {
        var files = Repo.Files().ToList();

        RegistryEntries()
            .Where(entry => !files.Any(file => IsCovered(file, [entry])))
            .Should().BeEmpty($"{RegistryFile} must not list assets that no longer exist");
    }

    [Theory]
    [InlineData("src/Sillfall.App/Assets/StoreLogo.png", true)]
    [InlineData("src/Sillfall.App/Assets/data.bin", true)]
    [InlineData("docs/images/overview.svg", true)]
    [InlineData("src/Sillfall.Render/Shaders/Rain.hlsl", false)]
    [InlineData("src/Sillfall.Core/AppInfo.cs", false)]
    [InlineData("tools/icons/generate_app_icons.py", false)]
    public void IsAsset_ClassifiesByExtensionAndAssetsFolder(string path, bool expected)
    {
        IsAsset(path).Should().Be(expected);
    }

    [Theory]
    [InlineData("a/b/icon.png", "a/b/icon.png", true)]
    [InlineData("a/b/icon.png", "a/b/", true)]
    [InlineData("a/b/c/icon.png", "a/b/", true)]
    [InlineData("a/b/icon.png", "a/b/other.png", false)]
    [InlineData("a/bc/icon.png", "a/b", false)]
    public void IsCovered_MatchesFilesAndDirectoryEntries(string file, string entry, bool expected)
    {
        IsCovered(file, [entry]).Should().Be(expected);
    }

    /// <summary>A file is an asset if it is a media file, or if it lives in a folder named <c>Assets</c>.</summary>
    private static bool IsAsset(string relativePath) =>
        s_assetExtensions.Contains(Path.GetExtension(relativePath), StringComparer.OrdinalIgnoreCase)
        || $"/{relativePath}".Contains("/Assets/", StringComparison.OrdinalIgnoreCase);

    /// <summary>An entry covers one exact file, or everything below it when it ends with a slash.</summary>
    private static bool IsCovered(string relativePath, IReadOnlyList<string> entries) =>
        entries.Any(entry => entry.EndsWith('/')
            ? relativePath.StartsWith(entry, StringComparison.Ordinal)
            : relativePath.Equals(entry, StringComparison.Ordinal));

    /// <summary>Paths from the first column of the registry tables: <c>| `path` | …</c>.</summary>
    private static IReadOnlyList<string> RegistryEntries() =>
        [.. File.ReadLines(Repo.PathOf(RegistryFile))
            .Select(line => RegistryRow().Match(line))
            .Where(match => match.Success)
            .Select(match => match.Groups["path"].Value)];

    [GeneratedRegex(@"^\|\s*`(?<path>[^`]+)`\s*\|")]
    private static partial Regex RegistryRow();
}
