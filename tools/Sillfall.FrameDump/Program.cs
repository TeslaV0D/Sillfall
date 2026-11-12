using Sillfall.Core;
using Sillfall.Core.Previews;
using Sillfall.FrameDump;
using Sillfall.Render;

// Headless renderer: plays preview scenes (JSON) with the simulation engine and renders the captured
// frames with WARP into PNG files, plus one contact sheet per scene.
//
//   Sillfall.FrameDump <scene.json | directory> [--out <directory>]
//
// Without arguments it renders every scene in the Scenes folder next to the executable.

string output = Path.Combine(Environment.CurrentDirectory, "framedump");
var inputs = new List<string>();
for (int i = 0; i < args.Length; i++)
{
    if (args[i] == "--out" && i + 1 < args.Length)
    {
        output = Path.GetFullPath(args[++i]);
    }
    else if (args[i] is "--help" or "-h")
    {
        Console.WriteLine($"{AppInfo.Name} FrameDump {AppInfo.DisplayVersion}");
        Console.WriteLine("Usage: Sillfall.FrameDump <scene.json | directory> ... [--out <directory>]");
        return 0;
    }
    else
    {
        inputs.Add(args[i]);
    }
}

if (inputs.Count == 0)
{
    inputs.Add(Path.Combine(AppContext.BaseDirectory, "Scenes"));
}

var scenes = new List<string>();
foreach (string input in inputs)
{
    if (Directory.Exists(input))
    {
        scenes.AddRange(Directory.GetFiles(input, "*.json").Order(StringComparer.Ordinal));
    }
    else if (File.Exists(input))
    {
        scenes.Add(input);
    }
    else
    {
        Console.Error.WriteLine($"Not found: {input}");
        return 2;
    }
}

using var renderer = new HeadlessRenderer();
foreach (string path in scenes)
{
    PreviewScene scene = PreviewScene.Parse(File.ReadAllText(path));
    IReadOnlyList<string> written = FrameDumper.Dump(scene, output, renderer);
    Console.WriteLine($"{scene.Name}: {written.Count} file(s) in {output}");
}

return 0;
