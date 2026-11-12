using Sillfall.Core.Visitors;

namespace Sillfall.Core.Rendering;

/// <summary>What a visitor holds or wears for the weather (PLAN.md, section 6: umbrella, snowball, sunglasses).</summary>
public enum VisitorProp : byte
{
    None,
    Umbrella,
    Snowball,
    Sunglasses,
    Lantern,
    Leaf,
}

/// <summary>A visitor's face.</summary>
public enum VisitorExpression : byte
{
    Calm,
    Happy,
    Sulky,
}

/// <summary>
/// One visitor as the renderer draws it: which drawing, where, and how its parts move. Data only, so the D2D renderer
/// (Render) and the hit windows (Platform) need no simulation code.
/// </summary>
public struct VisitorPose
{
    /// <summary>Identity for the hit windows; new for every visit.</summary>
    public int Id { get; set; }

    public VisitorKind Kind { get; set; }

    /// <summary>Middle of the feet, in desktop pixels.</summary>
    public float X { get; set; }

    public float Y { get; set; }

    /// <summary>Height of the drawing in pixels.</summary>
    public float Height { get; set; }

    /// <summary>+1 looks right, -1 looks left (the drawing is mirrored).</summary>
    public float Facing { get; set; }

    /// <summary>Rotation around the feet in radians (jumps, falls).</summary>
    public float Tilt { get; set; }

    /// <summary>Vertical stretch: below 1 squashed (landing), above 1 stretched (jumping).</summary>
    public float Stretch { get; set; }

    /// <summary>Lift of the body over the feet in pixels (walking bounce, hovering).</summary>
    public float Bob { get; set; }

    /// <summary>Walking: the feet take turns (<see cref="Step"/>).</summary>
    public bool Walking { get; set; }

    /// <summary>Walk cycle, 0 to 1: the feet swap at 0.5.</summary>
    public float Step { get; set; }

    /// <summary>Eyes: 0 open, 1 closed.</summary>
    public float Blink { get; set; }

    public VisitorProp Prop { get; set; }

    /// <summary>How far the prop is out, 0 to 1 (an umbrella opens).</summary>
    public float PropAmount { get; set; }

    public VisitorExpression Expression { get; set; }

    /// <summary>Opacity, 0 to 1 (arriving, leaving).</summary>
    public float Alpha { get; set; }

    /// <summary>Overall scale for popping in and out, 0 to 1.</summary>
    public float Scale { get; set; }

    /// <summary>Held by the user (interactive mode).</summary>
    public bool Held { get; set; }
}

/// <summary>The box visitors are dropped into (interactive mode, PLAN.md, section 6).</summary>
/// <param name="Bounds">Where it stands, in desktop pixels.</param>
/// <param name="Visibility">0 hidden to 1 fully shown (it fades in while a visitor is held).</param>
/// <param name="Hot">A held visitor is over it: letting go collects.</param>
public readonly record struct VisitorBox(Geometry.PixelRect Bounds, float Visibility, bool Hot)
{
    public bool IsVisible => Visibility > 0.01f && !Bounds.IsEmpty;
}
