using System.Xml.Linq;
using Sillfall.Core.Tests.Support;

namespace Sillfall.Core.Tests.Release;

/// <summary>
/// M14 keeps the package, the privacy policy and the winget manifest in step with <see cref="AppInfo"/>.
/// The store name is reserved in Partner Center before submission (ADR-0017).
/// </summary>
public class ReleasePackageTests
{
    private static readonly XNamespace s_manifest = "http://schemas.microsoft.com/appx/manifest/foundation/windows10";
    private static readonly XNamespace s_uap = "http://schemas.microsoft.com/appx/manifest/uap/windows10";
    private static readonly XNamespace s_uap5 = "http://schemas.microsoft.com/appx/manifest/uap/windows10/5";
    private static readonly XNamespace s_restricted = "http://schemas.microsoft.com/appx/manifest/foundation/windows10/restrictedcapabilities";

    private const string PlaceholderHash = "0000000000000000000000000000000000000000000000000000000000000000";

    [Fact]
    public void Manifest_UsesTheDevelopmentIdentityAndTheProductVersion()
    {
        XDocument manifest = Repo.LoadXml("src/Sillfall.App/Package.appxmanifest");
        XElement identity = manifest.Root!.Element(s_manifest + "Identity")!;
        XElement properties = manifest.Root.Element(s_manifest + "Properties")!;

        identity.Attribute("Name")!.Value.Should().Be("Sillfall.Dev");
        identity.Attribute("Publisher")!.Value.Should().Be("CN=Sillfall Development");
        identity.Attribute("Version")!.Value.Should().Be($"{ProductVersion()}.0");
        properties.Element(s_manifest + "DisplayName")!.Value.Should().Be(AppInfo.Name);
        properties.Element(s_manifest + "PublisherDisplayName")!.Value.Should().Be(AppInfo.Name);
        Product().Should().Be(AppInfo.Name);
    }

    [Fact]
    public void Manifest_KeepsTheStableProtocolAndStartupTask()
    {
        XDocument manifest = Repo.LoadXml("src/Sillfall.App/Package.appxmanifest");
        XElement application = manifest.Root!.Element(s_manifest + "Applications")!.Element(s_manifest + "Application")!;
        XElement visuals = application.Element(s_uap + "VisualElements")!;
        IEnumerable<XElement> extensions = application.Element(s_manifest + "Extensions")!.Elements();

        visuals.Attribute("DisplayName")!.Value.Should().Be(AppInfo.Name);
        visuals.Attribute("Description")!.Value.Should().Be("Animated weather that settles on the edges of your open windows.");

        XElement protocol = extensions.Elements(s_uap + "Protocol").Single();
        protocol.Attribute("Name")!.Value.Should().Be(AppInfo.UriScheme);

        XElement startup = extensions.Elements(s_uap5 + "StartupTask").Single();
        startup.Attribute("TaskId")!.Value.Should().Be(AppInfo.StartupTaskId);
        startup.Attribute("Enabled")!.Value.Should().Be("false");
    }

    [Fact]
    public void Manifest_DeclaresOnlyLocationAndFullTrust()
    {
        XElement capabilities = Repo.LoadXml("src/Sillfall.App/Package.appxmanifest").Root!.Element(s_manifest + "Capabilities")!;

        string[] names =
        [
            .. capabilities.Elements(s_restricted + "Capability").Select(item => item.Attribute("Name")!.Value),
            .. capabilities.Elements(s_manifest + "DeviceCapability").Select(item => item.Attribute("Name")!.Value),
        ];

        names.Should().BeEquivalentTo(["runFullTrust", "location"]);
    }

    [Fact]
    public void Project_StampsTheSameDefaultsTheManifestCarries()
    {
        string project = Repo.ReadText("src/Sillfall.App/Sillfall.App.csproj");

        project.Should().Contain("<SillfallPackageName Condition=\"'$(SillfallPackageName)' == ''\">Sillfall.Dev</SillfallPackageName>");
        project.Should().Contain("<SillfallPackagePublisher Condition=\"'$(SillfallPackagePublisher)' == ''\">CN=Sillfall Development</SillfallPackagePublisher>");
        project.Should().Contain("StampPackageManifest");
    }

    [Fact]
    public void PrivacyPolicy_MatchesWhatTheAppDoes()
    {
        string english = Repo.ReadText("docs/privacy.md");
        string german = Repo.ReadText("docs/privacy.de.md");

        AppInfo.PrivacyPolicyUrl.Should().Be("https://github.com/TeslaV0D/Sillfall/blob/main/docs/privacy.md");
        AppInfo.PrivacyPolicyUrlGerman.Should().EndWith("/docs/privacy.de.md");
        english.Should().Contain("privacy.de.md").And.Contain("api.met.no").And.Contain("11 km").And.Contain("window titles");
        english.Should().Contain("does not record the screen").And.Contain("No telemetry");
        german.Should().Contain("privacy.md").And.Contain("api.met.no").And.Contain("11 km").And.Contain("Fenstertitel");
        Repo.ReadText("docs/store/listing.md").Should().Contain(AppInfo.PrivacyPolicyUrl).And.Contain(AppInfo.SupportUrl);
    }

    [Fact]
    public void Winget_PointsAtTheReleaseAndKeepsPlaceholderHashes()
    {
        string[] manifests = [.. Repo.Files("winget").Where(file => file.EndsWith(".yaml", StringComparison.Ordinal))];

        manifests.Should().HaveCount(4);
        string installer = manifests.Single(file => file.EndsWith(".installer.yaml", StringComparison.Ordinal));
        string text = Repo.ReadText(installer);
        text.Should().Contain("InstallerType: msix");
        text.Should().Contain("Architecture: x64").And.Contain("Architecture: arm64");
        text.Should().Contain($"releases/download/v{AppInfo.DisplayVersion}/Sillfall_{AppInfo.DisplayVersion}.0_x64.msix");
        text.Should().Contain($"releases/download/v{AppInfo.DisplayVersion}/Sillfall_{AppInfo.DisplayVersion}.0_arm64.msix");
        text.Split(PlaceholderHash).Should().HaveCount(5, "two architectures, and each has an installer hash and a signature hash, all still placeholders");

        foreach (string manifest in manifests)
        {
            Repo.ReadText(manifest).Should().Contain($"PackageVersion: {AppInfo.DisplayVersion}");
            Repo.ReadText(manifest).Should().Contain("PackageIdentifier: TeslaV0D.Sillfall");
        }

        Repo.ReadText(manifests.Single(file => file.EndsWith(".locale.en-US.yaml", StringComparison.Ordinal)))
            .Should().Contain($"PrivacyUrl: {AppInfo.PrivacyPolicyUrl}")
            .And.Contain($"PackageName: {AppInfo.Name}");
        Repo.ReadText(manifests.Single(file => file.EndsWith(".locale.de-DE.yaml", StringComparison.Ordinal)))
            .Should().Contain($"PrivacyUrl: {AppInfo.PrivacyPolicyUrlGerman}");
    }

    [Fact]
    public void Installer_MatchesTheAppIdentity()
    {
        string script = Repo.ReadText("tools/installer/Sillfall.iss");

        script.Should().Contain($"#define AppName \"{AppInfo.Name}\"");
        script.Should().Contain($"#define UriScheme \"{AppInfo.UriScheme}\"");
        script.Should().Contain($"#define AppUrl \"{AppInfo.RepositoryUrl}\"");
        script.Should().Contain("AppId={{62E2C09E-EACC-4D42-87E2-6B1CFF3237F2}", "the id ties updates and uninstall to earlier installs");
        script.Should().Contain(@"AppMutex=Local\Sillfall.SingleInstance", "setup closes a running app through the single-instance mutex");
        script.Should().Contain("PrivilegesRequired=lowest");
        script.Should().Contain("MinVersion=10.0.19041");
        script.Should().Contain("Sillfall.App.exe");
        Repo.ReadText("tools/ci/installer.ps1").Should().Contain("Sillfall.App.exe").And.Contain("WindowsPackageType=None");
    }

    private static string ProductVersion()
    {
        XElement version = Repo.LoadXml("Directory.Build.props").Descendants("Version").Single();
        return version.Value;
    }

    private static string Product()
    {
        XElement product = Repo.LoadXml("Directory.Build.props").Descendants("Product").Single();
        return product.Value;
    }
}
