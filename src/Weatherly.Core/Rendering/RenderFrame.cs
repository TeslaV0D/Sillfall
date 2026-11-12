using Weatherly.Core.Geometry;
using Weatherly.Core.Surfaces;

namespace Weatherly.Core.Rendering;

/// <summary>
/// Everything the renderer needs for one frame, built by the simulation: sprites in draw order and
/// the surfaces they rest on. Reused from frame to frame: effects fill it, the renderer only reads it.
/// </summary>
public sealed class RenderFrame
{
    /// <summary>Most windows the atmosphere passes take into account: the topmost ones.</summary>
    public const int MaxOccluders = 64;

    /// <summary>Most visitors drawn at once.</summary>
    public const int MaxVisitors = 4;

    private readonly PixelRect[] _occluders = new PixelRect[MaxOccluders];
    private readonly VisitorPose[] _visitors = new VisitorPose[MaxVisitors];
    private int _visitorCount;
    private readonly PixelRect[] _cutouts = new PixelRect[MaxOccluders];
    private int _cutoutCount;
    private Sprite[] _sprites;
    private int _count;
    private int _occluderCount;

    /// <summary>
    /// The default capacity holds the full particle budget of PLAN.md, section 4 (12k rain drops plus splashes,
    /// drips with trails and puddles) without growing during play.
    /// </summary>
    public const int DefaultCapacity = 32_768;

    public RenderFrame(int initialCapacity = DefaultCapacity)
    {
        _sprites = new Sprite[Math.Max(initialCapacity, 16)];
    }

    /// <summary>Sprites in draw order: earlier ones lie below later ones.</summary>
    public ReadOnlySpan<Sprite> Sprites => _sprites.AsSpan(0, _count);

    public int SpriteCount => _count;

    /// <summary>Surfaces of this frame, for the debug layer. May be <see langword="null"/>.</summary>
    public SurfaceSet? Surfaces { get; set; }

    /// <summary>Simulated time of this frame in seconds.</summary>
    public double Time { get; set; }

    /// <summary>Fog behind all sprites. Default: none.</summary>
    public FogLayer Fog { get; set; }

    /// <summary>Sunlight behind the fog and all sprites. Default: none.</summary>
    public SunLayer Sun { get; set; }

    /// <summary>Whether a full-screen atmosphere pass (fog or sun) has to run.</summary>
    public bool HasAtmosphere => Fog.IsVisible || Sun.IsVisible;

    /// <summary>
    /// Window rectangles in desktop pixels, topmost first: they block the sunlight and thin the fog.
    /// Filled by the engine while an atmosphere is visible.
    /// </summary>
    public ReadOnlySpan<PixelRect> Occluders => _occluders.AsSpan(0, _occluderCount);

    /// <summary>Visitors, drawn on top of all weather (PLAN.md, section 4.7: render order).</summary>
    public ReadOnlySpan<VisitorPose> Visitors => _visitors.AsSpan(0, _visitorCount);

    /// <summary>The box for collecting visitors. Default: none.</summary>
    public VisitorBox Box { get; set; }

    /// <summary>Windows the user's app rules keep clear of weather: nothing is drawn over them.</summary>
    public ReadOnlySpan<PixelRect> Cutouts => _cutouts.AsSpan(0, _cutoutCount);

    public void Clear()
    {
        _count = 0;
        _occluderCount = 0;
        _cutoutCount = 0;
        _visitorCount = 0;
        Box = default;
        Surfaces = null;
        Fog = default;
        Sun = default;
    }

    /// <summary>Adds a visitor. Beyond <see cref="MaxVisitors"/> they are ignored.</summary>
    public void AddVisitor(in VisitorPose pose)
    {
        if (_visitorCount < MaxVisitors)
        {
            _visitors[_visitorCount++] = pose;
        }
    }

    /// <summary>Adds a window to keep clear of weather. Beyond <see cref="MaxOccluders"/> they are ignored.</summary>
    public void AddCutout(PixelRect rect)
    {
        if (_cutoutCount < MaxOccluders && !rect.IsEmpty)
        {
            _cutouts[_cutoutCount++] = rect;
        }
    }

    /// <summary>Adds a window rectangle. Beyond <see cref="MaxOccluders"/> (the lowest windows) they are ignored.</summary>
    public void AddOccluder(PixelRect rect)
    {
        if (_occluderCount < MaxOccluders && !rect.IsEmpty)
        {
            _occluders[_occluderCount++] = rect;
        }
    }

    /// <summary>Appends a sprite on top of all sprites added so far.</summary>
    public void Add(in Sprite sprite)
    {
        if (_count == _sprites.Length)
        {
            // Grows rarely (capacity doubles); in steady state the frame never allocates.
            Array.Resize(ref _sprites, _sprites.Length * 2);
        }

        _sprites[_count++] = sprite;
    }
}
