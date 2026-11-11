using System.Text.Json;
using System.Text.Json.Serialization;

namespace Weatherly.Core.Previews;

/// <summary>
/// A reproducible scene for previews and golden images: one monitor, some windows, effect settings,
/// wind, scripted window moves and the times at which frames are captured. Stored as JSON.
/// </summary>
public sealed record PreviewScene
{
    public string Name { get; set; } = "scene";

    public uint Seed { get; set; } = 1;

    public PreviewMonitor Monitor { get; set; } = new();

    /// <summary>Windows in z-order, topmost first.</summary>
    public IReadOnlyList<PreviewWindow> Windows { get; set; } = [];

    public PreviewEffects Effects { get; set; } = new();

    public PreviewWind Wind { get; set; } = new();

    /// <summary>Draws the surface debug layer on top.</summary>
    public bool ShowSurfaces { get; set; }

    /// <summary>Simulated frames per second. Frames are spaced evenly, independent of the real time.</summary>
    public int FrameRate { get; set; } = 60;

    /// <summary>Simulated times in seconds at which a frame is captured.</summary>
    public IReadOnlyList<double> Captures { get; set; } = [];

    /// <summary>Window moves at given times, for example to shake a window or close it.</summary>
    public IReadOnlyList<PreviewEvent> Events { get; set; } = [];

    /// <summary>Length of the scene: the last capture.</summary>
    [JsonIgnore]
    public double Duration => Captures.Count == 0 ? 0 : Captures.Max();

    public static PreviewScene Parse(string json) =>
        JsonSerializer.Deserialize(json, PreviewSceneJsonContext.Default.PreviewScene)
        ?? throw new JsonException("The scene file is empty.");

    public string ToJson() => JsonSerializer.Serialize(this, PreviewSceneJsonContext.Default.PreviewScene);
}

public sealed record PreviewMonitor
{
    public int Width { get; set; } = 960;

    public int Height { get; set; } = 540;

    /// <summary>Height of a taskbar at the bottom. The floor lies on top of it.</summary>
    public int Taskbar { get; set; } = 40;

    /// <summary>DPI scale, 1 for 100 %.</summary>
    public float Scale { get; set; } = 1f;
}

public sealed record PreviewWindow
{
    public long Id { get; set; }

    public int X { get; set; }

    public int Y { get; set; }

    public int Width { get; set; }

    public int Height { get; set; }

    public bool Maximized { get; set; }
}

public sealed record PreviewEffects
{
    /// <summary>Calibration drops, 0 to 1.</summary>
    public float TestDrops { get; set; }

    /// <summary>Rain intensity, 0 to 1.</summary>
    public float Rain { get; set; }

    /// <summary>Share of rain drops that interact with windows, 0 to 1.</summary>
    public float RainDepth { get; set; } = 0.5f;

    /// <summary>Snow intensity, 0 to 1.</summary>
    public float Snow { get; set; }

    /// <summary>Snow already lying on every window and the floor at the start, in pixels at 100 %.</summary>
    public float SnowFill { get; set; }
}

public sealed record PreviewWind
{
    /// <summary>Base wind in pixels per second, positive to the right.</summary>
    public float X { get; set; }

    public float Y { get; set; }

    public float Gustiness { get; set; } = 0.5f;
}

/// <summary>At <see cref="At"/> seconds, window <see cref="Window"/> moves to (<see cref="X"/>, <see cref="Y"/>), or disappears.</summary>
public sealed record PreviewEvent
{
    public double At { get; set; }

    public long Window { get; set; }

    public int X { get; set; }

    public int Y { get; set; }

    /// <summary>Closes the window instead of moving it.</summary>
    public bool Close { get; set; }
}

[JsonSourceGenerationOptions(
    PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase,
    WriteIndented = true,
    ReadCommentHandling = JsonCommentHandling.Skip,
    AllowTrailingCommas = true,
    UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow)]
[JsonSerializable(typeof(PreviewScene))]
internal sealed partial class PreviewSceneJsonContext : JsonSerializerContext;
