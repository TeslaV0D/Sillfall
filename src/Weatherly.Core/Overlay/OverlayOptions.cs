namespace Weatherly.Core.Overlay;

/// <summary>What an overlay draws.</summary>
public enum OverlayContent
{
    /// <summary>Nothing. The overlay is fully transparent.</summary>
    None,

    /// <summary>Calibration pattern that makes monitor bounds, DPI scaling and alpha blending visible.</summary>
    TestPattern,
}

/// <summary>User-facing switches of the overlay.</summary>
public sealed record OverlayOptions
{
    /// <summary>Whether overlays exist at all. When off, no overlay window and no swap chain is alive.</summary>
    public bool Enabled { get; init; } = true;

    /// <summary>Hides the overlay from screenshots and screen recordings.</summary>
    public bool ExcludeFromCapture { get; init; }

    public OverlayContent Content { get; init; } = OverlayContent.None;

    /// <summary>Debug layer: draws every detected surface (top edges, sides, floors) as a colored line.</summary>
    public bool ShowSurfaces { get; init; }

    /// <summary>Intensity of the calibration drops (milestone M3), 0 to 1. 0 switches them off.</summary>
    public float TestDrops { get; init; }

    /// <summary>Rain (milestone M4).</summary>
    public Effects.EffectSettings Rain { get; init; } = new();

    /// <summary>Share of rain drops that interact with windows, 0 to 1 (PLAN.md: "Depth").</summary>
    public float RainDepth { get; init; } = 0.5f;

    /// <summary>Snow (milestone M5).</summary>
    public Effects.EffectSettings Snow { get; init; } = new();

    /// <summary>Frost from the weather sync: lying snow stays after it stops snowing (see <c>SnowEffect.Frozen</c>).</summary>
    public bool SnowStays { get; init; }

    /// <summary>Leaves (milestone M6).</summary>
    public Effects.EffectSettings Leaves { get; init; } = new();

    /// <summary>Fog (milestone M7).</summary>
    public Effects.EffectSettings Fog { get; init; } = new();

    /// <summary>Sunlight (milestone M7).</summary>
    public Effects.EffectSettings Sun { get; init; } = new();

    /// <summary>Grass (milestone M8).</summary>
    public Effects.EffectSettings Grass { get; init; } = new();

    /// <summary>Upper corner the sun stands above.</summary>
    public Effects.Sun.SunSide SunSide { get; init; } = Effects.Sun.SunSide.Right;

    /// <summary>Steady wind in pixels per second at 100 % (all effects read it).</summary>
    public System.Numerics.Vector2 Wind { get; init; }

    /// <summary>Strength of the gusts relative to the steady wind, 0 to 1.</summary>
    public float Gustiness { get; init; } = 0.5f;

    /// <summary>Highest frame rate of the render loop; 0 for the display's own rate (PLAN.md: Smoothness).</summary>
    public int FrameRateCap { get; init; }

    /// <summary>Executable names ("obs64.exe") whose windows are kept clear of weather (app rules).</summary>
    public IReadOnlyList<string> HideOverProcesses { get; init; } = [];

    /// <summary>Per-monitor settings by device name. Monitors without an entry show the weather at full opacity.</summary>
    public IReadOnlyList<Settings.DisplayConfig> Displays { get; init; } = [];

    /// <summary>Settings of one monitor, or the defaults.</summary>
    public Settings.DisplayConfig Display(string deviceName)
    {
        foreach (Settings.DisplayConfig display in Displays)
        {
            if (string.Equals(display.DeviceName, deviceName, StringComparison.OrdinalIgnoreCase))
            {
                return display;
            }
        }

        return new Settings.DisplayConfig { DeviceName = deviceName };
    }
}
