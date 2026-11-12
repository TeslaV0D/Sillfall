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
}
