using System.Xml.Linq;

namespace Sillfall.Core.Tests.Support;

/// <summary>The parts of a csproj that the architecture rules care about.</summary>
internal sealed class ProjectFile
{
    private readonly XDocument _xml;

    private ProjectFile(string relativePath, XDocument xml)
    {
        RelativePath = relativePath;
        _xml = xml;
    }

    public string RelativePath { get; }

    public string Name => Path.GetFileNameWithoutExtension(RelativePath);

    public string? TargetFramework => _xml.Descendants("TargetFramework").FirstOrDefault()?.Value;

    public IReadOnlyList<string> PackageReferences =>
        [.. _xml.Descendants("PackageReference").Select(reference => reference.Attribute("Include")!.Value)];

    /// <summary>Names of referenced projects, for example <c>Sillfall.Core</c>.</summary>
    public IReadOnlyList<string> ProjectReferences =>
        [.. _xml.Descendants("ProjectReference")
            .Select(reference => reference.Attribute("Include")!.Value.Replace('\\', '/'))
            .Select(path => Path.GetFileNameWithoutExtension(path))];

    public static ProjectFile Load(string relativePath) => new(relativePath, Repo.LoadXml(relativePath));

    public override string ToString() => Name;
}
