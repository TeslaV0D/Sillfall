using System.Numerics;
using Sillfall.Core.Rendering;
using Sillfall.Core.Simulation;

namespace Sillfall.Core.Effects.Fog;

/// <summary>
/// Fog (PLAN.md, section 4.4). The simulation only drives a few numbers: how thick the fog is (it rolls in and
/// out slowly instead of switching), how far it drifted with the wind, and a slow phase that changes its shape.
/// The renderer draws it as noise in a full-screen pass. Allocation-free.
/// </summary>
public sealed class FogEffect : IEffectSystem
{
    /// <summary>Most coverage anywhere (PLAN.md: about 0.35), so text stays readable.</summary>
    public const float MaxAlpha = 0.35f;

    /// <summary>Seconds the fog takes to roll in or out most of the way.</summary>
    public const float TransitionSeconds = 4f;

    /// <summary>Drift without any wind, pixels per second at 100 %: fog is never quite still.</summary>
    private const float BaseDrift = 6f;

    private static readonly Vector3 s_fogColor = new(0.86f, 0.89f, 0.93f);

    private readonly uint _seed;
    private Vector2 _scroll;
    private float _phase;
    private float _time;

    public FogEffect(uint seed = 1)
    {
        _seed = seed;
    }

    public string Name => "fog";

    public EffectSettings Settings { get; set; } = new();

    /// <summary>Thickness right now, following the intensity setting slowly.</summary>
    public float Density { get; private set; }

    public bool IsActive => Settings.ClampedIntensity > 0f || Density > 0.002f;

    /// <summary>Jumps to the target thickness at once instead of rolling in. For previews.</summary>
    public void SkipTransition() => Density = Settings.ClampedIntensity;

    public void Simulate(float dt, SimulationContext context)
    {
        float target = Settings.ClampedIntensity;
        float blend = 1f - MathF.Exp(-dt / (TransitionSeconds / 3f));
        Density += (target - Density) * blend;
        if (target == 0f && Density < 0.002f)
        {
            Density = 0f;
        }

        float scale = context.PixelScale;
        _time += dt;
        _phase += dt * 0.03f;
        _scroll += ((context.Wind.Current * 0.6f) + new Vector2(BaseDrift, 0f)) * scale * dt;
    }

    public void OnWindowEvent(in Desktop.WindowEvent windowEvent, SimulationContext context)
    {
    }

    public void Emit(RenderFrame frame, SimulationContext context)
    {
        if (Density <= 0f)
        {
            return;
        }

        // The fog breathes: it thickens and thins a little over tens of seconds.
        float breath = 0.82f + (0.18f * Noise1D.Fractal(_time * 0.04f, 2, _seed));
        frame.Fog = new FogLayer(
            Density * breath,
            _scroll,
            _phase,
            Settings.Color ?? s_fogColor,
            MathF.Min(1f, MaxAlpha * Settings.ClampedBrightness),
            context.PixelScale);
    }
}
