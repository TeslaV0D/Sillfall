using System.Reflection;
using Sillfall.Core.Tests.Support;

namespace Sillfall.Core.Tests.Invariants;

/// <summary>
/// Rule 2 of the project rules: Sillfall.Core stays free of Windows dependencies, so the simulation,
/// geometry and settings code can be built and tested on Linux.
/// </summary>
public class ArchitectureTests
{
    private const string CoreProject = "src/Sillfall.Core/Sillfall.Core.csproj";

    /// <summary>Assembly and package name prefixes that only exist on Windows or belong to the Windows layers.</summary>
    private static readonly string[] s_windowsOnlyPrefixes =
    [
        "Vortice",
        "SharpGen",
        "Microsoft.Windows",
        "Microsoft.WindowsAppSDK",
        "Microsoft.UI",
        "WinRT",
        "H.NotifyIcon",
        "System.Windows",
        "System.Drawing.Common",
        "PresentationCore",
        "PresentationFramework",
        "WindowsBase",
    ];

    /// <summary>Allowed project references per layer. A project that is missing here fails the test.</summary>
    private static readonly Dictionary<string, string[]> s_allowedProjectReferences = new()
    {
        ["Sillfall.Core"] = [],
        ["Sillfall.Platform"] = ["Sillfall.Core"],
        ["Sillfall.Render"] = ["Sillfall.Core"],
        ["Sillfall.Audio"] = ["Sillfall.Core"],
        ["Sillfall.App"] = ["Sillfall.Core", "Sillfall.Platform", "Sillfall.Render", "Sillfall.Audio"],
        ["Sillfall.Sandbox"] = ["Sillfall.Core", "Sillfall.Platform", "Sillfall.Render", "Sillfall.Audio"],
        ["Sillfall.FrameDump"] = ["Sillfall.Core", "Sillfall.Render"],
        ["Sillfall.Core.Tests"] = ["Sillfall.Core"],
        ["Sillfall.Render.Tests"] = ["Sillfall.Core", "Sillfall.Render"],
    };

    [Fact]
    public void Core_TargetsPlatformNeutralFramework()
    {
        ProjectFile.Load(CoreProject).TargetFramework.Should().Be("net10.0");
    }

    [Fact]
    public void Core_HasNoProjectReferences()
    {
        ProjectFile.Load(CoreProject).ProjectReferences.Should().BeEmpty();
    }

    [Fact]
    public void Core_HasNoWindowsOnlyPackages()
    {
        ProjectFile.Load(CoreProject).PackageReferences
            .Where(IsWindowsOnly)
            .Should().BeEmpty("Sillfall.Core must build and run on Linux");
    }

    [Fact]
    public void CoreAssembly_ReferencesNoWindowsOnlyAssemblies()
    {
        Assembly core = typeof(AppInfo).Assembly;

        core.GetReferencedAssemblies()
            .Select(reference => reference.Name!)
            .Where(IsWindowsOnly)
            .Should().BeEmpty("Sillfall.Core must build and run on Linux");
    }

    [Fact]
    public void CoreSources_DeclareNoNativeImports()
    {
        string[] nativeImportMarkers = ["DllImport", "LibraryImport", "System.Runtime.InteropServices.Marshalling"];

        var offenders = Repo.Files("src/Sillfall.Core")
            .Where(file => file.EndsWith(".cs", StringComparison.Ordinal))
            .Where(file => nativeImportMarkers.Any(marker => Repo.ReadText(file).Contains(marker, StringComparison.Ordinal)));

        offenders.Should().BeEmpty("native interop belongs in Sillfall.Platform");
    }

    [Fact]
    public void EveryProject_IsCoveredByTheLayeringRules()
    {
        Repo.SolutionProjects()
            .Select(path => ProjectFile.Load(path).Name)
            .Should().BeEquivalentTo(s_allowedProjectReferences.Keys);
    }

    [Fact]
    public void ProjectReferences_FollowTheLayering()
    {
        var violations =
            from path in Repo.SolutionProjects()
            let project = ProjectFile.Load(path)
            from reference in project.ProjectReferences
            where !s_allowedProjectReferences.GetValueOrDefault(project.Name, []).Contains(reference)
            select $"{project.Name} -> {reference}";

        violations.Should().BeEmpty();
    }

    private static bool IsWindowsOnly(string name) =>
        s_windowsOnlyPrefixes.Any(prefix =>
            name.Equals(prefix, StringComparison.OrdinalIgnoreCase)
            || name.StartsWith(prefix + ".", StringComparison.OrdinalIgnoreCase));
}
