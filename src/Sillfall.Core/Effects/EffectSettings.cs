using System.Numerics;

namespace Sillfall.Core.Effects;

/// <summary>
/// Settings every effect shares (PLAN.md, section 0: intensity, own color, brightness). Fixed/random mode
/// and the per-effect options of the settings window follow in milestone M9.
/// </summary>
public sealed record EffectSettings
{
    /// <summary>0 switches the effect off, 1 is the strongest.</summary>
    public float Intensity { get; init; }

    /// <summary>Own color in linear RGB, 0 to 1. <see langword="null"/> uses the effect's natural colors.</summary>
    public Vector3? Color { get; init; }

    /// <summary>Multiplies the effect's opacity, 0.25 to 2. 1 is the default look.</summary>
    public float Brightness { get; init; } = 1f;

    public float ClampedIntensity => Math.Clamp(Intensity, 0f, 1f);

    public float ClampedBrightness => Math.Clamp(Brightness, 0.25f, 2f);
}
