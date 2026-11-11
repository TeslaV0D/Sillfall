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

    // Pads the struct to 48 bytes, so instances are 16-byte aligned in the GPU buffer.
    private readonly Vector2 _padding;

    /// <summary>Conservative bounding box: the circle around the rotated quad.</summary>
    public float BoundingRadius => HalfSize.Length();

    /// <summary>A premultiplied color from straight RGB in [0, 1] and an alpha.</summary>
    public static Vector4 Premultiply(Vector3 color, float alpha) => new(color * alpha, alpha);
}
