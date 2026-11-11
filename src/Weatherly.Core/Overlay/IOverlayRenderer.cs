using Weatherly.Core.Geometry;

namespace Weatherly.Core.Overlay;

/// <summary>Everything a surface needs to draw one frame.</summary>
/// <param name="Content">What to draw.</param>
/// <param name="ScaleFactor">DPI scale of the monitor, 1 at 100 %.</param>
public readonly record struct OverlayFrame(OverlayContent Content, float ScaleFactor);

/// <summary>Outcome of presenting a frame.</summary>
public enum PresentResult
{
    /// <summary>The frame is on screen.</summary>
    Presented,

    /// <summary>
    /// The graphics device was removed or reset (driver update, GPU switch, remote session).
    /// All surfaces are unusable until <see cref="IOverlayRenderer.RecreateDevice"/> ran.
    /// </summary>
    DeviceLost,
}

/// <summary>
/// The transparent drawing surface of one overlay window. Not thread-safe:
/// all calls come from the thread that owns the overlay windows.
/// </summary>
public interface IOverlaySurface : IDisposable
{
    PixelSize Size { get; }

    /// <summary>Draws <paramref name="frame"/> and presents it.</summary>
    PresentResult Present(in OverlayFrame frame);
}

/// <summary>
/// Creates drawing surfaces for overlay windows. The window layer (Weatherly.Platform) drives the
/// renderer (Weatherly.Render) through this contract, so neither layer has to know the other.
/// Not thread-safe: all calls come from the thread that owns the overlay windows.
/// </summary>
public interface IOverlayRenderer : IDisposable
{
    /// <summary>Name of the graphics adapter in use, for diagnostics.</summary>
    string AdapterName { get; }

    /// <summary>Creates the surface for the window <paramref name="windowHandle"/> with the given size in physical pixels.</summary>
    IOverlaySurface CreateSurface(nint windowHandle, PixelSize size);

    /// <summary>
    /// Rebuilds the graphics device after <see cref="PresentResult.DeviceLost"/>.
    /// The caller must dispose every surface first and create new ones afterwards.
    /// </summary>
    void RecreateDevice();
}
