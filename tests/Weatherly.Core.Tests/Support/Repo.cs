using System.Xml.Linq;

namespace Weatherly.Core.Tests.Support;

/// <summary>Read-only access to the repository working tree for invariant tests.</summary>
internal static class Repo
{
    private const string SolutionFileName = "Weatherly.slnx";

    private static readonly string[] s_ignoredDirectories = ["bin", "obj", ".git", ".vs", ".idea", "artifacts", "TestResults"];

    /// <summary>Absolute path of the repository root (the directory that holds the solution file).</summary>
    public static string Root { get; } = FindRoot();

    /// <summary>Absolute path for a repo-relative path written with forward slashes.</summary>
    public static string PathOf(string relativePath) =>
        Path.Combine(Root, relativePath.Replace('/', Path.DirectorySeparatorChar));

    /// <summary>Repo-relative path with forward slashes.</summary>
    public static string Relative(string absolutePath) =>
        Path.GetRelativePath(Root, absolutePath).Replace(Path.DirectorySeparatorChar, '/');

    /// <summary>
    /// All files below <paramref name="relativeDirectory"/> as repo-relative paths, skipping build output and tool folders.
    /// </summary>
    public static IEnumerable<string> Files(string relativeDirectory = "")
    {
        string start = PathOf(relativeDirectory);
        if (!Directory.Exists(start))
        {
            yield break;
        }

        var pending = new Stack<string>();
        pending.Push(start);
        while (pending.Count > 0)
        {
            string directory = pending.Pop();
            foreach (string file in Directory.EnumerateFiles(directory))
            {
                yield return Relative(file);
            }

            foreach (string child in Directory.EnumerateDirectories(directory))
            {
                if (!s_ignoredDirectories.Contains(Path.GetFileName(child), StringComparer.OrdinalIgnoreCase))
                {
                    pending.Push(child);
                }
            }
        }
    }

    /// <summary>Hand-written source and interop files of the product code (<c>src/</c> and <c>tools/</c>).</summary>
    public static IEnumerable<string> ProductSourceFiles()
    {
        string[] extensions = [".cs", ".xaml", ".txt", ".json", ".hlsl", ".csproj", ".props", ".targets"];
        return Files("src").Concat(Files("tools"))
            .Where(file => extensions.Contains(Path.GetExtension(file), StringComparer.OrdinalIgnoreCase));
    }

    public static string ReadText(string relativePath) => File.ReadAllText(PathOf(relativePath));

    public static XDocument LoadXml(string relativePath) => XDocument.Load(PathOf(relativePath));

    /// <summary>Repo-relative paths of all project files in the solution.</summary>
    public static IReadOnlyList<string> SolutionProjects() =>
        [.. LoadXml(SolutionFileName).Descendants("Project").Select(project => project.Attribute("Path")!.Value.Replace('\\', '/'))];

    private static string FindRoot()
    {
        for (DirectoryInfo? directory = new(AppContext.BaseDirectory); directory is not null; directory = directory.Parent)
        {
            if (File.Exists(Path.Combine(directory.FullName, SolutionFileName)))
            {
                return directory.FullName;
            }
        }

        throw new InvalidOperationException(
            $"Could not find {SolutionFileName} above {AppContext.BaseDirectory}. Invariant tests must run from inside the repository.");
    }
}
