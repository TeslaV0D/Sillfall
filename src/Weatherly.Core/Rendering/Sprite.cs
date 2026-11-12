using System.Numerics;
using System.Runtime.InteropServices;

namespace Weatherly.Core.Rendering;

/// <summary>How the GPU shades a sprite inside its quad.</summary>
public enum SpriteShape : uint
{
    /// <summary>Solid rectangle.</summary>
    Rectangle = 0,

    /// <summary>Round blob with a soft edge: snow flakes, splashes, dust.</summary>
    Disc = 1,

    /// <summary>Capsule with soft ends, long along its x axis: rain drops in motion.</summary>
    Streak = 2,

    /// <summary>Thin elliptic ring: the ripple where a drop hits a surface.</summary>
    Ring = 3,

    /// <summary>
    /// Snow flake, drawn procedurally in one of <see cref="Sprite.FlakeVariants"/> forms chosen by
    /// <see cref="Sprite.Variant"/>: soft blob, star, branched star, plate, broad star, cluster.
    /// </summary>
    Flake = 4,

    /// <summary>
    /// Leaf, long along its x axis with the stem at -x, in one of <see cref="Sprite.LeafVariants"/> forms chosen
    /// by <see cref="Sprite.Variant"/>: pointed oval, maple, oak, narrow. A darker midrib runs along the axis.
    /// </summary>
    Leaf = 5,

    /// <summary>
    /// Grass blade: root at -x, tip at +x, tapering towards the tip and curving sideways by
    /// <see cref="Sprite.Variant"/> (the tip's offset as a share of the blade length, negative to the left of its axis).
    /// The quad's half height must cover the curve: <see cref="Sprite.BladeHalfHeight"/>.
    /// </summary>
    Blade = 6,
}

/// <summary>
/// One instance for the GPU, in physical pixels of the virtual desktop. Layout matches the
/// structured buffer in Sprite.vs.hlsl: 48 bytes.
/// </summary>
/// <param name="Center">Center of the quad.</param>
/// <param name="HalfSize">Half width (along the rotated x axis) and half height.</param>
/// <param name="Rotation">Rotation in radians, clockwise on screen.</param>
/// <param name="Shape">How the quad is shaded.</param>
/// <param name="Color">Premultiplied color.</param>
[StructLayout(LayoutKind.Sequential)]
public readonly record struct Sprite(Vector2 Center, Vector2 HalfSize, float Rotation, SpriteShape Shape, Vector4 Color)
{
    /// <summary>Size of one instance in bytes, as the shader expects it.</summary>
    public const int SizeInBytes = 48;

    /// <summary>Number of forms <see cref="SpriteShape.Flake"/> knows.</summary>
    public const int FlakeVariants = 6;

    /// <summary>Number of forms <see cref="SpriteShape.Leaf"/> knows.</summary>
    public const int LeafVariants = 4;

    /// <summary>Which form a shape with variants takes (<see cref="SpriteShape.Flake"/>, <see cref="SpriteShape.Leaf"/>). Ignored by the others.</summary>
    public float Variant { get; init; }

    // Pads the struct to 48 bytes, so instances are 16-byte aligned in the GPU buffer.
    private readonly float _padding;

    /// <summary>Conservative bounding box: the circle around the rotated quad.</summary>
    public float BoundingRadius => HalfSize.Length();

    /// <summary>Half height of the quad of a <see cref="SpriteShape.Blade"/> that holds its curve and width.</summary>
    public static float BladeHalfHeight(float halfLength, float bend) => (halfLength * ((2f * MathF.Abs(bend)) + 0.12f)) + 0.5f;

    /// <summary>A premultiplied color from straight RGB in [0, 1] and an alpha.</summary>
    public static Vector4 Premultiply(Vector3 color, float alpha) => new(color * alpha, alpha);
}
