using System.Numerics;
using Weatherly.Core.Desktop;
using Weatherly.Core.Geometry;
using Weatherly.Core.Rendering;
using Weatherly.Core.Simulation;
using Weatherly.Core.Surfaces;

namespace Weatherly.Core.Effects.Grass;

/// <summary>
/// Grass (PLAN.md, section 4.6): blades grow on window top edges and on the floor, faster in the rain, and sway
/// in the wind; a passing cursor pushes them aside. The grass moves with its window. When a window closes its
/// grass is not thrown off but fades out quickly; when the grass is switched off it shrinks away. Each surface
/// gets its strip of blades once (allocation only then and when a window grows wider); stepping and drawing
/// allocate nothing.
/// </summary>
public sealed class GrassEffect : IEffectSystem
{
    /// <summary>Tallest blade in pixels at 100 % (PLAN.md: about 18 px x DPI).</summary>
    public const float MaxBladeHeight = 18f;

    /// <summary>Growth in pixels per second at 100 % and full intensity, without rain.</summary>
    public const float GrowthRate = 0.8f;

    /// <summary>Seconds the grass of a closed window takes to fade out.</summary>
    public const float FadeSeconds = 0.6f;

    /// <summary>Seconds switched-off grass takes to shrink away.</summary>
    public const float WitherSeconds = 3f;

    /// <summary>Reach of the cursor's push in pixels at 100 %.</summary>
    public const float CursorReach = 36f;

    private static readonly Vector3[] s_greens =
    [
        new(0.30f, 0.62f, 0.20f),
        new(0.38f, 0.70f, 0.24f),
        new(0.24f, 0.52f, 0.18f),
        new(0.50f, 0.72f, 0.28f),
    ];

    private readonly Dictionary<SurfaceKey, Strip> _strips = [];
    private readonly List<SurfaceKey> _stale = [];
    private readonly List<Strip> _fading = [];
    private readonly ulong _seed;
    private float _scale = 1f;
    private float _time;
    private float _wither = 1f;

    public GrassEffect(ulong seed = 1)
    {
        _seed = seed;
    }

    public string Name => "grass";

    public EffectSettings Settings { get; set; } = new();

    /// <summary>Rain intensity of the rain effect, 0 to 1. Rain makes the grass grow faster (PLAN.md, section 4.7).</summary>
    public float RainIntensity { get; set; }

    public bool IsActive => Settings.ClampedIntensity > 0f || _strips.Count > 0 || _fading.Count > 0;

    /// <summary>Number of blades on the top edge of a window, or 0.</summary>
    public int BladesOn(long windowId) => _strips.TryGetValue(new SurfaceKey(windowId, SurfaceEdge.Top), out Strip? strip) ? strip.Count : 0;

    /// <summary>Height of the tallest blade on the top edge of a window, in pixels.</summary>
    public float TallestOn(long windowId) => _strips.TryGetValue(new SurfaceKey(windowId, SurfaceEdge.Top), out Strip? strip) ? strip.Tallest : 0f;

    /// <summary>Number of blades on the floor of a monitor (index into the monitor list).</summary>
    public int BladesOnFloor(int monitorIndex) => _strips.TryGetValue(new SurfaceKey(monitorIndex, SurfaceEdge.Floor), out Strip? strip) ? strip.Count : 0;

    /// <summary>Strips fading out after their window closed.</summary>
    public int FadingCount => _fading.Count;

    /// <summary>Lets every grown strip reach <paramref name="share"/> of its final height at once. For previews and the sandbox.</summary>
    public void Grow(SimulationContext context, float share)
    {
        ArgumentNullException.ThrowIfNull(context);
        _scale = context.PixelScale;
        EnsureStrips(context);
        foreach (Strip strip in _strips.Values)
        {
            strip.GrowTo(Math.Clamp(share, 0f, 1f));
        }
    }

    public void Simulate(float dt, SimulationContext context)
    {
        float intensity = Settings.ClampedIntensity;
        _scale = context.PixelScale;
        _time += dt;

        if (intensity > 0f)
        {
            _wither = 1f;
            EnsureStrips(context);
        }
        else
        {
            _wither = MathF.Max(0f, _wither - (dt / WitherSeconds));
        }

        // Rain makes it grow three times as fast (PLAN.md: rate x (1 + 2 x rain)).
        float growth = GrowthRate * _scale * intensity * (1f + (2f * Math.Clamp(RainIntensity, 0f, 1f))) * dt;
        _stale.Clear();
        foreach (KeyValuePair<SurfaceKey, Strip> entry in _strips)
        {
            Strip strip = entry.Value;
            if (!TryBounds(entry.Key, context, out PixelRect bounds))
            {
                _stale.Add(entry.Key);
                continue;
            }

            strip.Origin = new Vector2(bounds.Left, bounds.Top);
            strip.Grow(growth);
            if (_wither <= 0f)
            {
                _stale.Add(entry.Key);
            }
        }

        foreach (SurfaceKey key in _stale)
        {
            _strips.Remove(key);
        }

        for (int i = _fading.Count - 1; i >= 0; i--)
        {
            Strip strip = _fading[i];
            strip.Fade -= dt / FadeSeconds;
            if (strip.Fade <= 0f)
            {
                _fading.RemoveAt(i);
            }
        }
    }

    public void OnWindowEvent(in WindowEvent windowEvent, SimulationContext context)
    {
        _scale = context.PixelScale;
        var key = new SurfaceKey(windowEvent.WindowId, SurfaceEdge.Top);
        switch (windowEvent.Kind)
        {
            case WindowEventKind.Disappeared:
                // Not thrown off: it fades where the window was.
                if (_strips.Remove(key, out Strip? strip))
                {
                    strip.Origin = new Vector2(windowEvent.Bounds.Left, windowEvent.Bounds.Top);
                    strip.Fade = 1f;
                    _fading.Add(strip);
                }

                break;

            case WindowEventKind.Resized:
                if (_strips.TryGetValue(key, out Strip? resized))
                {
                    resized.Resize(windowEvent.Bounds.Width, _scale);
                }

                break;
        }
    }

    public void Emit(RenderFrame frame, SimulationContext context)
    {
        _scale = context.PixelScale;
        float windLean = Math.Clamp(context.Wind.Current.X * 0.0025f, -0.5f, 0.5f);
        ReadOnlySpan<TrackedWindow> windows = context.Windows.Windows;

        // Only where the surface can be seen: a window above hides the grass behind it.
        foreach (SurfaceSegment segment in context.Surfaces.Segments)
        {
            if (segment.IsHorizontal && _strips.TryGetValue(segment.Key, out Strip? strip))
            {
                EmitStrip(frame, context, strip, segment.Start, segment.End, segment.Position, windLean, _wither);
            }
        }

        foreach (Strip strip in _fading)
        {
            int left = (int)strip.Origin.X;
            if (!IsCovered(windows, strip.Origin))
            {
                EmitStrip(frame, context, strip, left, left + strip.Width, (int)strip.Origin.Y, windLean, strip.Fade);
            }
        }
    }

    private static bool IsCovered(ReadOnlySpan<TrackedWindow> windows, Vector2 point)
    {
        foreach (TrackedWindow window in windows)
        {
            if (window.Bounds.Contains((int)point.X, (int)point.Y - 1))
            {
                return true;
            }
        }

        return false;
    }

    private static bool TryBounds(SurfaceKey key, SimulationContext context, out PixelRect bounds)
    {
        if (key.Edge == SurfaceEdge.Top)
        {
            int index = context.Windows.IndexOf(key.OwnerId);
            bounds = index >= 0 ? context.Windows[index].Bounds : default;
            return index >= 0;
        }

        if (key.OwnerId >= 0 && key.OwnerId < context.Monitors.Count)
        {
            PixelRect workArea = context.Monitors[(int)key.OwnerId].WorkArea;
            bounds = workArea with { Top = workArea.Bottom };
            return true;
        }

        bounds = default;
        return false;
    }

    private void EnsureStrips(SimulationContext context)
    {
        foreach (SurfaceSegment segment in context.Surfaces.Segments)
        {
            if (!segment.IsHorizontal || _strips.ContainsKey(segment.Key) || !TryBounds(segment.Key, context, out PixelRect bounds))
            {
                continue;
            }

            int width = segment.Key.Edge == SurfaceEdge.Top ? bounds.Width : context.Monitors[(int)segment.Key.OwnerId].WorkArea.Width;
            ulong seed = _seed ^ (ulong)segment.Key.OwnerId ^ ((ulong)segment.Key.Edge << 40);
            _strips.Add(segment.Key, new Strip(width, _scale, seed) { Origin = new Vector2(bounds.Left, bounds.Top) });
        }
    }

    private void EmitStrip(RenderFrame frame, SimulationContext context, Strip strip, int left, int right, int baseRow, float windLean, float alpha)
    {
        if (alpha <= 0.01f)
        {
            return;
        }

        Vector2 cursor = context.Cursor;
        float reach = CursorReach * _scale;
        for (int i = 0; i < strip.Count; i++)
        {
            float x = strip.Origin.X + strip.LocalX[i];
            float height = strip.Height[i] * (0.4f + (0.6f * _wither));
            if (x < left || x >= right || height < 0.6f)
            {
                continue;
            }

            // Swaying: wind lean plus a slow wave along the strip, each blade with its own phase.
            float sway = 0.12f * MathF.Sin((_time * 1.6f) + strip.Phase[i] + (x * 0.012f));
            float bend = windLean + sway;

            // The cursor pushes nearby blades away from itself.
            float dx = x - cursor.X;
            float dy = baseRow - cursor.Y;
            if (MathF.Abs(dx) < reach && dy > -2f * _scale && dy < height + reach)
            {
                bend += MathF.CopySign(0.55f * (1f - (MathF.Abs(dx) / reach)), dx);
            }

            bend = Math.Clamp(bend, -0.7f, 0.7f);
            float halfLength = height / 2f;

            // The quad stands upright on the root (rotated a quarter turn: its +y axis points right on screen),
            // so a positive bend curves the tip to the right; the shader draws the curve inside the quad.
            var center = new Vector2(x, baseRow - halfLength);
            Vector3 color = Settings.Color ?? s_greens[strip.Color[i]];
            frame.Add(new Sprite(
                center,
                new Vector2(halfLength, Sprite.BladeHalfHeight(halfLength, bend)),
                -MathF.PI / 2f,
                SpriteShape.Blade,
                Sprite.Premultiply(Vector3.Min(color * Settings.ClampedBrightness, Vector3.One), MathF.Min(1f, alpha)))
            { Variant = bend });
        }
    }

    /// <summary>The blades of one surface, positioned relative to its left end.</summary>
    private sealed class Strip
    {
        private readonly ulong _seed;

        public Strip(int width, float scale, ulong seed)
        {
            _seed = seed;
            Scale = scale;
            Width = Math.Max(width, 1);
            int capacity = BladeCount(Width, scale);
            LocalX = new float[capacity];
            Height = new float[capacity];
            MaxHeight = new float[capacity];
            Phase = new float[capacity];
            Color = new byte[capacity];
            Fill(0);
        }

        public float Scale { get; }

        public int Width { get; private set; }

        public int Count { get; private set; }

        public Vector2 Origin { get; set; }

        /// <summary>1 while the strip lives on its window, falling to 0 while it fades out after a close.</summary>
        public float Fade { get; set; } = 1f;

        public float[] LocalX { get; private set; }

        public float[] Height { get; private set; }

        public float[] MaxHeight { get; private set; }

        public float[] Phase { get; private set; }

        public byte[] Color { get; private set; }

        public float Tallest
        {
            get
            {
                float tallest = 0f;
                for (int i = 0; i < Count; i++)
                {
                    tallest = MathF.Max(tallest, Height[i]);
                }

                return tallest;
            }
        }

        public void Grow(float amount)
        {
            for (int i = 0; i < Count; i++)
            {
                Height[i] = MathF.Min(MaxHeight[i], Height[i] + amount);
            }
        }

        public void GrowTo(float share)
        {
            for (int i = 0; i < Count; i++)
            {
                Height[i] = MathF.Max(Height[i], MaxHeight[i] * share);
            }
        }

        /// <summary>Narrower: the blades beyond the new end go. Wider: new blades sprout in the new part.</summary>
        public void Resize(int width, float scale)
        {
            width = Math.Max(width, 1);
            if (width < Width)
            {
                int kept = 0;
                for (int i = 0; i < Count; i++)
                {
                    if (LocalX[i] < width)
                    {
                        Move(i, kept++);
                    }
                }

                Count = kept;
                Width = width;
                return;
            }

            int capacity = BladeCount(width, scale);
            if (capacity > LocalX.Length)
            {
                LocalX = Grown(LocalX, capacity);
                Height = Grown(Height, capacity);
                MaxHeight = Grown(MaxHeight, capacity);
                Phase = Grown(Phase, capacity);
                Color = Grown(Color, capacity);
            }

            int from = Width;
            Width = width;
            Fill(from);
        }

        /// <summary>About one blade per 4 px at 100 %, in irregular clumps.</summary>
        private static int BladeCount(int width, float scale) => Math.Max(1, (int)(width / (4f * scale)) + 2);

        private static T[] Grown<T>(T[] array, int capacity)
        {
            var grown = new T[capacity];
            array.CopyTo(grown, 0);
            return grown;
        }

        /// <summary>Plants blades from <paramref name="from"/> to the end. Each position gets the same blade every time (seeded by position).</summary>
        private void Fill(int from)
        {
            float spacing = 4f * Scale;
            int first = (int)MathF.Ceiling(from / spacing);
            for (int n = first; Count < LocalX.Length; n++)
            {
                var random = new Pcg32(_seed ^ (ulong)(n * 2654435761L));
                float x = (n * spacing) + random.NextFloat(-1.5f, 1.5f) * Scale;
                if (x >= Width)
                {
                    break;
                }

                int i = Count++;
                LocalX[i] = Math.Clamp(x, 0f, Width - 1);
                Height[i] = 0f;

                // Clumps: a slow wave over the position makes tall and short stretches.
                float clump = 0.55f + (0.45f * MathF.Abs(MathF.Sin((x * 0.021f) + (_seed % 7))));
                MaxHeight[i] = MaxBladeHeight * Scale * clump * random.NextFloat(0.6f, 1f);
                Phase[i] = random.NextFloat(0f, MathF.Tau);
                Color[i] = (byte)random.NextInt(s_greens.Length);
            }
        }

        private void Move(int from, int to)
        {
            LocalX[to] = LocalX[from];
            Height[to] = Height[from];
            MaxHeight[to] = MaxHeight[from];
            Phase[to] = Phase[from];
            Color[to] = Color[from];
        }
    }
}
