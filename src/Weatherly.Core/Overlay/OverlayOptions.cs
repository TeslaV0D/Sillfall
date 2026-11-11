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
}
