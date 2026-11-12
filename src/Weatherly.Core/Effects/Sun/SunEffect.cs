using System.Numerics;
using Weatherly.Core.Geometry;
using Weatherly.Core.Rendering;
using Weatherly.Core.Simulation;

namespace Weatherly.Core.Effects.Sun;

/// <summary>Which upper corner of the desktop the sun shines from.</summary>
public enum SunSide
{
    Right,
    Left,
}

/// <summary>
/// Sunlight (PLAN.md, section 4.5): the simulation places the sun above one upper corner of the desktop, lets it
/// wander slowly and fades it in and out; the renderer draws the god rays the windows block and the glow. About a
/// hundred dust particles float in the light. Allocation-free.
/// </summary>
public sealed class SunEffect : IEffectSystem
{
    public const int DustCount = 100;

    /// <summary>Seconds the light takes to come or go most of the way.</summary>
    public const float TransitionSeconds = 3f;

    private static readonly Vector3 s_sunColor = new(1f, 0.86f, 0.58f);
    private static readonly Vector3 s_dustColor = new(1f, 0.95f, 0.8f);

    private readonly uint _seed;
    private readonly float[] _dustX = new float[DustCount];
    private readonly float[] _dustY = new float[DustCount];
    private readonly float[] _dustPhase = new float[DustCount];
    private Pcg32 _random;
    private bool _dustPlaced;
    private float _time;

    public SunEffect(uint seed = 1)
    {
        _seed = seed;
        _random = new Pcg32(seed);
    }

    public string Name => "sun";

    public EffectSettings Settings { get; set; } = new();

    public SunSide Side { get; set; } = SunSide.Right;

    /// <summary>Brightness right now, following the intensity setting slowly.</summary>
    public float Strength { get; private set; }

    /// <summary>Where the sun stands in desktop pixels (valid while the desktop is known).</summary>
    public Vector2 Position { get; private set; }

    public bool IsActive => Settings.ClampedIntensity > 0f || Strength > 0.002f;

    /// <summary>Jumps to the target brightness at once instead of fading in. For previews.</summary>
    public void SkipTransition() => Strength = Settings.ClampedIntensity;

    public void Simulate(float dt, SimulationContext context)
    {
        float target = Settings.ClampedIntensity;
        Strength += (target - Strength) * (1f - MathF.Exp(-dt / (TransitionSeconds / 3f)));
        if (target == 0f && Strength < 0.002f)
        {
            Strength = 0f;
        }

        _time += dt;
        PixelRect desktop = context.DesktopBounds;
        if (desktop.IsEmpty)
        {
            return;
        }

        // Above the chosen corner, wandering a few percent of the width over minutes.
        float wander = 0.05f * Noise1D.Sample(_time * 0.01f, _seed);
        float x = Side == SunSide.Right ? 0.86f + wander : 0.14f + wander;
        Position = new Vector2(desktop.Left + (x * desktop.Width), desktop.Top - (0.08f * desktop.Height));

        SimulateDust(dt, context, desktop);
    }

    public void OnWindowEvent(in Desktop.WindowEvent windowEvent, SimulationContext context)
    {
    }

    public void Emit(RenderFrame frame, SimulationContext context)
    {
        if (Strength <= 0f || context.DesktopBounds.IsEmpty)
        {
            return;
        }

        float brightness = Settings.ClampedBrightness;
        float scale = context.PixelScale;
        frame.Sun = new SunLayer(MathF.Min(1f, Strength * brightness), Position, Settings.Color ?? s_sunColor, scale);

        // Dust glitters in the light: brighter near the sun, twinkling slowly.
        float reach = context.DesktopBounds.Height * 1.2f;
        for (int i = 0; i < DustCount; i++)
        {
            var position = new Vector2(_dustX[i], _dustY[i]);
            float nearness = MathF.Max(0f, 1f - (Vector2.Distance(position, Position) / reach));
            float twinkle = 0.5f + (0.5f * MathF.Sin((_time * 0.9f) + _dustPhase[i]));
            float alpha = Strength * brightness * nearness * twinkle * 0.55f;
            if (alpha < 0.02f)
            {
                continue;
            }

            float radius = (0.7f + (0.6f * twinkle)) * scale;
            frame.Add(new Sprite(position, new Vector2(radius, radius), 0f, SpriteShape.Disc, Sprite.Premultiply(s_dustColor, MathF.Min(1f, alpha))));
        }
    }

    private void SimulateDust(float dt, SimulationContext context, PixelRect desktop)
    {
        if (!_dustPlaced)
        {
            _dustPlaced = true;
            for (int i = 0; i < DustCount; i++)
            {
                PlaceDust(i, desktop);
            }
        }

        // Floating: a slow drift with the wind and a gentle wobble.
        Vector2 drift = context.Wind.Current * 0.08f * context.PixelScale;
        for (int i = 0; i < DustCount; i++)
        {
            float wobble = MathF.Sin((_time * 0.7f) + (_dustPhase[i] * 3f)) * 4f * context.PixelScale;
            _dustX[i] += (drift.X + wobble) * dt;
            _dustY[i] += (drift.Y + (3f * context.PixelScale)) * dt;
            if (!desktop.Contains((int)_dustX[i], (int)_dustY[i]))
            {
                PlaceDust(i, desktop);
            }
        }
    }

    /// <summary>Somewhere in the cone of light below the sun.</summary>
    private void PlaceDust(int i, PixelRect desktop)
    {
        float along = _random.NextFloat(0.1f, 1f);
        float spread = _random.NextFloat(-0.35f, 0.35f) * along;
        float toward = Side == SunSide.Right ? -1f : 1f;
        _dustX[i] = Math.Clamp(Position.X + (((toward * 0.4f * along) + spread) * desktop.Width), desktop.Left, desktop.Right - 1);
        _dustY[i] = Math.Clamp(desktop.Top + (along * desktop.Height), desktop.Top, desktop.Bottom - 1);
        _dustPhase[i] = _random.NextFloat(0f, MathF.Tau);
    }
}
