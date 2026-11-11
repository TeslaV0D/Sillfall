using Weatherly.Core.Surfaces;

namespace Weatherly.Core.Rendering;

/// <summary>
/// Everything the renderer needs for one frame, built by the simulation: sprites in draw order and
/// the surfaces they rest on. Reused from frame to frame: effects fill it, the renderer only reads it.
/// </summary>
public sealed class RenderFrame
{
    private Sprite[] _sprites;
    private int _count;

    public RenderFrame(int initialCapacity = 4096)
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

    public void Clear()
    {
        _count = 0;
        Surfaces = null;
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
