using System.Numerics;
using Weatherly.Core.Desktop;
using Weatherly.Core.Geometry;
using Weatherly.Core.Rendering;
using Weatherly.Core.Simulation;
using Weatherly.Core.Surfaces;

namespace Weatherly.Core.Effects.Snow;

/// <summary>
/// Snow (PLAN.md, section 4.2): flakes at different depths tumble down with the wind. Near flakes settle on
/// window top edges and on the floor, where the snow piles up as a height field that keeps the angle of repose.
/// Too much falls over the ends as clumps. Closing, hiding, shaking or jerking a window throws its snow off,
/// and the clumps pile up again wherever they land. When it stops snowing (or rains), the snow melts.
/// Struct-of-arrays with fixed capacities: no allocation per frame, except a field the first time a surface
/// gets snow.
/// </summary>
public sealed class SnowEffect : IEffectSystem
{
    public const int MaxFlakes = 8_000;

    public const int MaxClumps = 1_000;

    /// <summary>Width of a height-field bin in pixels at 100 % scaling (PLAN.md: 2 px x DPI).</summary>
    public const int BinWidth = 2;

    /// <summary>Highest pile in pixels at 100 % scaling (PLAN.md: 28 px x DPI).</summary>
    public const float MaxPileHeight = 28f;

    /// <summary>Share of the flakes that are near enough to land on windows. The others fall behind them.</summary>
    public const float NearShare = 0.65f;

    /// <summary>A window that changes speed by more than this (pixels per second at 100 %) in one move loses snow.</summary>
    public const float JerkThreshold = 2500f;

    /// <summary>Angle of repose: neighbouring bins differ by at most this share of the bin width (about 45 degrees).</summary>
    private const float ReposeSlope = 1f;

    /// <summary>Flakes per second per 1000 px of desktop width at intensity 1.</summary>
    private const float SpawnRatePer1000Pixels = 160f;

    /// <summary>Snow a landing flake adds, in square pixels at 100 % (scaled with nearness).</summary>
    private const float VolumePerFlake = 2.2f;

    /// <summary>Snow that makes one clump falling off an end, in square pixels at 100 %.</summary>
    private const float ClumpVolume = 14f;

    /// <summary>Width of the pieces a pile breaks into when it slides off, in pixels at 100 %.</summary>
    private const float SlideClumpWidth = 7f;

    private const float Gravity = 900f;

    /// <summary>Melting without rain: a share of the height per second plus a constant, about half a minute for a full pile.</summary>
    private const float MeltRate = 0.08f;

    private const float MeltLinear = 0.15f;

    /// <summary>Extra melting per unit of rain intensity (PLAN.md, section 4.7: rain on snow melts it faster).</summary>
    private const float RainMeltRate = 0.3f;

    /// <summary>Below this peak (pixels at 100 %) melting snow counts as gone.</summary>
    private const float GoneHeight = 0.3f;

    /// <summary>A move this long after the previous one starts from rest, and a window this long without a move has stopped.</summary>
    private const float MoveGap = 0.12f;

    private static readonly Vector3 s_snowColor = new(0.97f, 0.98f, 1f);
    private static readonly Vector3 s_shadowColor = new(0.55f, 0.64f, 0.85f);

    private readonly Dictionary<SurfaceKey, SnowField> _fields = [];
    private readonly Dictionary<long, Motion> _motion = [];
    private readonly List<SurfaceKey> _staleFields = [];
    private readonly List<long> _staleMotion = [];
    private Pcg32 _random;
    private float _spawnDebt;
    private float _scale = 1f;

    // Falling flakes.
    private readonly float[] _flakeX = new float[MaxFlakes];
    private readonly float[] _flakeY = new float[MaxFlakes];
    private readonly float[] _flakeVy = new float[MaxFlakes];
    private readonly float[] _flakeZ = new float[MaxFlakes];
    private readonly float[] _flakePhase = new float[MaxFlakes];
    private readonly float[] _flakeRotation = new float[MaxFlakes];
    private readonly float[] _flakeSpin = new float[MaxFlakes];
    private readonly byte[] _flakeVariant = new byte[MaxFlakes];
    private int _flakes;

    // Clumps of snow falling off windows. They carry their volume and leave it where they land.
    private readonly float[] _clumpX = new float[MaxClumps];
    private readonly float[] _clumpY = new float[MaxClumps];
    private readonly float[] _clumpVx = new float[MaxClumps];
    private readonly float[] _clumpVy = new float[MaxClumps];
    private readonly float[] _clumpVolume = new float[MaxClumps];
    private readonly float[] _clumpRotation = new float[MaxClumps];

    /// <summary>Row a clump must pass before it can land: it never lands back on the window it fell from.</summary>
    private readonly float[] _clumpFloor = new float[MaxClumps];
    private int _clumps;

    public SnowEffect(ulong seed = 1)
    {
        _random = new Pcg32(seed);
    }

    public string Name => "snow";

    public EffectSettings Settings { get; set; } = new();

    /// <summary>Rain intensity of the rain effect, 0 to 1. Rain melts the snow (PLAN.md, section 4.7).</summary>
    public float RainIntensity { get; set; }

    public bool IsActive => Settings.ClampedIntensity > 0f || _flakes > 0 || _clumps > 0 || HasSnow();

    public int FlakeCount => _flakes;

    public int ClumpCount => _clumps;

    /// <summary>Volume of all clumps in the air, in square pixels.</summary>
    public float FallingVolume
    {
        get
        {
            float sum = 0f;
            for (int i = 0; i < _clumps; i++)
            {
                sum += _clumpVolume[i];
            }

            return sum;
        }
    }

    /// <summary>Snow on the top edge of a window, or <see langword="null"/> while none fell there.</summary>
    public SnowField? PileOf(long windowId) => _fields.GetValueOrDefault(new SurfaceKey(windowId, SurfaceEdge.Top));

    /// <summary>Snow on the floor of a monitor (index into the monitor list).</summary>
    public SnowField? FloorOf(int monitorIndex) => _fields.GetValueOrDefault(new SurfaceKey(monitorIndex, SurfaceEdge.Floor));

    /// <summary>
    /// Covers every visible window top edge and floor with <paramref name="height"/> pixels of snow (at 100 %),
    /// sloped at the angle of repose. For previews and the sandbox.
    /// </summary>
    public void Fill(SimulationContext context, float height)
    {
        ArgumentNullException.ThrowIfNull(context);
        _scale = context.PixelScale;
        foreach (SurfaceSegment segment in context.Surfaces.Segments)
        {
            if (segment.IsHorizontal && FieldFor(segment, context) is { } field)
            {
                field.Fill(height * _scale);
            }
        }
    }

    public void Simulate(float dt, SimulationContext context)
    {
        float intensity = Settings.ClampedIntensity;
        _scale = context.PixelScale;
        Spawn(dt, intensity, context);
        SimulateFlakes(dt, context);
        SimulateClumps(dt, context);
        SimulateFields(dt, intensity, context);
        DetectStops(context);
    }

    public void OnWindowEvent(in WindowEvent windowEvent, SimulationContext context)
    {
        long id = windowEvent.WindowId;
        var key = new SurfaceKey(id, SurfaceEdge.Top);
        _scale = context.PixelScale;
        switch (windowEvent.Kind)
        {
            case WindowEventKind.Moved:
                OnMoved(id, windowEvent, context);
                break;

            case WindowEventKind.Resized:
                if (_fields.TryGetValue(key, out SnowField? resized))
                {
                    resized.Resize(windowEvent.Bounds.Width);
                }

                break;

            case WindowEventKind.Shaken:
                // Tossed up and off to both sides.
                SlideOff(key, windowEvent.Bounds, 1f, windowEvent.Velocity * 0.3f, toss: true);
                break;

            case WindowEventKind.Disappeared:
                SlideOff(key, windowEvent.Bounds, 1f, Vector2.Zero, toss: false);
                _fields.Remove(key);
                _motion.Remove(id);
                break;
        }
    }

    public void Emit(RenderFrame frame, SimulationContext context)
    {
        float brightness = Settings.ClampedBrightness;
        _scale = context.PixelScale;
        Vector3 color = Settings.Color ?? s_snowColor;

        EmitPiles(frame, context, color, brightness);
        EmitClumps(frame, color, brightness);
        EmitFlakes(frame, context, color, brightness);
    }

    private static float FallSpeed(float z) => 38f + (70f * z);

    private static bool IsBehindAWindow(ReadOnlySpan<TrackedWindow> windows, float x, float y)
    {
        int px = (int)MathF.Floor(x);
        int py = (int)MathF.Floor(y);
        foreach (TrackedWindow window in windows)
        {
            if (window.Bounds.Contains(px, py))
            {
                return true;
            }
        }

        return false;
    }

    /// <summary>A cheap, stable hash in [0, 1) for sparkles.</summary>
    private static float Hash(int a, int b, int c)
    {
        uint h = ((uint)a * 0x9E3779B1u) ^ ((uint)b * 0x85EBCA77u) ^ ((uint)c * 0xC2B2AE3Du);
        h ^= h >> 15;
        h *= 0x2C1B3C6Du;
        h ^= h >> 12;
        return (h & 0xFFFFFF) / 16777216f;
    }

    private bool HasSnow()
    {
        foreach (SnowField field in _fields.Values)
        {
            if (!field.IsEmpty)
            {
                return true;
            }
        }

        return false;
    }

    private SnowField? FieldFor(in SurfaceSegment segment, SimulationContext context)
    {
        if (_fields.TryGetValue(segment.Key, out SnowField? field))
        {
            return field;
        }

        int width;
        if (segment.Key.Edge == SurfaceEdge.Top)
        {
            int index = context.Windows.IndexOf(segment.Key.OwnerId);
            if (index < 0)
            {
                return null;
            }

            width = context.Windows[index].Bounds.Width;
        }
        else
        {
            int monitor = segment.MonitorIndex;
            if (monitor < 0 || monitor >= context.Monitors.Count)
            {
                return null;
            }

            width = context.Monitors[monitor].WorkArea.Width;
        }

        int binWidth = Math.Max(1, (int)MathF.Round(BinWidth * _scale));
        field = new SnowField(width, binWidth, MaxPileHeight * _scale, ReposeSlope * binWidth);
        _fields.Add(segment.Key, field);
        return field;
    }

    private void Spawn(float dt, float intensity, SimulationContext context)
    {
        PixelRect desktop = context.DesktopBounds;
        if (intensity <= 0f || desktop.IsEmpty)
        {
            return;
        }

        float windX = context.Wind.Current.X * _scale;
        float fallTime = desktop.Height / (FallSpeed(0.5f) * _scale);
        float margin = MathF.Min(MathF.Abs(windX) * fallTime, desktop.Width);
        float left = desktop.Left - (windX > 0 ? margin : 0f);
        float width = desktop.Width + margin;

        _spawnDebt += MathF.Pow(intensity, 1.1f) * SpawnRatePer1000Pixels * (width / 1000f) * dt;
        while (_spawnDebt >= 1f && _flakes < MaxFlakes)
        {
            _spawnDebt -= 1f;
            int i = _flakes++;
            float z = _random.NextFloat();
            _flakeZ[i] = z;
            _flakeX[i] = left + (_random.NextFloat() * width);
            _flakeY[i] = desktop.Top - (_random.NextFloat() * 20f * _scale);
            _flakeVy[i] = FallSpeed(z) * _scale * _random.NextFloat(0.8f, 1.1f);
            _flakePhase[i] = _random.NextFloat(0f, 100f);
            _flakeRotation[i] = _random.NextFloat(0f, MathF.Tau);
            _flakeSpin[i] = _random.NextFloat(-1.2f, 1.2f);
            _flakeVariant[i] = (byte)_random.NextInt(Sprite.FlakeVariants);
        }

        if (_flakes == MaxFlakes)
        {
            _spawnDebt = 0f;
        }
    }

    private void SimulateFlakes(float dt, SimulationContext context)
    {
        float threshold = 1f - NearShare;
        float windX = context.Wind.Current.X * _scale;
        float bottom = context.DesktopBounds.Bottom + 10f;
        int i = 0;
        while (i < _flakes)
        {
            float z = _flakeZ[i];

            // Tumbling: a sideways sway that grows with nearness, on top of the wind.
            _flakePhase[i] += dt * (1.1f + (0.6f * z));
            float sway = MathF.Sin(_flakePhase[i]) * (14f + (22f * z)) * _scale;
            float vx = (windX * (0.35f + (0.65f * z))) + sway;
            _flakeRotation[i] += _flakeSpin[i] * dt;

            float nextX = _flakeX[i] + (vx * dt);
            float nextY = _flakeY[i] + (_flakeVy[i] * dt);

            if (z >= threshold && context.SurfaceIndex.TryFindLanding(nextX, _flakeY[i], nextY, out SurfaceHit hit))
            {
                SurfaceSegment segment = context.Surfaces.Segments[hit.Segment];
                if (FieldFor(segment, context) is { } field)
                {
                    field.Add(nextX - segment.Origin, VolumePerFlake * (0.6f + (0.8f * z)) * _scale * _scale);
                }

                RemoveFlake(i);
                continue;
            }

            _flakeX[i] = nextX;
            _flakeY[i] = nextY;
            if (nextY > bottom)
            {
                RemoveFlake(i);
                continue;
            }

            i++;
        }
    }

    private void SimulateClumps(float dt, SimulationContext context)
    {
        float bottom = context.DesktopBounds.Bottom + 40f;
        float windX = context.Wind.Current.X * _scale;
        int i = 0;
        while (i < _clumps)
        {
            _clumpVy[i] += Gravity * _scale * dt;
            _clumpVx[i] += ((windX * 0.2f) - _clumpVx[i]) * MathF.Min(1f, 0.8f * dt);
            _clumpRotation[i] += _clumpVx[i] * 0.02f * dt;

            float nextX = _clumpX[i] + (_clumpVx[i] * dt);
            float nextY = _clumpY[i] + (_clumpVy[i] * dt);

            // Landing: on whatever surface lies below, once the clump is past the window it came from.
            float from = MathF.Max(_clumpY[i], _clumpFloor[i]);
            if (_clumpVy[i] > 0f && nextY > from && context.SurfaceIndex.TryFindLanding(nextX, from, nextY, out SurfaceHit hit))
            {
                SurfaceSegment segment = context.Surfaces.Segments[hit.Segment];
                if (FieldFor(segment, context) is { } field)
                {
                    // Spread over the clump's width, so a heavy clump does not make a spike.
                    float volume = _clumpVolume[i];
                    float radius = ClumpRadius(volume);
                    int pieces = Math.Clamp((int)MathF.Ceiling(2f * radius / field.BinWidth), 1, 16);
                    float local = nextX - segment.Origin;
                    for (int p = 0; p < pieces; p++)
                    {
                        float offset = pieces == 1 ? 0f : ((p / (pieces - 1f)) - 0.5f) * 2f * radius;
                        field.Add(local + offset, volume / pieces);
                    }
                }

                RemoveClump(i);
                continue;
            }

            _clumpX[i] = nextX;
            _clumpY[i] = nextY;
            if (nextY > bottom)
            {
                RemoveClump(i);
                continue;
            }

            i++;
        }
    }

    private void SimulateFields(float dt, float intensity, SimulationContext context)
    {
        float rain = Math.Clamp(RainIntensity, 0f, 1f);
        bool melting = intensity <= 0f || rain > 0f;
        float rate = MeltRate + (RainMeltRate * rain);

        _staleFields.Clear();
        foreach (KeyValuePair<SurfaceKey, SnowField> entry in _fields)
        {
            SurfaceKey key = entry.Key;
            SnowField field = entry.Value;
            bool isFloor = key.Edge == SurfaceEdge.Floor;
            int index = isFloor ? -1 : context.Windows.IndexOf(key.OwnerId);
            bool gone = isFloor ? key.OwnerId < 0 || key.OwnerId >= context.Monitors.Count : index < 0;
            if (gone)
            {
                _staleFields.Add(key);
                continue;
            }

            if (isFloor)
            {
                field.Resize(context.Monitors[(int)key.OwnerId].WorkArea.Width);
            }

            field.Relax(passes: 2);

            float clump = ClumpVolume * _scale * _scale;
            if (melting)
            {
                field.Melt(dt, rate, MeltLinear * _scale);

                // Once only a trace is left, it is gone: no remainder keeps the render loop alive.
                if (field.Peak < GoneHeight * _scale && field.LeftShed < clump && field.RightShed < clump)
                {
                    field.TakeAll();
                    continue;
                }
            }

            // The floor's ends are monitor edges: snow pushed over them is simply gone.
            if (isFloor)
            {
                field.LeftShed = 0f;
                field.RightShed = 0f;
                continue;
            }

            // Snow pushed over a window's ends falls as clumps from its corners.
            PixelRect bounds = context.Windows[index].Bounds;
            while (field.LeftShed >= clump)
            {
                field.LeftShed -= clump;
                SpawnClump(bounds.Left - ClumpRadius(clump), bounds.Top - (2f * _scale), clump, new Vector2(-15f, 0f) * _scale, bounds.Top);
            }

            while (field.RightShed >= clump)
            {
                field.RightShed -= clump;
                SpawnClump(bounds.Right + ClumpRadius(clump), bounds.Top - (2f * _scale), clump, new Vector2(15f, 0f) * _scale, bounds.Top);
            }
        }

        foreach (SurfaceKey key in _staleFields)
        {
            _fields.Remove(key);
        }
    }

    private void OnMoved(long id, in WindowEvent windowEvent, SimulationContext context)
    {
        double now = context.Time;
        Vector2 velocity = windowEvent.Velocity;
        Vector2 before = Vector2.Zero;
        if (_motion.TryGetValue(id, out Motion motion) && now - motion.Time <= MoveGap)
        {
            before = motion.Velocity;
        }

        _motion[id] = new Motion(velocity, now);

        // A jerk: the snow keeps its old velocity while the window changes its own.
        float jerk = (velocity - before).Length();
        if (jerk > JerkThreshold * _scale)
        {
            float share = Math.Clamp((jerk / (JerkThreshold * _scale)) - 0.6f, 0.4f, 1f);
            SlideOff(new SurfaceKey(id, SurfaceEdge.Top), windowEvent.PreviousBounds, share, before, toss: false);
        }
    }

    /// <summary>A window that moved fast and then stopped: the snow keeps going and flies off.</summary>
    private void DetectStops(SimulationContext context)
    {
        if (_motion.Count == 0)
        {
            return;
        }

        _staleMotion.Clear();
        foreach (KeyValuePair<long, Motion> entry in _motion)
        {
            if (context.Time - entry.Value.Time <= MoveGap)
            {
                continue;
            }

            _staleMotion.Add(entry.Key);
            Vector2 velocity = entry.Value.Velocity;
            float jerk = velocity.Length();
            if (jerk <= JerkThreshold * _scale)
            {
                continue;
            }

            int index = context.Windows.IndexOf(entry.Key);
            if (index >= 0)
            {
                float share = Math.Clamp((jerk / (JerkThreshold * _scale)) - 0.6f, 0.4f, 1f);
                SlideOff(new SurfaceKey(entry.Key, SurfaceEdge.Top), context.Windows[index].Bounds, share, velocity, toss: false);
            }
        }

        foreach (long id in _staleMotion)
        {
            _motion.Remove(id);
        }
    }

    /// <summary>Breaks <paramref name="share"/> of a window's snow into clumps that fall off.</summary>
    /// <param name="key">The window's top edge.</param>
    /// <param name="bounds">Where the window was when it lost the snow.</param>
    /// <param name="share">How much of the snow comes off, 0 to 1.</param>
    /// <param name="velocity">Velocity the snow keeps (pixels per second).</param>
    /// <param name="toss">Thrown up and sideways (shaking) instead of just dropping.</param>
    private void SlideOff(SurfaceKey key, PixelRect bounds, float share, Vector2 velocity, bool toss)
    {
        if (!_fields.TryGetValue(key, out SnowField? field))
        {
            return;
        }

        int binsPerClump = Math.Max(1, (int)MathF.Round(SlideClumpWidth * _scale / field.BinWidth));
        ReadOnlySpan<float> heights = field.Heights;
        for (int first = 0; first < field.Count; first += binsPerClump)
        {
            float peak = 0f;
            for (int b = first; b < Math.Min(first + binsPerClump, field.Count); b++)
            {
                peak = MathF.Max(peak, heights[b]);
            }

            if (peak <= 0f)
            {
                continue;
            }

            float volume = field.Shed(first, binsPerClump, share);
            float x = bounds.Left + ((first + (binsPerClump / 2f)) * field.BinWidth);
            float y = bounds.Top - (peak / 2f);
            Vector2 v = velocity + (new Vector2(_random.NextFloat(-25f, 25f), _random.NextFloat(-10f, 30f)) * _scale);
            if (toss)
            {
                v += new Vector2(_random.NextFloat(-90f, 90f), _random.NextFloat(-160f, -60f)) * _scale;
            }

            if (!SpawnClump(x, y, volume, v, bounds.Top))
            {
                // No room in the air: the rest stays on the window.
                field.Add(x - bounds.Left, volume);
            }
        }

        // What waited at the ends goes along.
        float rest = field.LeftShed + field.RightShed;
        field.LeftShed = 0f;
        field.RightShed = 0f;
        if (rest > 0f && !SpawnClump(bounds.Left + (bounds.Width / 2f), bounds.Top - _scale, rest, velocity, bounds.Top))
        {
            field.RightShed = rest;
        }
    }

    private float ClumpRadius(float volume) => MathF.Max(1.2f * _scale, MathF.Sqrt(volume / MathF.PI));

    private bool SpawnClump(float x, float y, float volume, Vector2 velocity, float floor)
    {
        if (volume <= 0f || _clumps >= MaxClumps)
        {
            return volume <= 0f;
        }

        int i = _clumps++;
        _clumpX[i] = x;
        _clumpY[i] = y;
        _clumpVx[i] = velocity.X;
        _clumpVy[i] = velocity.Y;
        _clumpVolume[i] = volume;
        _clumpRotation[i] = _random.NextFloat(0f, MathF.Tau);

        // Below the edge it came from, plus its own size: it falls past its window first.
        _clumpFloor[i] = floor + 1f;
        return true;
    }

    private void RemoveFlake(int i)
    {
        int last = --_flakes;
        _flakeX[i] = _flakeX[last];
        _flakeY[i] = _flakeY[last];
        _flakeVy[i] = _flakeVy[last];
        _flakeZ[i] = _flakeZ[last];
        _flakePhase[i] = _flakePhase[last];
        _flakeRotation[i] = _flakeRotation[last];
        _flakeSpin[i] = _flakeSpin[last];
        _flakeVariant[i] = _flakeVariant[last];
    }

    private void RemoveClump(int i)
    {
        int last = --_clumps;
        _clumpX[i] = _clumpX[last];
        _clumpY[i] = _clumpY[last];
        _clumpVx[i] = _clumpVx[last];
        _clumpVy[i] = _clumpVy[last];
        _clumpVolume[i] = _clumpVolume[last];
        _clumpRotation[i] = _clumpRotation[last];
        _clumpFloor[i] = _clumpFloor[last];
    }

    private void EmitPiles(RenderFrame frame, SimulationContext context, Vector3 color, float brightness)
    {
        int twinkle = (int)(context.Time * 3);
        float alpha = MathF.Min(1f, 0.95f * brightness);
        Vector4 snow = Sprite.Premultiply(color, alpha);
        Vector4 shadow = Sprite.Premultiply(s_shadowColor * color, MathF.Min(1f, 0.28f * brightness));
        foreach (SurfaceSegment segment in context.Surfaces.Segments)
        {
            if (!segment.IsHorizontal || !_fields.TryGetValue(segment.Key, out SnowField? field))
            {
                continue;
            }

            ReadOnlySpan<float> heights = field.Heights;
            int first = field.BinAt(segment.Start - segment.Origin);
            int last = field.BinAt(segment.End - 1 - segment.Origin);
            for (int b = first; b <= last; b++)
            {
                float height = heights[b];
                if (height < 0.05f)
                {
                    continue;
                }

                // Clip the bin to the visible part of the surface.
                float left = MathF.Max(segment.Origin + (b * field.BinWidth), segment.Start);
                float right = MathF.Min(segment.Origin + ((b + 1) * field.BinWidth), segment.End);
                if (right <= left)
                {
                    continue;
                }

                float halfWidth = (right - left) / 2f;
                float centerX = (left + right) / 2f;
                float bottom = segment.Position;

                // Whole pixels solid, the fraction as a half-transparent top row: a soft, smooth top edge.
                float whole = MathF.Floor(height);
                float fraction = height - whole;
                if (whole > 0f)
                {
                    frame.Add(new Sprite(new Vector2(centerX, bottom - (whole / 2f)), new Vector2(halfWidth, whole / 2f), 0f, SpriteShape.Rectangle, snow));
                }

                if (fraction > 0.05f)
                {
                    frame.Add(new Sprite(new Vector2(centerX, bottom - whole - 0.5f), new Vector2(halfWidth, 0.5f), 0f, SpriteShape.Rectangle, snow * fraction));
                }

                // A light blue shade towards the bottom gives the pile some volume.
                float shade = MathF.Min(whole, MathF.Max(1f, height * 0.4f));
                if (shade >= 1f)
                {
                    frame.Add(new Sprite(new Vector2(centerX, bottom - (shade / 2f)), new Vector2(halfWidth, shade / 2f), 0f, SpriteShape.Rectangle, shadow));
                }

                // Glitter: now and then a bin near the top of the pile twinkles.
                if (height > 2f * _scale && Hash(b, (int)segment.Key.OwnerId, twinkle) < 0.012f)
                {
                    float glint = 0.5f + (0.5f * Hash(b, twinkle, 7));
                    frame.Add(new Sprite(
                        new Vector2(centerX, bottom - height + (1.5f * _scale)),
                        new Vector2(1.2f, 1.2f) * _scale,
                        MathF.PI / 4f,
                        SpriteShape.Flake,
                        Sprite.Premultiply(Vector3.One, MathF.Min(1f, glint * brightness)))
                    { Variant = 1 });
                }
            }
        }
    }

    private void EmitClumps(RenderFrame frame, Vector3 color, float brightness)
    {
        Vector4 snow = Sprite.Premultiply(color, MathF.Min(1f, 0.95f * brightness));
        for (int i = 0; i < _clumps; i++)
        {
            float radius = ClumpRadius(_clumpVolume[i]);
            var center = new Vector2(_clumpX[i], _clumpY[i]);
            frame.Add(new Sprite(center, new Vector2(radius, radius * 0.85f), _clumpRotation[i], SpriteShape.Disc, snow));

            // A smaller lump on the side makes it look less like a ball.
            var offset = new Vector2(MathF.Cos(_clumpRotation[i]), MathF.Sin(_clumpRotation[i])) * radius * 0.6f;
            frame.Add(new Sprite(center + offset, new Vector2(radius * 0.6f, radius * 0.55f), 0f, SpriteShape.Disc, snow));
        }
    }

    private void EmitFlakes(RenderFrame frame, SimulationContext context, Vector3 color, float brightness)
    {
        float threshold = 1f - NearShare;
        ReadOnlySpan<TrackedWindow> windows = context.Windows.Windows;
        for (int i = 0; i < _flakes; i++)
        {
            float z = _flakeZ[i];

            // Far flakes fall behind the windows.
            if (z < threshold && IsBehindAWindow(windows, _flakeX[i], _flakeY[i]))
            {
                continue;
            }

            float radius = (0.8f + (2.6f * z * z)) * _scale;
            float alpha = MathF.Min(1f, (0.35f + (0.6f * z)) * brightness);

            // Small flakes cannot show a shape: they are soft blobs.
            int variant = radius >= 2.2f * _scale ? _flakeVariant[i] : 0;
            frame.Add(new Sprite(
                new Vector2(_flakeX[i], _flakeY[i]),
                new Vector2(radius, radius),
                _flakeRotation[i],
                SpriteShape.Flake,
                Sprite.Premultiply(color, alpha))
            { Variant = variant });
        }
    }

    private readonly record struct Motion(Vector2 Velocity, double Time);
}
