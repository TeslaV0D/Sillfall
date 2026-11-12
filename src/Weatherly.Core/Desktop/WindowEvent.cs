using System.Numerics;
using Weatherly.Core.Geometry;

namespace Weatherly.Core.Desktop;

/// <summary>What happened to a window between two snapshots.</summary>
public enum WindowEventKind
{
    /// <summary>The window appeared: opened, restored or switched to.</summary>
    Appeared,

    /// <summary>The window disappeared: closed, hidden, minimized or moved to another virtual desktop. Accumulations slide off.</summary>
    Disappeared,

    /// <summary>The window moved. <see cref="WindowEvent.Velocity"/> is set.</summary>
    Moved,

    /// <summary>The window changed its size. Accumulations are resampled to the new width.</summary>
    Resized,

    /// <summary>The window was shaken: several quick direction changes. Leaves and snow come off.</summary>
    Shaken,
}

/// <summary>A change of one window, derived from consecutive snapshots.</summary>
/// <param name="Kind">What happened.</param>
/// <param name="WindowId">Which window.</param>
/// <param name="Bounds">Bounds after the change (before it for <see cref="WindowEventKind.Disappeared"/>).</param>
/// <param name="PreviousBounds">Bounds before the change. Empty for <see cref="WindowEventKind.Appeared"/>.</param>
/// <param name="Velocity">Velocity of the window in pixels per second, for moves.</param>
public readonly record struct WindowEvent(
    WindowEventKind Kind,
    long WindowId,
    PixelRect Bounds,
    PixelRect PreviousBounds,
    Vector2 Velocity);
