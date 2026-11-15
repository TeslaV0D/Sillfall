using System.Numerics;
using System.Runtime.CompilerServices;
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

    /// <summary>Smallest raindrop diameter in millimetres.</summary>
    private const float MinDiameter = 0.5f;

    /// <summary>Largest raindrop diameter in millimetres.</summary>
    private const float MaxDiameter = 4.0f;

    /// <summary>
    /// Screen pixels per metre of real fall. Chosen so a typical (~1 mm) mid-depth drop still falls
    /// around 1200 px/s like the pre-v2 rain; a rare near 2 mm drop is then a bit faster than 1500 px/s.
    /// </summary>
    private const float PixelsPerMeter = 380f;

    /// <summary>Wind response rate for a 1 mm drop, in 1/s. Larger drops drag more slowly: rate / d.</summary>
    private const float DragRate = 6f;

    /// <summary>Horizontal height bands that share one turbulence sample per step.</summary>
    private const int GustBands = 32;

    /// <summary>Share of wind speed added as band turbulence when wind is blowing.</summary>
    private const float Turbulence = 0.25f;

    /// <summary>Mean diameter used for the wind-side spawn margin (mid-range drop).</summary>
    private const float MeanSpawnDiameter = 1.4f;

    /// <summary>Splash droplets spawned per unit of impact energy (d³ · (vy/scale)²).</summary>
    private const float SplashPerEnergy = 0.55f;

    /// <summary>Base splash launch speed scale at 100 % DPI, before the sqrt(vy) factor.</summary>
    private const float SplashSpeed = 38f;

    /// <summary>Water depth (bin units) above which the edge counts as wet for splash/ripple scaling.</summary>
    private const float WetEdgeThreshold = 0.4f;

    /// <summary>Diameter in mm above which a Worthington jet can form on a wet edge.</summary>
    private const float JetDiameter = 2.5f;

    /// <summary>Base pinning mass in bin units; roughness scales friction around this. Above WaterPerDrip so a fresh drip often sticks until fed.</summary>
    private const float PinMass = 1.65f;

    /// <summary>Drip speed in px/s per excess bin-unit of mass above friction, at 100 % scaling.</summary>
    private const float DripSpeedPerMass = 110f;

    /// <summary>Mass left behind in the trail per px of travel (bin units / px at 100 %).</summary>
    private const float TrailLoss = 0.0035f;

    /// <summary>Below this mass a stuck drip can evaporate after <see cref="DripEvaporateAfter"/>.</summary>
    private const float MinMass = 0.35f;

    /// <summary>Seconds a low-mass drip may sit still before it evaporates.</summary>
    private const float DripEvaporateAfter = 6f;

    /// <summary>Mass lost per second while a drip is pinned (pane evaporation).</summary>
    private const float DripIdleEvaporation = 0.12f;

    /// <summary>How long a trail point stays visible, in seconds.</summary>
    private const float TrailLifetime = 2.5f;

    /// <summary>Feed into an existing drip if its head is still within this many px of the top (at 100 %).</summary>
    private const float DripMergeDistance = 8f;

    private static readonly Vector3 s_rainColor = new(0.78f, 0.85f, 0.95f);
    private static readonly Vector3 s_waterColor = new(0.80f, 0.88f, 1f);
    private static readonly Vector3 s_farTint = new(0.88f, 0.92f, 0.98f);

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

    /// <summary>Drop diameter in millimetres.</summary>
    private readonly float[] _dropSize = new float[MaxDrops];

    /// <summary>Wind adaptation rate in 1/s, precomputed as <see cref="DragRate"/> / diameter.</summary>
    private readonly float[] _dropDrag = new float[MaxDrops];

    /// <summary>Cached terminal fall speed in m/s from <see cref="TerminalVelocity"/>.</summary>
    private readonly float[] _dropVt = new float[MaxDrops];

    /// <summary>Window a drop came from (sliding off, a corner stream, a released drip), or none (<see cref="WindowOcclusion"/>).</summary>
    private readonly long[] _dropHome = new long[MaxDrops];
    private int _drops;

    // Splash droplets.
    private readonly float[] _splashX = new float[MaxSplashes];
    private readonly float[] _splashY = new float[MaxSplashes];
    private readonly float[] _splashVx = new float[MaxSplashes];
    private readonly float[] _splashVy = new float[MaxSplashes];
    private readonly float[] _splashLife = new float[MaxSplashes];

    /// <summary>Initial lifetime of a splash, so Emit can fade by life / startLife.</summary>
    private readonly float[] _splashStartLife = new float[MaxSplashes];

    /// <summary>Row of the surface a splash came from. Falling back to it, the droplet is gone.</summary>
    private readonly float[] _splashFloor = new float[MaxSplashes];

    /// <summary>Window whose top edge a splash jumped from, or none for the floor.</summary>
    private readonly long[] _splashHome = new long[MaxSplashes];
    private int _splashes;

    /// <summary>Splash droplets created in the last simulation step (tests).</summary>
    internal int LastStepSplashDroplets { get; private set; }

    /// <summary>Hits recorded in the last simulation step (tests).</summary>
    internal int LastStepHits { get; private set; }

    /// <summary>Horizontal velocities of live splash droplets in px/s (tests).</summary>
    internal ReadOnlySpan<float> SplashVx => _splashVx.AsSpan(0, _splashes);

    /// <summary>Mass of live drips in bin units (tests).</summary>
    internal ReadOnlySpan<float> DripMasses => _dripMass.AsSpan(0, _drips);

    /// <summary>Vertical speeds of live drips in px/s (tests).</summary>
    internal ReadOnlySpan<float> DripVelocities => _dripVy.AsSpan(0, _drips);

    /// <summary>Desktop Y of live drip heads (tests).</summary>
    internal ReadOnlySpan<float> DripYs => _dripY.AsSpan(0, _drips);

    /// <summary>How often a drip slowed to a stop while still on the frame (tests).</summary>
    internal int DripStickEvents { get; private set; }

    // Ripples where drops hit.
    private readonly float[] _rippleX = new float[MaxRipples];
    private readonly float[] _rippleY = new float[MaxRipples];
    private readonly float[] _rippleAge = new float[MaxRipples];
    private readonly float[] _rippleSize = new float[MaxRipples];
    private readonly long[] _rippleHome = new long[MaxRipples];
    private int _ripples;

    // Drips running down window sides, each with a trail.
    private readonly float[] _dripX = new float[MaxDrips];
    private readonly float[] _dripY = new float[MaxDrips];
    private readonly float[] _dripVy = new float[MaxDrips];
    private readonly float[] _dripPhase = new float[MaxDrips];
    private readonly float[] _dripMass = new float[MaxDrips];
    private readonly float[] _dripIdle = new float[MaxDrips];
    private readonly bool[] _dripWasSliding = new bool[MaxDrips];
    private readonly long[] _dripOwner = new long[MaxDrips];
    private readonly int[] _dripColumn = new int[MaxDrips];
    private readonly int[] _dripOutward = new int[MaxDrips];
    private readonly float[] _trailX = new float[MaxDrips * TrailLength];
    private readonly float[] _trailY = new float[MaxDrips * TrailLength];
    private readonly float[] _trailTime = new float[MaxDrips * TrailLength];
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

    /// <summary>Diameters of the live drops in millimetres (tests).</summary>
    internal ReadOnlySpan<float> DropSizes => _dropSize.AsSpan(0, _drops);

    /// <summary>Horizontal velocities of the live drops in px/s (tests).</summary>
    internal ReadOnlySpan<float> DropVx => _dropVx.AsSpan(0, _drops);

    /// <summary>Wind drag rates of the live drops in 1/s (tests).</summary>
    internal ReadOnlySpan<float> DropDrag => _dropDrag.AsSpan(0, _drops);

    /// <summary>
    /// The water field of a window's top edge (<see cref="WaterField.IsDry"/> until the first drop), or
    /// <see langword="null"/> while the edge is not exposed.
    /// </summary>
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

    /// <summary>Every exposed top edge gets its water field now, not on its first hit in the frame loop (F-02).</summary>
    public void OnSurfacesChanged(SimulationContext context)
    {
        ArgumentNullException.ThrowIfNull(context);
        foreach (SurfaceSegment segment in context.Surfaces.Segments)
        {
            if (segment.Key.Edge == SurfaceEdge.Top)
            {
                FieldFor(segment.Key.OwnerId, context);
            }
        }
    }

    public void Emit(RenderFrame frame, SimulationContext context)
    {
        float brightness = Settings.ClampedBrightness;
        _scale = context.PixelScale;
        Vector3 rainColor = Settings.Color ?? s_rainColor;
        Vector3 waterColor = Settings.Color ?? s_waterColor;

        ReadOnlySpan<TrackedWindow> windows = context.Windows.Windows;
        EmitPuddles(frame, context, waterColor, brightness);
        EmitDrips(frame, windows, waterColor, brightness, (float)context.Time);
        EmitRipples(frame, windows, waterColor, brightness);
        EmitSplashes(frame, windows, rainColor, brightness);
        EmitDrops(frame, windows, rainColor, brightness);
    }

    /// <summary>Terminal velocity in m/s for a drop of diameter <paramref name="d"/> in mm (Atlas et al. 1973).</summary>
    internal static float TerminalVelocity(float d) => MathF.Max(9.65f - (10.3f * MathF.Exp(-0.6f * d)), 1f);

    /// <summary>
    /// Mean Marshall–Palmer diameter in mm for rain of the given intensity (0..1).
    /// Kept near 1 mm so large drops (d &gt; 3 mm) stay under about 5 % even in heavy rain.
    /// </summary>
    internal static float MeanDropDiameter(float intensity) => 0.85f + (0.2f * Math.Clamp(intensity, 0f, 1f));

    /// <summary>Depth parallax: distant drops fall and look smaller.</summary>
    private static float Parallax(float z) => 0.55f + (0.45f * z);

    /// <summary>Screen fall speed in px/s for a drop of diameter <paramref name="dMm"/> at depth <paramref name="z"/> and pixel scale.</summary>
    private static float ScreenFallSpeed(float dMm, float z, float scale) =>
        TerminalVelocity(dMm) * PixelsPerMeter * Parallax(z) * scale;

    private static float Smoothstep(float edge0, float edge1, float x)
    {
        float t = Math.Clamp((x - edge0) / (edge1 - edge0), 0f, 1f);
        return t * t * (3f - (2f * t));
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
            if (!field.IsDry && (field.Total > 0f || field.LeftRunoff > 0f || field.RightRunoff > 0f))
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
        float fallTime = desktop.Height / MathF.Max(ScreenFallSpeed(MeanSpawnDiameter, 0.5f, scale), 1f);
        float margin = MathF.Abs(windX) * fallTime;
        float left = desktop.Left - (windX > 0 ? margin : 0f);
        float width = desktop.Width + margin;
        float mean = MeanDropDiameter(intensity);

        _spawnDebt += MathF.Pow(intensity, 1.2f) * SpawnRatePer1000Pixels * (width / 1000f) * dt;
        while (_spawnDebt >= 1f && _drops < MaxDrops)
        {
            _spawnDebt -= 1f;
            int i = _drops++;
            float z = _random.NextFloat();
            float d = Math.Clamp(-MathF.Log(1f - (_random.NextFloat() * 0.999f)) * mean, MinDiameter, MaxDiameter);
            float vt = TerminalVelocity(d);
            _dropZ[i] = z;
            _dropSize[i] = d;
            _dropVt[i] = vt;
            _dropDrag[i] = DragRate / d;
            _dropX[i] = left + (_random.NextFloat() * width);
            _dropY[i] = desktop.Top - (_random.NextFloat() * 60f * scale);
            _dropVy[i] = vt * PixelsPerMeter * Parallax(z) * scale * _random.NextFloat(0.9f, 1f);
            _dropVx[i] = windX * (0.5f + (0.5f * z));
            _dropHome[i] = WindowOcclusion.NoHome;
        }

        if (_drops == MaxDrops)
        {
            _spawnDebt = 0f;
        }
    }

    [SkipLocalsInit]
    private int SimulateDrops(float dt, SimulationContext context)
    {
        float threshold = 1f - Math.Clamp(Depth, 0f, 1f);
        float scale = context.PixelScale;
        float windX = context.Wind.Current.X * scale;
        float bottom = context.DesktopBounds.Bottom + 20f;
        float top = context.DesktopBounds.Top;
        float time = (float)context.Time;
        int hits = 0;
        LastStepSplashDroplets = 0;

        Span<float> gusts = stackalloc float[GustBands];
        float bandHeight = MathF.Max(context.DesktopBounds.Height / (float)GustBands, 1f);
        float gustScale = Turbulence * MathF.Abs(windX);
        for (int b = 0; b < GustBands; b++)
        {
            gusts[b] = Noise1D.Sample((b * 0.35f) + (time * 0.8f), 23) * gustScale;
        }

        int i = 0;
        while (i < _drops)
        {
            float z = _dropZ[i];
            float screenVt = _dropVt[i] * PixelsPerMeter * Parallax(z) * scale;
            _dropVy[i] = MathF.Min(_dropVy[i] + (Gravity * scale * dt), screenVt);
            int band = Math.Clamp((int)((_dropY[i] - top) / bandHeight), 0, GustBands - 1);
            float targetVx = (windX * (0.5f + (0.5f * z))) + gusts[band];
            _dropVx[i] += (targetVx - _dropVx[i]) * MathF.Min(1f, _dropDrag[i] * dt);

            float nextX = _dropX[i] + (_dropVx[i] * dt);
            float nextY = _dropY[i] + (_dropVy[i] * dt);

            if (z >= threshold && context.SurfaceIndex.TryFindLanding(nextX, _dropY[i], nextY, out SurfaceHit hit))
            {
                SurfaceSegment segment = context.Surfaces.Segments[hit.Segment];
                Hit(nextX, hit.Row, z, _dropSize[i], _dropVx[i], _dropVy[i], segment, context, scale);
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

        LastStepHits = hits;
        return hits;
    }

    private void Hit(float x, int row, float z, float d, float vx, float vy, in SurfaceSegment segment, SimulationContext context, float scale)
    {
        long home = segment.Key.Edge == SurfaceEdge.Top ? segment.Key.OwnerId : WindowOcclusion.NoHome;
        float water = 0f;
        if (segment.Key.Edge == SurfaceEdge.Top)
        {
            WaterField field = FieldFor(segment.Key.OwnerId, context);
            float localX = x - segment.Origin;
            water = field.Bins[field.BinAt(localX)];
            field.Add(localX, WaterPerHit * (0.5f + z));
        }

        bool wet = water > WetEdgeThreshold;
        // Normalize by a typical near-drop screen speed so energy stays O(1) across pixel scales.
        float fall = MathF.Max(vy / (2000f * scale), 0f);
        float energy = d * d * d * fall * fall;
        float expected = energy * SplashPerEnergy;
        if (wet)
        {
            expected *= 0.6f;
        }

        int droplets = Math.Clamp((int)(expected + _random.NextFloat()), 0, 4);
        float speed = SplashSpeed * MathF.Sqrt(MathF.Max(vy / scale, 0f)) * scale;
        for (int n = 0; n < droplets && _splashes < MaxSplashes; n++)
        {
            SpawnSplashDroplet(x, row, home, scale, speed, vx, upward: false);
        }

        // Worthington jet: a large drop into standing water throws one droplet straight up.
        if (d > JetDiameter && wet && _splashes < MaxSplashes)
        {
            SpawnSplashDroplet(x, row, home, scale, speed * 0.5f, vx: 0f, upward: true);
            droplets++;
        }

        LastStepSplashDroplets += droplets;

        if (_ripples < MaxRipples)
        {
            int r = _ripples++;
            float rippleScale = wet ? 1.6f : 0.6f;
            _rippleX[r] = x;
            _rippleY[r] = row - scale;
            _rippleAge[r] = 0f;
            _rippleSize[r] = (3f + (4f * z)) * scale * rippleScale;
            _rippleHome[r] = home;
        }
    }

    /// <summary>Crown lobe left/right, or a vertical Worthington jet when <paramref name="upward"/>.</summary>
    private void SpawnSplashDroplet(float x, int row, long home, float scale, float speed, float vx, bool upward)
    {
        int s = _splashes++;
        float life = _random.NextFloat(0.15f, 0.35f);
        _splashX[s] = x;
        _splashY[s] = row - scale;
        _splashFloor[s] = row - scale;
        _splashLife[s] = life;
        _splashStartLife[s] = life;
        _splashHome[s] = home;

        if (upward)
        {
            _splashVx[s] = 0.25f * vx;
            _splashVy[s] = -speed * _random.NextFloat(0.6f, 1f);
            return;
        }

        // Two lobes left/right of the vertical: 20°–65° from the horizontal, upward.
        float side = _random.NextFloat() < 0.5f ? -1f : 1f;
        float fromHorizontal = _random.NextFloat(20f, 65f) * (MathF.PI / 180f);
        float v = speed * _random.NextFloat(0.6f, 1f);
        _splashVx[s] = (side * MathF.Cos(fromHorizontal) * v) + (0.25f * vx);
        _splashVy[s] = -MathF.Sin(fromHorizontal) * v;
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
        float drag = MathF.Max(0f, 1f - (1.5f * dt));
        int i = 0;
        while (i < _splashes)
        {
            _splashLife[i] -= dt;
            _splashVx[i] *= drag;
            _splashVy[i] = (_splashVy[i] * drag) + (Gravity * _scale * dt);
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
                _splashStartLife[i] = _splashStartLife[last];
                _splashFloor[i] = _splashFloor[last];
                _splashHome[i] = _splashHome[last];
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
                _rippleHome[i] = _rippleHome[last];
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

            WaterField field = entry.Value;
            if (field.IsDry)
            {
                continue;
            }

            PixelRect bounds = context.Windows[index].Bounds;
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
                SpawnSheet(entry.Key, bounds.Left - 1, -1, bounds.Top, rate * dt);
                SpawnSheet(entry.Key, bounds.Right, 1, bounds.Top, rate * dt);
            }
        }

        foreach (long id in _staleFields)
        {
            _puddles.Remove(id);
        }
    }

    /// <summary>A thin stream from a corner: small near drops that start slowly and accelerate.</summary>
    /// <param name="home">Window the stream pours from.</param>
    /// <param name="column">Side column just outside the frame.</param>
    /// <param name="outward">-1 for the left corner, +1 for the right one: the stream stays outside the window.</param>
    /// <param name="y">Row the stream starts at.</param>
    /// <param name="expected">Drops expected in this step.</param>
    private void SpawnSheet(long home, int column, int outward, float y, float expected)
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
            AssignDropSize(i, MeanSpawnDiameter);
            _dropX[i] = column + 0.5f + (outward * _random.NextFloat(0f, 1.5f));
            _dropY[i] = y + 1f;
            _dropVx[i] = 0f;
            _dropVy[i] = _random.NextFloat(20f, 80f) * _scale;

            // Just below "near": falls past the window it pours from. Over other windows it is hidden (gutter streams end
            // at the first window below, finding F-05).
            _dropZ[i] = 0.98f;
            _dropHome[i] = home;
        }
    }

    /// <param name="owner">Window the drip runs down.</param>
    /// <param name="column">Side column just outside the frame.</param>
    /// <param name="outward">-1 for the left side, +1 for the right side: the drip hangs away from the window.</param>
    /// <param name="top">Row the drip starts at.</param>
    private void SpawnDrip(long owner, int column, int outward, int top)
    {
        // Feed an existing drip still hanging near this corner instead of starting a parallel rivulet.
        float mergeLimit = top + (DripMergeDistance * _scale);
        for (int existing = 0; existing < _drips; existing++)
        {
            if (_dripOwner[existing] == owner && _dripColumn[existing] == column && _dripY[existing] <= mergeLimit)
            {
                _dripMass[existing] += WaterPerDrip;
                _dripIdle[existing] = 0f;
                return;
            }
        }

        if (_drips >= MaxDrips)
        {
            return;
        }

        int i = _drips++;
        _dripOutward[i] = outward;
        _dripX[i] = column + 0.5f + (outward * DripRadius * _scale * 0.5f);
        _dripY[i] = top;
        _dripVy[i] = 0f;
        _dripPhase[i] = _random.NextFloat(0f, 100f);
        _dripMass[i] = WaterPerDrip;
        _dripIdle[i] = 0f;
        _dripWasSliding[i] = false;
        _dripOwner[i] = owner;
        _dripColumn[i] = column;
        _trailCount[i] = 0;
    }

    private void SimulateDrips(float dt, SimulationContext context)
    {
        float time = (float)context.Time;
        int i = 0;
        while (i < _drips)
        {
            int index = context.Windows.IndexOf(_dripOwner[i]);
            if (index < 0)
            {
                DiscardDrip(i);
                continue;
            }

            PixelRect bounds = context.Windows[index].Bounds;
            // Stick-slip: roughness along the frame pins light drips until enough mass builds up.
            float roughness = 0.5f + (0.5f * Noise1D.Sample((_dripY[i] / _scale) * 0.05f, 31));
            float friction = PinMass * (0.7f + (0.6f * roughness));
            float targetV = MathF.Max(0f, _dripMass[i] - friction) * DripSpeedPerMass * _scale;
            targetV = MathF.Min(targetV, DripMaxSpeed * _scale);
            bool sliding = targetV > 1f * _scale;
            if (_dripWasSliding[i] && !sliding && _dripY[i] > bounds.Top + (20f * _scale) && _dripY[i] < bounds.Bottom - (10f * _scale))
            {
                DripStickEvents++;
            }

            _dripWasSliding[i] = sliding;
            _dripVy[i] += (targetV - _dripVy[i]) * MathF.Min(1f, 8f * dt);
            if (_dripVy[i] > 0f)
            {
                _dripMass[i] -= TrailLoss * _dripVy[i] * dt / _scale;
                if (_dripMass[i] < 0f)
                {
                    _dripMass[i] = 0f;
                }
            }

            _dripY[i] += _dripVy[i] * dt;

            if (_dripVy[i] < 1f * _scale)
            {
                _dripIdle[i] += dt;
                _dripMass[i] = MathF.Max(0f, _dripMass[i] - (DripIdleEvaporation * dt));
                if (_dripMass[i] < MinMass && _dripIdle[i] > DripEvaporateAfter)
                {
                    DiscardDrip(i);
                    continue;
                }
            }
            else
            {
                _dripIdle[i] = 0f;
            }

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
                _trailTime[trail + slot] = time;
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
            AssignDropSize(d, Math.Clamp(1.5f + _dripMass[i], 1.5f, 4f));
            _dropX[d] = _dripX[i];
            _dropY[d] = _dripY[i];
            _dropVx[d] = 0f;
            _dropVy[d] = MathF.Max(_dripVy[i], 40f * _scale);
            _dropZ[d] = 0.98f;
            _dropHome[d] = _dripOwner[i];
        }

        SwapRemoveDrip(i);
    }

    /// <summary>Removes a drip without shedding a falling drop (evaporation or a vanished window).</summary>
    private void DiscardDrip(int i) => SwapRemoveDrip(i);

    private void SwapRemoveDrip(int i)
    {
        int last = --_drips;
        _dripX[i] = _dripX[last];
        _dripY[i] = _dripY[last];
        _dripVy[i] = _dripVy[last];
        _dripPhase[i] = _dripPhase[last];
        _dripMass[i] = _dripMass[last];
        _dripIdle[i] = _dripIdle[last];
        _dripWasSliding[i] = _dripWasSliding[last];
        _dripOwner[i] = _dripOwner[last];
        _dripColumn[i] = _dripColumn[last];
        _dripOutward[i] = _dripOutward[last];
        _trailCount[i] = _trailCount[last];
        Array.Copy(_trailX, last * TrailLength, _trailX, i * TrailLength, TrailLength);
        Array.Copy(_trailY, last * TrailLength, _trailY, i * TrailLength, TrailLength);
        Array.Copy(_trailTime, last * TrailLength, _trailTime, i * TrailLength, TrailLength);
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
            AssignDropSize(i, MeanSpawnDiameter);
            _dropX[i] = bounds.Left + ((b + 0.5f) * field.BinWidth);
            _dropY[i] = bounds.Top + 2f;
            _dropVx[i] = _random.NextFloat(-30f, 30f) * _scale;
            _dropVy[i] = _random.NextFloat(0f, 120f) * _scale;
            _dropZ[i] = 0.98f;
            _dropHome[i] = owner;
        }

        field.Drain();
    }

    private void AssignDropSize(int i, float diameterMm)
    {
        float d = Math.Clamp(diameterMm, MinDiameter, MaxDiameter);
        _dropSize[i] = d;
        _dropVt[i] = TerminalVelocity(d);
        _dropDrag[i] = DragRate / d;
    }

    private void RemoveDrop(int i)
    {
        int last = --_drops;
        _dropX[i] = _dropX[last];
        _dropY[i] = _dropY[last];
        _dropVx[i] = _dropVx[last];
        _dropVy[i] = _dropVy[last];
        _dropZ[i] = _dropZ[last];
        _dropSize[i] = _dropSize[last];
        _dropDrag[i] = _dropDrag[last];
        _dropVt[i] = _dropVt[last];
        _dropHome[i] = _dropHome[last];
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

                float leftWater = b > 0 ? 0.5f * (field.DisplayHeight(b - 1) + field.DisplayHeight(b)) : field.DisplayHeight(b);
                float rightWater = b < bins.Length - 1 ? 0.5f * (field.DisplayHeight(b) + field.DisplayHeight(b + 1)) : field.DisplayHeight(b);
                float hL = (1f + (2.5f * leftWater)) * _scale;
                float hR = (1f + (2.5f * rightWater)) * _scale;
                float height = MathF.Max(hL, hR) + _scale;
                float shimmer = 0.85f + (0.15f * Noise1D.Sample((time * 1.3f) + (b * 0.37f), 5));
                float alpha = MathF.Min(1f, (0.25f + (0.45f * water)) * shimmer * brightness);
                var center = new Vector2((left + right) / 2f, segment.Position - (height / 2f));
                frame.Add(new Sprite(center, new Vector2((right - left) / 2f, height / 2f), 0f, SpriteShape.WaterFilm, Sprite.Premultiply(color, alpha))
                {
                    Variant = hL,
                    Param = hR,
                });
            }
        }
    }

    private void EmitDrips(RenderFrame frame, ReadOnlySpan<TrackedWindow> windows, Vector3 color, float brightness, float time)
    {
        for (int i = 0; i < _drips; i++)
        {
            // A drip runs just outside its window's side: hidden where a window in front of it covers that column.
            if (WindowOcclusion.HidesAttached(windows, _dripX[i], _dripY[i], _dripOwner[i]))
            {
                continue;
            }

            int trail = i * TrailLength;
            int count = Math.Min(_trailCount[i], TrailLength);
            int newest = _trailCount[i] - 1;

            // Trail: segments between remembered points, fading by age and tapering towards the oldest end.
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
                float age = time - _trailTime[trail + slot];
                if (length > 0.5f && age < TrailLifetime && !WindowOcclusion.HidesAttached(windows, point.X, point.Y, _dripOwner[i]))
                {
                    float alpha = 0.45f * (1f - (age / TrailLifetime)) * brightness;
                    float halfWidth = (0.8f - (0.4f * (n / (float)Math.Max(count - 1, 1)))) * _scale;
                    frame.Add(new Sprite(
                        (previous + point) / 2f,
                        new Vector2((length / 2f) + halfWidth, halfWidth),
                        MathF.Atan2(delta.Y, delta.X),
                        SpriteShape.Streak,
                        Sprite.Premultiply(color, MathF.Min(1f, alpha))));
                }

                previous = point;
            }

            float mass = _dripMass[i];
            float radius = DripRadius * (0.8f + (0.4f * MathF.Sqrt(MathF.Max(mass, 0f)))) * _scale;
            float halfLength = 1.5f * radius;
            // Head points down the frame (+π/2 with +x = head for Raindrop).
            frame.Add(new Sprite(
                new Vector2(_dripX[i], _dripY[i]),
                new Vector2(halfLength, radius),
                MathF.PI / 2f,
                SpriteShape.Raindrop,
                Sprite.Premultiply(color, MathF.Min(1f, 0.85f * brightness)))
            {
                Param = 0.7f,
            });
        }
    }

    private void EmitRipples(RenderFrame frame, ReadOnlySpan<TrackedWindow> windows, Vector3 color, float brightness)
    {
        for (int i = 0; i < _ripples; i++)
        {
            if (WindowOcclusion.HidesAttached(windows, _rippleX[i], _rippleY[i], _rippleHome[i]))
            {
                continue;
            }

            float t = _rippleAge[i] / 0.3f;
            float size = _rippleSize[i] * (0.4f + (0.6f * t));
            float alpha = MathF.Min(1f, 0.6f * (1f - t) * brightness);
            float thickness = 1f - (0.6f * t); // 1.0 px → 0.4 px half-thickness via Param
            frame.Add(new Sprite(new Vector2(_rippleX[i], _rippleY[i]), new Vector2(size, size * 0.3f), 0f, SpriteShape.Ring, Sprite.Premultiply(color, alpha))
            {
                Param = thickness,
            });
        }
    }

    private void EmitSplashes(RenderFrame frame, ReadOnlySpan<TrackedWindow> windows, Vector3 color, float brightness)
    {
        for (int i = 0; i < _splashes; i++)
        {
            // Splashes on the floor below a maximized window would jump into its content: hidden there.
            if (WindowOcclusion.HidesAttached(windows, _splashX[i], _splashY[i], _splashHome[i]))
            {
                continue;
            }

            var velocity = new Vector2(_splashVx[i], _splashVy[i]);
            float speed = velocity.Length();
            float lifeFade = _splashStartLife[i] > 0f ? Math.Clamp(_splashLife[i] / _splashStartLife[i], 0f, 1f) : 0f;
            float alpha = MathF.Min(1f, lifeFade * 0.7f * brightness);
            float radius = (0.7f + (0.3f * lifeFade)) * _scale;
            float halfLength = MathF.Max(radius, speed * 0.012f);
            Vector2 direction = speed > 0.001f ? velocity / speed : -Vector2.UnitY;
            frame.Add(new Sprite(
                new Vector2(_splashX[i], _splashY[i]) - (direction * halfLength * 0.35f),
                new Vector2(halfLength, radius),
                MathF.Atan2(_splashVy[i], _splashVx[i]),
                SpriteShape.Raindrop,
                Sprite.Premultiply(color, alpha))
            {
                Param = 0.5f,
                Variant = 1f, // marks splash droplets for tests / occlusion sampling
            });
        }
    }

    private void EmitDrops(RenderFrame frame, ReadOnlySpan<TrackedWindow> windows, Vector3 color, float brightness)
    {
        bool customColor = Settings.Color.HasValue;
        for (int i = 0; i < _drops; i++)
        {
            float z = _dropZ[i];

            // Far drops fall behind the windows. Near drops only hit top edges: over a window that is not their own they
            // are hidden too, so no rain falls across a window's content (maximized, at the top of the screen, top edge
            // covered; finding F-05). This keeps the depth readable and leaves the user's content clear.
            if (WindowOcclusion.HidesFalling(windows, _dropX[i], _dropY[i], _dropHome[i]))
            {
                continue;
            }

            var velocity = new Vector2(_dropVx[i], _dropVy[i]);
            float speed = velocity.Length();
            float d = _dropSize[i];

            float radius = (0.35f + (0.3f * d)) * (0.45f + (0.55f * z)) * _scale;
            float minRadius = 0.6f * _scale;
            float alphaScale = 1f;
            if (radius < minRadius)
            {
                alphaScale = radius / minRadius;
                radius = minRadius;
            }

            float halfLength = MathF.Max(radius, speed * 0.009f * (0.6f + (0.4f * z)));
            float alpha = MathF.Min(1f, (0.16f + (0.42f * z)) * brightness * alphaScale);
            Vector3 tint = customColor ? color : Vector3.Lerp(s_farTint, color, z);
            float rim = Smoothstep(0.4f, 0.9f, z);
            Vector2 direction = speed > 0.001f ? velocity / speed : Vector2.UnitY;
            frame.Add(new Sprite(
                new Vector2(_dropX[i], _dropY[i]) - (direction * halfLength),
                new Vector2(halfLength, radius),
                MathF.Atan2(_dropVy[i], _dropVx[i]),
                SpriteShape.Raindrop,
                Sprite.Premultiply(tint, alpha))
            {
                Param = rim,
            });
        }
    }
}
