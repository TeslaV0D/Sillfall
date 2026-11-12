using System.Numerics;

namespace Weatherly.Core.Rendering;

/// <summary>
/// Fog for the renderer (PLAN.md, section 4.4): drawn as a full-screen pass at a quarter of the resolution.
/// <see cref="Density"/> 0 means no fog.
/// </summary>
/// <param name="Density">Overall thickness, 0 to 1.</param>
/// <param name="Scroll">How far the fog drifted with the wind, in desktop pixels.</param>
/// <param name="Phase">Slowly running time that lets the fog's shape change.</param>
/// <param name="Color">Straight (not premultiplied) fog colour.</param>
/// <param name="MaxAlpha">Most coverage anywhere, so the screen stays readable.</param>
/// <param name="Scale">Physical pixels per device-independent pixel: the size of the fog's features.</param>
public readonly record struct FogLayer(float Density, Vector2 Scroll, float Phase, Vector3 Color, float MaxAlpha, float Scale)
{
    public bool IsVisible => Density > 0.001f && MaxAlpha > 0f;
}

/// <summary>
/// Sunlight for the renderer (PLAN.md, section 4.5): god rays from <see cref="Position"/>, blocked by the
/// windows (<see cref="RenderFrame.Occluders"/>), and a soft glow around the sun. <see cref="Strength"/> 0 means none.
/// </summary>
/// <param name="Strength">Brightness of the light, 0 to 1.</param>
/// <param name="Position">The sun in desktop pixels, usually just above the top of the screen.</param>
/// <param name="Color">Straight colour of the light.</param>
/// <param name="Scale">Physical pixels per device-independent pixel: the size of the glow.</param>
public readonly record struct SunLayer(float Strength, Vector2 Position, Vector3 Color, float Scale)
{
    public bool IsVisible => Strength > 0.001f;
}
