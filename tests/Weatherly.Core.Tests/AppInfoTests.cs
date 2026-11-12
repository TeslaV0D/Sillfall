namespace Weatherly.Core.Tests;

public class AppInfoTests
{
    [Fact]
    public void Name_IsTheWorkingTitle()
    {
        AppInfo.Name.Should().Be("Weatherly");
    }

    [Fact]
    public void DisplayVersion_HasThreeNumericParts()
    {
        AppInfo.DisplayVersion.Should().MatchRegex(@"^\d+\.\d+\.\d+$");
    }

    [Fact]
    public void UriScheme_IsValidAndLowercase()
    {
        Uri.CheckSchemeName(AppInfo.UriScheme).Should().BeTrue();
        AppInfo.UriScheme.Should().Be(AppInfo.UriScheme.ToLowerInvariant());
    }

    [Fact]
    public void PublicUrls_AreHttpsPagesOfTheRepository()
    {
        AppInfo.RepositoryUrl.Should().Be("https://github.com/TeslaV0D/weatherly_windows");
        AppInfo.SupportUrl.Should().StartWith(AppInfo.RepositoryUrl);
        AppInfo.PrivacyPolicyUrl.Should().StartWith(AppInfo.RepositoryUrl).And.EndWith("/docs/privacy.md");
        AppInfo.PrivacyPolicyUrlGerman.Should().StartWith(AppInfo.RepositoryUrl).And.EndWith("/docs/privacy.de.md");
    }
}
