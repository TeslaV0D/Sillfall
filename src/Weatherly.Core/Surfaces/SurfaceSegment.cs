namespace Weatherly.Core.Surfaces;

/// <summary>Which edge a surface belongs to.</summary>
public enum SurfaceEdge
{
    /// <summary>Top edge of a window. Snow, leaves, puddles and grass rest on it.</summary>
    Top,

    /// <summary>Column just left of a window. Rain runs down here.</summary>
    Left,

    /// <summary>Column just right of a window. Rain runs down here.</summary>
    Right,

    /// <summary>Bottom of a monitor's work area, usually the top of the taskbar. Everything that falls ends here.</summary>
    Floor,
}

/// <summary>
/// Identity of a surface. Accumulated snow, water, leaves and grass belong to this key, so they
/// survive while the visible part of the surface changes (PLAN.md, section 3.3).
/// </summary>
/// <param name="OwnerId">Window id for window edges. For <see cref="SurfaceEdge.Floor"/> the monitor index.</param>
/// <param name="Edge">Which edge.</param>
public readonly record struct SurfaceKey(long OwnerId, SurfaceEdge Edge)
{
    public override string ToString() => $"{Edge}#{OwnerId}";
}

/// <summary>
/// One visible piece of a surface, in physical pixels of the virtual desktop.
/// <para>
/// Horizontal surfaces (<see cref="SurfaceEdge.Top"/>, <see cref="SurfaceEdge.Floor"/>): the surface is the line
/// directly above row <see cref="Position"/>; things rest on it at y &lt; Position, covering columns
/// [<see cref="Start"/>, <see cref="End"/>).
/// </para>
/// <para>
/// Vertical surfaces (<see cref="SurfaceEdge.Left"/>, <see cref="SurfaceEdge.Right"/>): the surface is column
/// <see cref="Position"/> (just outside the window), covering rows [<see cref="Start"/>, <see cref="End"/>).
/// </para>
/// </summary>
/// <param name="Key">The surface this piece belongs to.</param>
/// <param name="Position">Row (horizontal) or column (vertical) of the surface.</param>
/// <param name="Start">First covered column or row.</param>
/// <param name="End">Column or row after the last covered one.</param>
/// <param name="Origin">
/// Where the whole surface starts (window left for top edges, window top for side edges, work area left
/// for floors). Accumulation data is stored relative to it, so it moves with the window.
/// </param>
/// <param name="MonitorIndex">Index of the monitor (in the list given to the extractor) that shows this piece.</param>
public readonly record struct SurfaceSegment(SurfaceKey Key, int Position, int Start, int End, int Origin, int MonitorIndex)
{
    public int Length => End - Start;

    public bool IsHorizontal => Key.Edge is SurfaceEdge.Top or SurfaceEdge.Floor;
}
