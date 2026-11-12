using System.Numerics;
using Sillfall.Core.Desktop;
using Sillfall.Core.Geometry;
using Sillfall.Core.Rendering;
using Sillfall.Core.Simulation;
using Sillfall.Core.Surfaces;

namespace Sillfall.Core.Effects.Rain;

/// <summary>
/// Rain (PLAN.md, section 4.1): drops at different depths fall with the wind. Near drops hit window top
/// edges and floors, splash and leave a ripple. Water collects on top edges, creeps to the corners and
/// runs down the window sides as drips with a trail. In heavy rain the corners pour like waterfalls.
/// Far drops pass behind the windows. Struct-of-arrays with fixed capacities: no allocation per frame,
/// except a water field the first time a window gets wet.
/// </summary>
public sealed class RainEffect : IEffectSystem
{
    /// <summary>Budget from PLAN.md, section 4.1.</summary>
    public const int MaxDrops = 12_000;

    public const int MaxSplashes = 2_000;

    public const int MaxDrips = 600;

    public const int MaxRipples = 600;

    /// <summary>Trail points remembered per drip.</summary>
    public const int TrailLength = 12;

    /// <summary>Width of a puddle bin in pixels at 100 % scaling.</summary>
    public const int BinWidth = 4;

    private const float Gravity = 1500f;

    /// <summary>Drops per second per 1000 px of desktop width at intensity 1.</summary>
    private const float SpawnRatePer1000Pixels = 2600f;

    private const float WaterPerHit = 0.05f;

    /// <summary>Water (in bin units) that makes one drip. Larger portions: fewer, fuller drips.</summary>
    private const float WaterPerDrip = 1.5f;

    /// <summary>Half width of a drip head at 100 %. Drips run just outside the frame, never on the window.</summary>
    private const float DripRadius = 1.8f;

    private const float DripMaxSpeed = 95f;

    /// <summary>Puddles evaporate slowly while it rains ...</summary>
    private const float Evaporation = 0.06f;

    /// <summary>... and faster afterwards: the edges are dry about 20 seconds after the rain stopped.</summary>
    private const float EvaporationAfterRain = 0.15f;

    /// <summary>Once no bin holds more than this after the rain, the edge counts as dry and its water is gone.</summary>
    private const float DryThreshold = 0.02f;

    private const float TrailSpacing = 5f;

    private static readonly Vector3 s_rainColor = new(0.78f, 0.85f, 0.95f);
    private static readonly Vector3 s_waterColor = new(0.80f, 0.88f, 1f);

    private readonly Dictionary<long, WaterField> _puddles = [];
    private readonly List<long> _staleFields = [];
    private Pcg32 _random;
    private float _spawnDebt;

    /// <summary>Pixel scale of the current step (<see cref="SimulationContext.PixelScale"/>).</summary>
    private float _scale = 1f;

    // Falling drops.
    private readonly float[] _dropX = new float[MaxDrops];
    private readonly float[] _dropY = new float[MaxDrops];
    private readonly float[] _dropVx = new float[MaxDrops];
    private readonly float[] _dropVy = new float[MaxDrops];
    private readonly float[] _dropZ = new float[MaxDrops];
    private int _drops;

    // Splash droplets.
    private readonly float[] _splashX = new float[MaxSplashes];
    private readonly float[] _splashY = new float[MaxSplashes];
    private readonly float[] _splashVx = new float[MaxSplashes];
    private readonly float[] _splashVy = new float[MaxSplashes];
    private readonly float[] _splashLife = new float[MaxSplashes];

    /// <summary>Row of the surface a splash came from. Falling back to it, the droplet is gone.</summary>
    private readonly float[] _splashFloor = new float[MaxSplashes];
    private int _splashes;

    // Ripples where drops hit.
    private readonly float[] _rippleX = new float[MaxRipples];
    private readonly float[] _rippleY = new float[MaxRipples];
    private readonly float[] _rippleAge = new float[MaxRipples];
    private readonly float[] _rippleSize = new float[MaxRipples];
    private int _ripples;

    // Drips running down window sides, each with a trail.
    private readonly float[] _dripX = new float[MaxDrips];
    private readonly float[] _dripY = new float[MaxDrips];
    private readonly float[] _dripVy = new float[MaxDrips];
    private readonly float[] _dripPhase = new float[MaxDrips];
    private readonly long[] _dripOwner = new long[MaxDrips];
    private readonly int[] _dripColumn = new int[MaxDrips];
    private readonly int[] _dripOutward = new int[MaxDrips];
    private readonly float[] _trailX = new float[MaxDrips * TrailLength];
    private readonly float[] _trailY = new float[MaxDrips * TrailLength];
    private readonly int[] _trailCount = new int[MaxDrips];
    private int _drips;

    public RainEffect(ulong seed = 1)
    {
        _random = new Pcg32(seed);
    }

    public string Name => "rain";

    public EffectSettings Settings { get; set; } = new();

    /// <summary>
    /// Share of the drops that are near enough to interact with windows (PLAN.md: setting "Depth"), 0 to 1.
    /// The others fall behind the windows.
    /// </summary>
    public float Depth { get; set; } = 0.5f;

    public bool IsActive => Settings.ClampedIntensity > 0f || _drops > 0 || _splashes > 0 || _ripples > 0 || _drips > 0 || HasWater();

    public int DropCount => _drops;

    public int SplashCount => _splashes;

    public int DripCount => _drips;

    public int RippleCount => _ripples;

    /// <summary>Splashes created in the last step, per second. Drives the rain sound.</summary>
    public float HitsPerSecond { get; private set; }

    /// <summary>Water standing on the top edge of a window, or <see langword="null"/> while it is dry.</summary>
    public WaterField? PuddleOf(long windowId) => _puddles.GetValueOrDefault(windowId);

    public void Simulate(float dt, SimulationContext context)
    {
        float intensity = Settings.ClampedIntensity;
        _scale = context.PixelScale;
        Spawn(dt, intensity, context);

        int hits = SimulateDrops(dt, context);
        HitsPerSecond = hits / dt;

        SimulateSplashes(dt);
        SimulateRipples(dt);
        SimulatePuddles(dt, intensity, context);
        SimulateDrips(dt, context);
    }

    public void OnWindowEvent(in WindowEvent windowEvent, SimulationContext context)
    {
        long id = windowEvent.WindowId;
        switch (windowEvent.Kind)
        {
            case WindowEventKind.Moved:
                ShiftDrips(id, windowEvent.Bounds.Left - windowEvent.PreviousBounds.Left, windowEvent.Bounds.Top - windowEvent.PreviousBounds.Top);
                break;

            case WindowEventKind.Resized:
                if (_puddles.TryGetValue(id, out WaterField? resized))
                {
                    resized.Resize(windowEvent.Bounds.Width);
                }

                break;

            case WindowEventKind.Shaken:
                SlideOff(id, windowEvent.Bounds);
                break;

            case WindowEventKind.Disappeared:
                SlideOff(id, windowEvent.Bounds);
                _puddles.Remove(id);
                ReleaseDrips(id);
                break;
        }
    }

    public void Emit(RenderFrame frame, SimulationContext context)
    {
        float brightness = Settings.ClampedBrightness;
        _scale = context.PixelScale;
        Vector3 rainColor = Settings.Color ?? s_rainColor;
        Vector3 waterColor = Settings.Color ?? s_waterColor;

        EmitPuddles(frame, context, waterColor, brightness);
        EmitDrips(frame, waterColor, brightness);
        EmitRipples(frame, waterColor, brightness);
        EmitSplashes(frame, rainColor, brightness);
        EmitDrops(frame, context, rainColor, brightness);
    }

    private static float TerminalVelocity(float z) => 900f + (650f * z);

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

    private static float MaxBin(WaterField field)
    {
        float max = 0f;
        foreach (float water in field.Bins)
        {
            max = MathF.Max(max, water);
        }

        return max;
    }

    /// <summary>Dry edges are drained to exactly zero, so any water left means there is something to show.</summary>
    private bool HasWater()
    {
        foreach (WaterField field in _puddles.Values)
        {
            if (field.Total > 0f || field.LeftRunoff > 0f || field.RightRunoff > 0f)
            {
                return true;
            }
        }

        return false;
    }

    private void Spawn(float dt, float intensity, SimulationContext context)
    {
        PixelRect desktop = context.DesktopBounds;
        if (intensity <= 0f || desktop.IsEmpty)
        {
            return;
        }

        // Wind blows drops in from the side: spawn wider than the desktop, on the windward side.
        float scale = context.PixelScale;
        float windX = context.Wind.Current.X * scale;
        float fallTime = desktop.Height / (TerminalVelocity(0.5f) * scale);
        float margin = MathF.Abs(windX) * fallTime;
        float left = desktop.Left - (windX > 0 ? margin : 0f);
        float width = desktop.Width + margin;

        _spawnDebt += MathF.Pow(intensity, 1.2f) * SpawnRatePer1000Pixels * (width / 1000f) * dt;
        while (_spawnDebt >= 1f && _drops < MaxDrops)
        {
            _spawnDebt -= 1f;
            int i = _drops++;
            float z = _random.NextFloat();
            _dropZ[i] = z;
            _dropX[i] = left + (_random.NextFloat() * width);
            _dropY[i] = desktop.Top - (_random.NextFloat() * 60f * scale);
            _dropVy[i] = TerminalVelocity(z) * scale * _random.NextFloat(0.9f, 1f);
            _dropVx[i] = windX * (0.5f + (0.5f * z));
        }

        if (_drops == MaxDrops)
        {
            _spawnDebt = 0f;
        }
    }

    private int SimulateDrops(float dt, SimulationContext context)
    {
        float threshold = 1f - Math.Clamp(Depth, 0f, 1f);
        float scale = context.PixelScale;
        float windX = context.Wind.Current.X * scale;
        float bottom = context.DesktopBounds.Bottom + 20f;
        int hits = 0;

        int i = 0;
        while (i < _drops)
        {
            float z = _dropZ[i];
            _dropVy[i] = MathF.Min(_dropVy[i] + (Gravity * scale * dt), TerminalVelocity(z) * scale);
            float targetVx = windX * (0.5f + (0.5f * z));
            _dropVx[i] += (targetVx - _dropVx[i]) * MathF.Min(1f, 3f * dt);

            float nextX = _dropX[i] + (_dropVx[i] * dt);
            float nextY = _dropY[i] + (_dropVy[i] * dt);

            if (z >= threshold && context.SurfaceIndex.TryFindLanding(nextX, _dropY[i], nextY, out SurfaceHit hit))
            {
                SurfaceSegment segment = context.Surfaces.Segments[hit.Segment];
                Hit(nextX, hit.Row, z, segment, context, scale);
                hits++;
                RemoveDrop(i);
                continue;
            }

            _dropX[i] = nextX;
            _dropY[i] = nextY;
            if (nextY > bottom)
            {
                RemoveDrop(i);
                continue;
            }

            i++;
        }

        return hits;
    }

    private void Hit(float x, int row, float z, in SurfaceSegment segment, SimulationContext context, float scale)
    {
        int droplets = 3 + _random.NextInt(4);
        float speed = (120f + (180f * z)) * scale;
        for (int d = 0; d < droplets && _splashes < MaxSplashes; d++)
        {
            int s = _splashes++;
            float angle = -MathF.PI * _random.NextFloat(0.15f, 0.85f);
            float v = speed * _random.NextFloat(0.5f, 1f);
            _splashX[s] = x;
            _splashY[s] = row - scale;
            _splashFloor[s] = row - scale;
            _splashVx[s] = MathF.Cos(angle) * v;
            _splashVy[s] = MathF.Sin(angle) * v;
            _splashLife[s] = _random.NextFloat(0.15f, 0.35f);
        }

        if (_ripples < MaxRipples)
        {
            int r = _ripples++;
            _rippleX[r] = x;
            _rippleY[r] = row - scale;
            _rippleAge[r] = 0f;
            _rippleSize[r] = (3f + (4f * z)) * scale;
        }

        if (segment.Key.Edge == SurfaceEdge.Top)
        {
            WaterField field = FieldFor(segment.Key.OwnerId, context);
            field.Add(x - segment.Origin, WaterPerHit * (0.5f + z));
        }
    }

    private WaterField FieldFor(long windowId, SimulationContext context)
    {
        if (!_puddles.TryGetValue(windowId, out WaterField? field))
        {
            int binWidth = Math.Max(1, (int)MathF.Round(BinWidth * context.PixelScale));
            int width = binWidth;
            int index = context.Windows.IndexOf(windowId);
            if (index >= 0)
            {
                width = context.Windows[index].Bounds.Width;
            }

            field = new WaterField(width, binWidth);
            _puddles.Add(windowId, field);
        }

        return field;
    }

    private void SimulateSplashes(float dt)
    {
        int i = 0;
        while (i < _splashes)
        {
            _splashLife[i] -= dt;
            _splashVy[i] += Gravity * _scale * dt;
            _splashX[i] += _splashVx[i] * dt;
            _splashY[i] += _splashVy[i] * dt;

            // A droplet that falls back onto the surface it jumped from is absorbed, it never drops into the window.
            if (_splashLife[i] <= 0f || (_splashVy[i] > 0f && _splashY[i] > _splashFloor[i]))
            {
                int last = --_splashes;
                _splashX[i] = _splashX[last];
                _splashY[i] = _splashY[last];
                _splashVx[i] = _splashVx[last];
                _splashVy[i] = _splashVy[last];
                _splashLife[i] = _splashLife[last];
                _splashFloor[i] = _splashFloor[last];
                continue;
            }

            i++;
        }
    }

    private void SimulateRipples(float dt)
    {
        const float Lifetime = 0.3f;
        int i = 0;
        while (i < _ripples)
        {
            _rippleAge[i] += dt;
            if (_rippleAge[i] >= Lifetime)
            {
                int last = --_ripples;
                _rippleX[i] = _rippleX[last];
                _rippleY[i] = _rippleY[last];
                _rippleAge[i] = _rippleAge[last];
                _rippleSize[i] = _rippleSize[last];
                continue;
            }

            i++;
        }
    }

    private void SimulatePuddles(float dt, float intensity, SimulationContext context)
    {
        _staleFields.Clear();
        foreach (KeyValuePair<long, WaterField> entry in _puddles)
        {
            int index = context.Windows.IndexOf(entry.Key);
            if (index < 0)
            {
                _staleFields.Add(entry.Key);
                continue;
            }

            PixelRect bounds = context.Windows[index].Bounds;
            WaterField field = entry.Value;
            field.Step(dt, evaporation: intensity > 0f ? Evaporation : EvaporationAfterRain);

            // After the rain, once no bin shows water any more and the runoff is too little for a drip, the edge
            // is dry. Without this a remainder would keep the effect, and the render loop, alive forever.
            // The field object stays for the next shower.
            if (intensity == 0f && field.LeftRunoff < WaterPerDrip && field.RightRunoff < WaterPerDrip && MaxBin(field) < DryThreshold)
            {
                field.Drain();
                continue;
            }

            // Runoff turns into drips at the corners, one per portion of water.
            while (field.LeftRunoff >= WaterPerDrip)
            {
                field.LeftRunoff -= WaterPerDrip;
                SpawnDrip(entry.Key, bounds.Left - 1, -1, bounds.Top);
            }

            while (field.RightRunoff >= WaterPerDrip)
            {
                field.RightRunoff -= WaterPerDrip;
                SpawnDrip(entry.Key, bounds.Right, 1, bounds.Top);
            }

            // Heavy rain: the corners pour continuously (PLAN.md: waterfalls above intensity 0.8).
            if (intensity > 0.8f && field.Total > field.Count * 0.15f)
            {
                float rate = (intensity - 0.8f) / 0.2f * 45f;
                SpawnSheet(bounds.Left - 1, bounds.Top, rate * dt);
                SpawnSheet(bounds.Right, bounds.Top, rate * dt);
            }
        }

        foreach (long id in _staleFields)
        {
            _puddles.Remove(id);
        }
    }

    /// <summary>A thin stream from a corner: small near drops that start slowly and accelerate.</summary>
    private void SpawnSheet(float x, float y, float expected)
    {
        // Poisson-ish: whole drops plus a chance for the fraction.
        int count = (int)expected;
        if (_random.NextFloat() < expected - count)
        {
            count++;
        }

        for (int n = 0; n < count && _drops < MaxDrops; n++)
        {
            int i = _drops++;
            _dropX[i] = x + _random.NextFloat(-1.5f, 1.5f);
            _dropY[i] = y + 1f;
            _dropVx[i] = 0f;
            _dropVy[i] = _random.NextFloat(20f, 80f) * _scale;

            // Just below "near": falls past the window it pours from, still in front of everything else.
            _dropZ[i] = 0.98f;
        }
    }

    /// <param name="owner">Window the drip runs down.</param>
    /// <param name="column">Side column just outside the frame.</param>
    /// <param name="outward">-1 for the left side, +1 for the right side: the drip hangs away from the window.</param>
    /// <param name="top">Row the drip starts at.</param>
    private void SpawnDrip(long owner, int column, int outward, int top)
    {
        if (_drips >= MaxDrips)
        {
            return;
        }

        int i = _drips++;
        _dripOutward[i] = outward;
        _dripX[i] = column + 0.5f + (outward * DripRadius * _scale * 0.5f);
        _dripY[i] = top;
        _dripVy[i] = 10f * _scale;
        _dripPhase[i] = _random.NextFloat(0f, 100f);
        _dripOwner[i] = owner;
        _dripColumn[i] = column;
        _trailCount[i] = 0;
    }

    private void SimulateDrips(float dt, SimulationContext context)
    {
        int i = 0;
        while (i < _drips)
        {
            int index = context.Windows.IndexOf(_dripOwner[i]);
            if (index < 0)
            {
                ReleaseDrip(i);
                continue;
            }

            PixelRect bounds = context.Windows[index].Bounds;
            _dripVy[i] = MathF.Min(_dripVy[i] + (220f * _scale * dt), DripMaxSpeed * _scale);
            _dripY[i] += _dripVy[i] * dt;

            // Meander: the drip wobbles a little around the side column as it runs down.
            _dripPhase[i] += dt * 1.7f;
            // Meander only away from the window, so the drip never runs over the frame.
            float wobble = 1.2f * _scale * MathF.Abs(Noise1D.Sample(_dripPhase[i], 11));
            _dripX[i] = _dripColumn[i] + 0.5f + (_dripOutward[i] * ((DripRadius * _scale * 0.5f) + wobble));

            // A new trail point every few pixels of travel.
            int trail = i * TrailLength;
            int count = _trailCount[i];
            if (count == 0 || _dripY[i] - _trailY[trail + ((count - 1) % TrailLength)] >= TrailSpacing * _scale)
            {
                int slot = count % TrailLength;
                _trailX[trail + slot] = _dripX[i];
                _trailY[trail + slot] = _dripY[i];
                _trailCount[i] = count + 1;
            }

            // At the bottom of the window the drip falls freely.
            if (_dripY[i] >= bounds.Bottom)
            {
                ReleaseDrip(i);
                continue;
            }

            i++;
        }
    }

    /// <summary>Turns a drip into a falling near drop and removes it.</summary>
    private void ReleaseDrip(int i)
    {
        if (_drops < MaxDrops)
        {
            int d = _drops++;
            _dropX[d] = _dripX[i];
            _dropY[d] = _dripY[i];
            _dropVx[d] = 0f;
            _dropVy[d] = _dripVy[i];
            _dropZ[d] = 0.98f;
        }

        int last = --_drips;
        _dripX[i] = _dripX[last];
        _dripY[i] = _dripY[last];
        _dripVy[i] = _dripVy[last];
        _dripPhase[i] = _dripPhase[last];
        _dripOwner[i] = _dripOwner[last];
        _dripColumn[i] = _dripColumn[last];
        _dripOutward[i] = _dripOutward[last];
        _trailCount[i] = _trailCount[last];
        Array.Copy(_trailX, last * TrailLength, _trailX, i * TrailLength, TrailLength);
        Array.Copy(_trailY, last * TrailLength, _trailY, i * TrailLength, TrailLength);
    }

    private void ReleaseDrips(long owner)
    {
        int i = 0;
        while (i < _drips)
        {
            if (_dripOwner[i] == owner)
            {
                ReleaseDrip(i);
                continue;
            }

            i++;
        }
    }

    private void ShiftDrips(long owner, float dx, float dy)
    {
        for (int i = 0; i < _drips; i++)
        {
            if (_dripOwner[i] != owner)
            {
                continue;
            }

            _dripX[i] += dx;
            _dripY[i] += dy;
            _dripColumn[i] += (int)dx;
            int trail = i * TrailLength;
            for (int t = 0; t < TrailLength; t++)
            {
                _trailX[trail + t] += dx;
                _trailY[trail + t] += dy;
            }
        }
    }

    /// <summary>A shaken or closed window loses its water: it falls off the edge as drops.</summary>
    private void SlideOff(long owner, PixelRect bounds)
    {
        if (!_puddles.TryGetValue(owner, out WaterField? field))
        {
            return;
        }

        ReadOnlySpan<float> bins = field.Bins;
        for (int b = 0; b < bins.Length && _drops < MaxDrops; b += 2)
        {
            if (bins[b] < 0.05f)
            {
                continue;
            }

            int i = _drops++;
            _dropX[i] = bounds.Left + ((b + 0.5f) * field.BinWidth);
            _dropY[i] = bounds.Top + 2f;
            _dropVx[i] = _random.NextFloat(-30f, 30f) * _scale;
            _dropVy[i] = _random.NextFloat(0f, 120f) * _scale;
            _dropZ[i] = 0.98f;
        }

        field.Drain();
    }

    private void RemoveDrop(int i)
    {
        int last = --_drops;
        _dropX[i] = _dropX[last];
        _dropY[i] = _dropY[last];
        _dropVx[i] = _dropVx[last];
        _dropVy[i] = _dropVy[last];
        _dropZ[i] = _dropZ[last];
    }

    private void EmitPuddles(RenderFrame frame, SimulationContext context, Vector3 color, float brightness)
    {
        float time = (float)context.Time;
        foreach (SurfaceSegment segment in context.Surfaces.Segments)
        {
            if (segment.Key.Edge != SurfaceEdge.Top || !_puddles.TryGetValue(segment.Key.OwnerId, out WaterField? field))
            {
                continue;
            }

            ReadOnlySpan<float> bins = field.Bins;
            int first = field.BinAt(segment.Start - segment.Origin);
            int last = field.BinAt(segment.End - 1 - segment.Origin);
            for (int b = first; b <= last; b++)
            {
                float water = bins[b];
                if (water < 0.02f)
                {
                    continue;
                }

                // Clip the bin to the visible part of the edge.
                float left = MathF.Max(segment.Origin + (b * field.BinWidth), segment.Start);
                float right = MathF.Min(segment.Origin + ((b + 1) * field.BinWidth), segment.End);
                if (right <= left)
                {
                    continue;
                }

                // A thin film, thicker where more water stands, shimmering slowly.
                float height = (1f + (2.5f * water)) * _scale;
                float shimmer = 0.85f + (0.15f * Noise1D.Sample((time * 1.3f) + (b * 0.37f), 5));
                float alpha = MathF.Min(1f, (0.25f + (0.45f * water)) * shimmer * brightness);
                var center = new Vector2((left + right) / 2f, segment.Position - (height / 2f));
                frame.Add(new Sprite(center, new Vector2((right - left) / 2f, height / 2f), 0f, SpriteShape.Rectangle, Sprite.Premultiply(color, alpha)));

                // Highlight along the water surface.
                var highlight = new Vector2(center.X, segment.Position - height);
                frame.Add(new Sprite(highlight, new Vector2((right - left) / 2f, 0.5f * _scale), 0f, SpriteShape.Rectangle, Sprite.Premultiply(Vector3.One, MathF.Min(1f, 0.35f * water * shimmer * brightness))));
            }
        }
    }

    private void EmitDrips(RenderFrame frame, Vector3 color, float brightness)
    {
        for (int i = 0; i < _drips; i++)
        {
            int trail = i * TrailLength;
            int count = Math.Min(_trailCount[i], TrailLength);
            int newest = _trailCount[i] - 1;

            // Trail: segments between remembered points, fading towards the oldest.
            Vector2 previous = new(_dripX[i], _dripY[i]);
            for (int n = 0; n < count; n++)
            {
                int slot = (newest - n) % TrailLength;
                if (slot < 0)
                {
                    slot += TrailLength;
                }

                var point = new Vector2(_trailX[trail + slot], _trailY[trail + slot]);
                Vector2 delta = previous - point;
                float length = delta.Length();
                if (length > 0.5f)
                {
                    float alpha = 0.45f * (1f - (n / (float)TrailLength)) * brightness;
                    frame.Add(new Sprite(
                        (previous + point) / 2f,
                        new Vector2((length / 2f) + (0.8f * _scale), 0.8f * _scale),
                        MathF.Atan2(delta.Y, delta.X),
                        SpriteShape.Streak,
                        Sprite.Premultiply(color, MathF.Min(1f, alpha))));
                }

                previous = point;
            }

            frame.Add(new Sprite(new Vector2(_dripX[i], _dripY[i]), new Vector2(DripRadius, 2.2f) * _scale, 0f, SpriteShape.Disc, Sprite.Premultiply(color, MathF.Min(1f, 0.85f * brightness))));
        }
    }

    private void EmitRipples(RenderFrame frame, Vector3 color, float brightness)
    {
        for (int i = 0; i < _ripples; i++)
        {
            float t = _rippleAge[i] / 0.3f;
            float size = _rippleSize[i] * (0.4f + (0.6f * t));
            float alpha = MathF.Min(1f, 0.6f * (1f - t) * brightness);
            frame.Add(new Sprite(new Vector2(_rippleX[i], _rippleY[i]), new Vector2(size, size * 0.3f), 0f, SpriteShape.Ring, Sprite.Premultiply(color, alpha)));
        }
    }

    private void EmitSplashes(RenderFrame frame, Vector3 color, float brightness)
    {
        for (int i = 0; i < _splashes; i++)
        {
            float alpha = MathF.Min(1f, MathF.Min(1f, _splashLife[i] / 0.15f) * 0.7f * brightness);
            frame.Add(new Sprite(new Vector2(_splashX[i], _splashY[i]), new Vector2(1.1f, 1.1f) * _scale, 0f, SpriteShape.Disc, Sprite.Premultiply(color, alpha)));
        }
    }

    private void EmitDrops(RenderFrame frame, SimulationContext context, Vector3 color, float brightness)
    {
        float threshold = 1f - Math.Clamp(Depth, 0f, 1f);
        ReadOnlySpan<TrackedWindow> windows = context.Windows.Windows;
        for (int i = 0; i < _drops; i++)
        {
            float z = _dropZ[i];

            // Far drops fall behind the windows: hidden where a window covers them. This keeps the depth
            // readable and leaves the user's content clear.
            if (z < threshold && IsBehindAWindow(windows, _dropX[i], _dropY[i]))
            {
                continue;
            }

            var velocity = new Vector2(_dropVx[i], _dropVy[i]);
            float speed = velocity.Length();

            // Motion-stretched: length grows with speed and nearness, width and opacity with nearness.
            float radius = (0.5f + (0.9f * z)) * _scale;
            float halfLength = MathF.Max(radius, speed * 0.009f * (0.6f + (0.4f * z)));
            float alpha = MathF.Min(1f, (0.16f + (0.42f * z)) * brightness);
            // The drop's position is the tip of the streak; the tail trails behind along the flight direction.
            Vector2 direction = speed > 0.001f ? velocity / speed : Vector2.UnitY;
            frame.Add(new Sprite(
                new Vector2(_dropX[i], _dropY[i]) - (direction * halfLength),
                new Vector2(halfLength, radius),
                MathF.Atan2(_dropVy[i], _dropVx[i]),
                SpriteShape.Streak,
                Sprite.Premultiply(color, alpha)));
        }
    }
}
