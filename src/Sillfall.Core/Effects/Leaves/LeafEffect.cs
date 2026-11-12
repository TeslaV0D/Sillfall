using System.Numerics;
using Sillfall.Core.Desktop;
using Sillfall.Core.Geometry;
using Sillfall.Core.Rendering;
using Sillfall.Core.Simulation;
using Sillfall.Core.Surfaces;

namespace Sillfall.Core.Effects.Leaves;

/// <summary>
/// Falling leaves (PLAN.md, section 4.3). Each leaf flutters: it rocks and turns over (pseudo-3D, its width
/// follows the cosine of the flip phase), drag holds it back and a lift that follows the flip makes it swing from
/// side to side. Near leaves land on window top edges and the floor and lie there slightly tilted; a surface
/// holds a limited number per 100 px, older ones slide off. A quick mouse movement blows the leaves near the
/// cursor away and lifts lying ones, and shaking a window throws its leaves off. After the leaves stop, the
/// lying ones fade. Struct-of-arrays with fixed capacity: no allocation per frame.
/// </summary>
public sealed class LeafEffect : IEffectSystem
{
    public const int MaxLeaves = 800;

    /// <summary>Most leaves lying on 100 px of a surface (PLAN.md: max. N per 100 px).</summary>
    public const int MaxPer100Pixels = 4;

    /// <summary>Share of the leaves that are near enough to land on windows. The others fall behind them.</summary>
    public const float NearShare = 0.7f;

    /// <summary>Cursor speed (pixels per second at 100 %) above which the cursor blows the leaves away.</summary>
    public const float GustThreshold = 600f;

    /// <summary>Reach of a cursor gust in pixels at 100 %.</summary>
    public const float GustRadius = 90f;

    /// <summary>Seconds lying leaves take to fade once the leaves were switched off.</summary>
    public const float FadeSeconds = 12f;

    /// <summary>Leaves per second per 1000 px of desktop width at intensity 1.</summary>
    private const float SpawnRatePer1000Pixels = 7f;

    private const float Gravity = 260f;

    /// <summary>Air drag per second: with gravity this gives a fall speed of about 100 px/s at 100 %.</summary>
    private const float Drag = 2.4f;

    /// <summary>Sideways acceleration of the flutter, pixels per second squared at 100 %.</summary>
    private const float Flutter = 240f;

    private const float MaxSpeed = 1400f;

    // Autumn palette: red, orange, golden yellow, ochre, brown, a late green.
    private static readonly Vector3[] s_palette =
    [
        new(0.78f, 0.20f, 0.10f),
        new(0.93f, 0.45f, 0.10f),
        new(0.95f, 0.70f, 0.15f),
        new(0.78f, 0.55f, 0.18f),
        new(0.55f, 0.30f, 0.12f),
        new(0.55f, 0.58f, 0.18f),
    ];

    private Pcg32 _random;
    private float _spawnDebt;
    private float _scale = 1f;
    private float _fade = 1f;

    private readonly float[] _x = new float[MaxLeaves];
    private readonly float[] _y = new float[MaxLeaves];
    private readonly float[] _vx = new float[MaxLeaves];
    private readonly float[] _vy = new float[MaxLeaves];
    private readonly float[] _z = new float[MaxLeaves];
    private readonly float[] _size = new float[MaxLeaves];
    private readonly float[] _angle = new float[MaxLeaves];
    private readonly float[] _spin = new float[MaxLeaves];
    private readonly float[] _flip = new float[MaxLeaves];
    private readonly float[] _flipSpeed = new float[MaxLeaves];
    private readonly byte[] _variant = new byte[MaxLeaves];
    private readonly byte[] _color = new byte[MaxLeaves];

    /// <summary>Row a falling leaf must pass before it can land: it never lands back on the edge it left.</summary>
    private readonly float[] _floor = new float[MaxLeaves];

    // Lying leaves: the surface (owner and edge), the position relative to its origin and when it landed.
    private readonly bool[] _resting = new bool[MaxLeaves];
    private readonly long[] _owner = new long[MaxLeaves];
    private readonly SurfaceEdge[] _edge = new SurfaceEdge[MaxLeaves];
    private readonly float[] _localX = new float[MaxLeaves];
    private readonly double[] _landed = new double[MaxLeaves];
    private int _count;

    public LeafEffect(ulong seed = 1)
    {
        _random = new Pcg32(seed);
    }

    public string Name => "leaves";

    public EffectSettings Settings { get; set; } = new();

    public bool IsActive => Settings.ClampedIntensity > 0f || _count > 0;

    public int Count => _count;

    public int FallingCount
    {
        get
        {
            int falling = 0;
            for (int i = 0; i < _count; i++)
            {
                falling += _resting[i] ? 0 : 1;
            }

            return falling;
        }
    }

    /// <summary>Leaves lying on the top edge of a window.</summary>
    public int LyingOn(long windowId) => CountLying(windowId, SurfaceEdge.Top);

    /// <summary>Leaves lying on the floor of a monitor (index into the monitor list).</summary>
    public int LyingOnFloor(int monitorIndex) => CountLying(monitorIndex, SurfaceEdge.Floor);

    /// <summary>
    /// Lays <paramref name="perHundredPixels"/> leaves on every 100 px of every visible window top edge and floor.
    /// For previews and the sandbox.
    /// </summary>
    public void Scatter(SimulationContext context, float perHundredPixels)
    {
        ArgumentNullException.ThrowIfNull(context);
        _scale = context.PixelScale;
        float count = Math.Clamp(perHundredPixels, 0f, MaxPer100Pixels);
        foreach (SurfaceSegment segment in context.Surfaces.Segments)
        {
            if (!segment.IsHorizontal)
            {
                continue;
            }

            int leaves = (int)(segment.Length / (100f * _scale) * count);
            for (int n = 0; n < leaves && _count < MaxLeaves; n++)
            {
                int i = _count++;
                Initialize(i, segment.Start + _random.NextFloat(0f, segment.Length), segment.Position, _random.NextFloat(0.4f, 1f));
                Rest(i, segment, context.Time);
            }
        }
    }

    public void Simulate(float dt, SimulationContext context)
    {
        float intensity = Settings.ClampedIntensity;
        _scale = context.PixelScale;
        Spawn(dt, intensity, context);

        // After the leaves stop, the lying ones fade out; a new start brings them back.
        _fade = intensity > 0f ? 1f : MathF.Max(0f, _fade - (dt / FadeSeconds));

        Gust(context);
        SimulateLeaves(dt, context);
    }

    public void OnWindowEvent(in WindowEvent windowEvent, SimulationContext context)
    {
        _scale = context.PixelScale;
        long id = windowEvent.WindowId;
        PixelRect bounds = windowEvent.Bounds;
        switch (windowEvent.Kind)
        {
            case WindowEventKind.Shaken:
                for (int i = 0; i < _count; i++)
                {
                    if (IsOn(i, id))
                    {
                        // Thrown up and off to both sides.
                        Detach(i, new Vector2(_random.NextFloat(-160f, 160f), _random.NextFloat(-260f, -140f)) * _scale, bounds.Top);
                    }
                }

                break;

            case WindowEventKind.Disappeared:
                for (int i = 0; i < _count; i++)
                {
                    if (IsOn(i, id))
                    {
                        Detach(i, new Vector2(_random.NextFloat(-30f, 30f), 0f) * _scale, bounds.Top);
                    }
                }

                break;

            case WindowEventKind.Resized:
                for (int i = 0; i < _count; i++)
                {
                    if (IsOn(i, id) && _localX[i] > bounds.Width)
                    {
                        Detach(i, Vector2.Zero, bounds.Top);
                    }
                }

                break;
        }
    }

    public void Emit(RenderFrame frame, SimulationContext context)
    {
        float brightness = Settings.ClampedBrightness;
        _scale = context.PixelScale;
        float threshold = 1f - NearShare;
        ReadOnlySpan<TrackedWindow> windows = context.Windows.Windows;
        for (int i = 0; i < _count; i++)
        {
            if (_resting[i] ? IsCovered(windows, i) : _z[i] < threshold && IsBehindAWindow(windows, windows.Length, _x[i], _y[i]))
            {
                continue;
            }

            // Turning over: the leaf shows its edge when the flip phase passes 90 degrees, its paler back after that.
            float turn = MathF.Cos(_flip[i]);
            float width = _resting[i] ? 1f : MathF.Max(0.18f, MathF.Abs(turn));
            float shade = (turn < 0f && !_resting[i] ? 0.78f : 1f) * (0.55f + (0.45f * _z[i]));
            Vector3 color = (Settings.Color ?? s_palette[_color[i]]) * shade;
            float alpha = MathF.Min(1f, brightness * (_resting[i] ? _fade : 1f));
            if (alpha <= 0.01f)
            {
                continue;
            }

            float length = _size[i] * _scale;
            frame.Add(new Sprite(
                new Vector2(_x[i], _y[i]),
                new Vector2(length, length * 0.55f * width),
                _angle[i],
                SpriteShape.Leaf,
                Sprite.Premultiply(Vector3.Min(color * brightness, Vector3.One), alpha))
            { Variant = _variant[i] });
        }
    }

    private static bool IsBehindAWindow(ReadOnlySpan<TrackedWindow> windows, int above, float x, float y)
    {
        int px = (int)MathF.Floor(x);
        int py = (int)MathF.Floor(y);
        for (int w = 0; w < above; w++)
        {
            if (windows[w].Bounds.Contains(px, py))
            {
                return true;
            }
        }

        return false;
    }

    private int CountLying(long owner, SurfaceEdge edge)
    {
        int lying = 0;
        for (int i = 0; i < _count; i++)
        {
            if (_resting[i] && _owner[i] == owner && _edge[i] == edge)
            {
                lying++;
            }
        }

        return lying;
    }

    private bool IsOn(int i, long windowId) => _resting[i] && _edge[i] == SurfaceEdge.Top && _owner[i] == windowId;

    /// <summary>A lying leaf is hidden where a window above its own covers it.</summary>
    private bool IsCovered(ReadOnlySpan<TrackedWindow> windows, int i)
    {
        int above = windows.Length;
        if (_edge[i] == SurfaceEdge.Top)
        {
            above = 0;
            while (above < windows.Length && windows[above].Id != _owner[i])
            {
                above++;
            }
        }

        return IsBehindAWindow(windows, above, _x[i], _y[i]);
    }

    private void Initialize(int i, float x, float y, float z)
    {
        _x[i] = x;
        _y[i] = y;
        _z[i] = z;
        _vx[i] = 0f;
        _vy[i] = 0f;
        _size[i] = (4.5f + (4.5f * z)) * _random.NextFloat(0.85f, 1.15f);
        _angle[i] = _random.NextFloat(0f, MathF.Tau);
        _spin[i] = _random.NextFloat(-1.5f, 1.5f);
        _flip[i] = _random.NextFloat(0f, MathF.Tau);
        _flipSpeed[i] = _random.NextFloat(2.2f, 4.5f) * (_random.NextFloat() < 0.5f ? -1f : 1f);
        _variant[i] = (byte)_random.NextInt(Sprite.LeafVariants);
        _color[i] = (byte)_random.NextInt(s_palette.Length);
        _floor[i] = float.MinValue;
        _resting[i] = false;
    }

    private void Spawn(float dt, float intensity, SimulationContext context)
    {
        PixelRect desktop = context.DesktopBounds;
        if (intensity <= 0f || desktop.IsEmpty)
        {
            return;
        }

        float windX = context.Wind.Current.X * _scale;
        float fallTime = desktop.Height / (100f * _scale);
        float margin = MathF.Min(MathF.Abs(windX) * fallTime * 0.5f, desktop.Width);
        float left = desktop.Left - (windX > 0 ? margin : 0f);
        float width = desktop.Width + margin;

        _spawnDebt += intensity * SpawnRatePer1000Pixels * (width / 1000f) * dt;
        while (_spawnDebt >= 1f && _count < MaxLeaves)
        {
            _spawnDebt -= 1f;
            int i = _count++;
            Initialize(i, left + (_random.NextFloat() * width), desktop.Top - (20f * _scale), _random.NextFloat());
            _vy[i] = 40f * _scale;
        }

        if (_count == MaxLeaves)
        {
            _spawnDebt = 0f;
        }
    }

    /// <summary>A quick mouse movement blows the leaves near the cursor along and lifts lying ones.</summary>
    private void Gust(SimulationContext context)
    {
        Vector2 velocity = context.CursorVelocity;
        float speed = velocity.Length();
        if (speed <= GustThreshold * _scale)
        {
            return;
        }

        float radius = GustRadius * _scale;
        Vector2 cursor = context.Cursor;
        for (int i = 0; i < _count; i++)
        {
            Vector2 offset = new Vector2(_x[i], _y[i]) - cursor;
            float distance = offset.Length();
            if (distance > radius)
            {
                continue;
            }

            float strength = 1f - (distance / radius);
            Vector2 push = velocity * (0.45f * strength);
            if (_resting[i])
            {
                Detach(i, push + (new Vector2(0f, -120f * strength) * _scale), _y[i]);
            }
            else
            {
                _vx[i] += push.X * 0.1f;
                _vy[i] += push.Y * 0.1f;
                _flipSpeed[i] += MathF.CopySign(2f * strength, _flipSpeed[i]);
            }
        }
    }

    private void SimulateLeaves(float dt, SimulationContext context)
    {
        float threshold = 1f - NearShare;
        Vector2 wind = context.Wind.Current * _scale;
        float bottom = context.DesktopBounds.Bottom + (20f * _scale);
        float maxSpeed = MaxSpeed * _scale;
        int i = 0;
        while (i < _count)
        {
            if (_resting[i])
            {
                if (!FollowSurface(i, context))
                {
                    Detach(i, Vector2.Zero, _y[i]);
                }

                // Faded away after the leaves stopped.
                if (_resting[i] && _fade <= 0f)
                {
                    Remove(i);
                    continue;
                }

                i++;
                continue;
            }

            // Flutter: the flip turns the leaf, the lift follows it and swings the leaf from side to side.
            _flip[i] += _flipSpeed[i] * dt;
            float swing = MathF.Sin(_flip[i]);
            float flat = MathF.Cos(_flip[i]);
            _vx[i] += ((wind.X - _vx[i]) * Drag * dt) + (swing * Flutter * _scale * dt);
            _vy[i] += (((wind.Y - _vy[i]) * Drag) + (Gravity * _scale * (1f - (0.55f * flat * flat)))) * dt;
            float speed = MathF.Sqrt((_vx[i] * _vx[i]) + (_vy[i] * _vy[i]));
            if (speed > maxSpeed)
            {
                _vx[i] *= maxSpeed / speed;
                _vy[i] *= maxSpeed / speed;
            }

            // Rocking around a slowly turning base angle.
            _angle[i] += ((_spin[i] * 0.3f) + (swing * 1.4f)) * dt;

            float nextX = _x[i] + (_vx[i] * dt);
            float nextY = _y[i] + (_vy[i] * dt);
            float from = MathF.Max(_y[i], _floor[i]);
            if (_z[i] >= threshold && _vy[i] > 0f && nextY > from
                && context.SurfaceIndex.TryFindLanding(nextX, from, nextY, out SurfaceHit hit))
            {
                _x[i] = nextX;
                Rest(i, context.Surfaces.Segments[hit.Segment], context.Time);
                i++;
                continue;
            }

            _x[i] = nextX;
            _y[i] = nextY;
            if (nextY > bottom)
            {
                Remove(i);
                continue;
            }

            i++;
        }
    }

    /// <summary>Puts a leaf down on a surface, slightly tilted, and makes room if the spot is full.</summary>
    private void Rest(int i, in SurfaceSegment segment, double time)
    {
        _resting[i] = true;
        _owner[i] = segment.Key.OwnerId;
        _edge[i] = segment.Key.Edge;
        _localX[i] = _x[i] - segment.Origin;
        _landed[i] = time;
        _vx[i] = 0f;
        _vy[i] = 0f;

        // Lying flat: along the edge, tilted a little, the leaf's centre just above the surface.
        _angle[i] = (_random.NextFloat() < 0.5f ? 0f : MathF.PI) + _random.NextFloat(-0.3f, 0.3f);
        _flip[i] = 0f;
        _y[i] = segment.Position - (_size[i] * _scale * 0.45f);

        // Too many on this 100 px stretch of the surface: the oldest one there slides off.
        float bucketWidth = 100f * _scale;
        int bucket = (int)MathF.Floor(_localX[i] / bucketWidth);
        int nearby = 0;
        int oldest = -1;
        for (int j = 0; j < _count; j++)
        {
            if (j == i || !_resting[j] || _owner[j] != _owner[i] || _edge[j] != _edge[i] || (int)MathF.Floor(_localX[j] / bucketWidth) != bucket)
            {
                continue;
            }

            nearby++;
            if (oldest < 0 || _landed[j] < _landed[oldest])
            {
                oldest = j;
            }
        }

        if (nearby >= MaxPer100Pixels && oldest >= 0)
        {
            float away = _localX[oldest] < _localX[i] ? -1f : 1f;
            Detach(oldest, new Vector2(away * 60f, -30f) * _scale, segment.Position);
        }
    }

    /// <summary>Keeps a lying leaf on its surface as the window moves. False when the surface is gone.</summary>
    private bool FollowSurface(int i, SimulationContext context)
    {
        if (_edge[i] == SurfaceEdge.Top)
        {
            int index = context.Windows.IndexOf(_owner[i]);
            if (index < 0)
            {
                return false;
            }

            PixelRect bounds = context.Windows[index].Bounds;
            if (_localX[i] < 0f || _localX[i] > bounds.Width)
            {
                return false;
            }

            _x[i] = bounds.Left + _localX[i];
            _y[i] = bounds.Top - (_size[i] * _scale * 0.45f);
            return true;
        }

        return _owner[i] >= 0 && _owner[i] < context.Monitors.Count;
    }

    /// <summary>Lets a lying leaf go with a push. It cannot land again before it passed <paramref name="floor"/>.</summary>
    private void Detach(int i, Vector2 push, float floor)
    {
        _resting[i] = false;
        _vx[i] = push.X;
        _vy[i] = push.Y;
        _floor[i] = floor + 1f;
        _flipSpeed[i] = MathF.CopySign(_random.NextFloat(2.5f, 5f), _flipSpeed[i] == 0f ? 1f : _flipSpeed[i]);
    }

    private void Remove(int i)
    {
        int last = --_count;
        _x[i] = _x[last];
        _y[i] = _y[last];
        _vx[i] = _vx[last];
        _vy[i] = _vy[last];
        _z[i] = _z[last];
        _size[i] = _size[last];
        _angle[i] = _angle[last];
        _spin[i] = _spin[last];
        _flip[i] = _flip[last];
        _flipSpeed[i] = _flipSpeed[last];
        _variant[i] = _variant[last];
        _color[i] = _color[last];
        _floor[i] = _floor[last];
        _resting[i] = _resting[last];
        _owner[i] = _owner[last];
        _edge[i] = _edge[last];
        _localX[i] = _localX[last];
        _landed[i] = _landed[last];
    }
}
