using System.Numerics;
using Sillfall.Core.Desktop;
using Sillfall.Core.Rendering;
using Sillfall.Core.Simulation;
using Sillfall.Core.Surfaces;

namespace Sillfall.Core.Effects;

/// <summary>
/// Calibration effect of milestone M3: round drops fall with gravity and wind, land on window top
/// edges and floors, rest there for a moment and fade out. Resting drops travel with their window,
/// and fall off when the window is shaken or closed.
/// <para>
/// It exercises every part of the engine (fixed step, surfaces, window events, sprites, idle stop)
/// before the real effects exist. Struct-of-arrays storage with a fixed capacity: no allocation per frame.
/// </para>
/// </summary>
public sealed class TestDropsEffect : IEffectSystem
{
    private const float Gravity = 900f;
    private const float TerminalVelocity = 650f;
    private const float Radius = 3f;
    private const float RestSeconds = 1.5f;
    private const float FadeSeconds = 0.5f;

    /// <summary>Drops per second per 1000 px of desktop width at intensity 1.</summary>
    private const float SpawnRatePer1000Pixels = 120f;

    private static readonly Vector3 s_fallingColor = new(0.55f, 0.75f, 1f);
    private static readonly Vector3 s_restingColor = new(0.85f, 0.92f, 1f);

    private readonly float[] _x;
    private readonly float[] _y;
    private readonly float[] _vx;
    private readonly float[] _vy;
    private readonly float[] _rest;
    private readonly long[] _owner;
    private readonly bool[] _resting;
    private Pcg32 _random;
    private float _spawnDebt;
    private int _count;

    public TestDropsEffect(int capacity = 4096, ulong seed = 1)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(capacity);
        _x = new float[capacity];
        _y = new float[capacity];
        _vx = new float[capacity];
        _vy = new float[capacity];
        _rest = new float[capacity];
        _owner = new long[capacity];
        _resting = new bool[capacity];
        _random = new Pcg32(seed);
    }

    public string Name => "test-drops";

    /// <summary>0 to 1. 0 stops spawning, the remaining drops still finish.</summary>
    public float Intensity { get; set; }

    public bool IsActive => Intensity > 0f || _count > 0;

    public int Count => _count;

    public int RestingCount
    {
        get
        {
            int resting = 0;
            for (int i = 0; i < _count; i++)
            {
                if (_resting[i])
                {
                    resting++;
                }
            }

            return resting;
        }
    }

    public int Capacity => _x.Length;

    public void Simulate(float dt, SimulationContext context)
    {
        Spawn(dt, context);

        float windX = context.Wind.Current.X;
        float bottom = context.DesktopBounds.Bottom + 50f;
        int i = 0;
        while (i < _count)
        {
            if (_resting[i])
            {
                _rest[i] -= dt;
                if (_rest[i] <= 0f)
                {
                    RemoveAt(i);
                    continue;
                }

                i++;
                continue;
            }

            _vy[i] = MathF.Min(_vy[i] + (Gravity * dt), TerminalVelocity);
            _vx[i] += (windX - _vx[i]) * MathF.Min(1f, 2f * dt);

            float nextX = _x[i] + (_vx[i] * dt);
            float nextY = _y[i] + (_vy[i] * dt);

            // The bottom of the drop touches the surface line when its center reaches row - radius.
            if (context.SurfaceIndex.TryFindLanding(nextX, _y[i] + Radius, nextY + Radius, out SurfaceHit hit))
            {
                SurfaceSegment segment = context.Surfaces.Segments[hit.Segment];
                _x[i] = nextX;
                _y[i] = hit.Row - Radius;
                _vx[i] = 0f;
                _vy[i] = 0f;
                _resting[i] = true;
                _rest[i] = RestSeconds;
                _owner[i] = segment.Key.Edge == SurfaceEdge.Top ? segment.Key.OwnerId : 0;
                i++;
                continue;
            }

            _x[i] = nextX;
            _y[i] = nextY;
            if (_y[i] > bottom)
            {
                RemoveAt(i);
                continue;
            }

            i++;
        }
    }

    public void OnWindowEvent(in WindowEvent windowEvent, SimulationContext context)
    {
        long id = windowEvent.WindowId;
        switch (windowEvent.Kind)
        {
            // Resting drops travel with their window: their position is relative to it.
            case WindowEventKind.Moved:
                float dx = windowEvent.Bounds.Left - windowEvent.PreviousBounds.Left;
                float dy = windowEvent.Bounds.Top - windowEvent.PreviousBounds.Top;
                for (int i = 0; i < _count; i++)
                {
                    if (_resting[i] && _owner[i] == id)
                    {
                        _x[i] += dx;
                        _y[i] += dy;
                    }
                }

                break;

            // Shaken off or the window is gone: the drops fall again.
            case WindowEventKind.Shaken:
            case WindowEventKind.Disappeared:
                float kick = windowEvent.Kind == WindowEventKind.Shaken ? -180f : 0f;
                for (int i = 0; i < _count; i++)
                {
                    if (_resting[i] && _owner[i] == id)
                    {
                        _resting[i] = false;
                        _owner[i] = 0;
                        _vx[i] = windowEvent.Velocity.X * 0.2f;
                        _vy[i] = kick;
                        _y[i] += 2f; // past the edge, so the drop does not land on it again at once
                    }
                }

                break;
        }
    }

    public void Emit(RenderFrame frame, SimulationContext context)
    {
        for (int i = 0; i < _count; i++)
        {
            Vector4 color;
            if (_resting[i])
            {
                float alpha = MathF.Min(1f, _rest[i] / FadeSeconds) * 0.9f;
                color = Sprite.Premultiply(s_restingColor, alpha);
            }
            else
            {
                color = Sprite.Premultiply(s_fallingColor, 0.8f);
            }

            frame.Add(new Sprite(new Vector2(_x[i], _y[i]), new Vector2(Radius, Radius), 0f, SpriteShape.Disc, color));
        }
    }

    private void Spawn(float dt, SimulationContext context)
    {
        if (Intensity <= 0f || context.DesktopBounds.IsEmpty)
        {
            return;
        }

        _spawnDebt += Intensity * SpawnRatePer1000Pixels * (context.DesktopBounds.Width / 1000f) * dt;
        while (_spawnDebt >= 1f && _count < _x.Length)
        {
            _spawnDebt -= 1f;
            int i = _count++;
            _x[i] = context.DesktopBounds.Left + (_random.NextFloat() * context.DesktopBounds.Width);
            _y[i] = context.DesktopBounds.Top - Radius - (_random.NextFloat() * 20f);
            _vx[i] = context.Wind.Current.X;
            _vy[i] = _random.NextFloat(100f, 250f);
            _resting[i] = false;
            _rest[i] = 0f;
            _owner[i] = 0;
        }

        // At capacity the debt must not pile up into a burst later.
        if (_count == _x.Length)
        {
            _spawnDebt = 0f;
        }
    }

    /// <summary>Removes a drop by moving the last one into its slot. Order does not matter.</summary>
    private void RemoveAt(int i)
    {
        int last = --_count;
        _x[i] = _x[last];
        _y[i] = _y[last];
        _vx[i] = _vx[last];
        _vy[i] = _vy[last];
        _rest[i] = _rest[last];
        _owner[i] = _owner[last];
        _resting[i] = _resting[last];
    }
}
