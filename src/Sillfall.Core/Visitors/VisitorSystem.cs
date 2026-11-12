using System.Numerics;
using Sillfall.Core.Desktop;
using Sillfall.Core.Effects;
using Sillfall.Core.Geometry;
using Sillfall.Core.Rendering;
using Sillfall.Core.Simulation;
using Sillfall.Core.Surfaces;

namespace Sillfall.Core.Visitors;

/// <summary>What a visitor is doing.</summary>
public enum VisitorState
{
    /// <summary>Pops in where it was placed.</summary>
    Entering,

    /// <summary>Walks along the top edge of a window or the taskbar.</summary>
    Walking,

    /// <summary>Stands, looks around, hops when it likes the weather.</summary>
    Idle,

    /// <summary>In the air: jumping to another window, or falling because its window went away.</summary>
    Airborne,

    /// <summary>Held by the user (interactive mode).</summary>
    Held,

    /// <summary>Was dropped somewhere else than the box: hurries off offended.</summary>
    Sulking,

    /// <summary>Fades out and is gone.</summary>
    Leaving,
}

/// <summary>A visitor as the host sees it (hit windows, keyboard alternative, diagnostics).</summary>
public readonly record struct VisitorInfo(int Id, VisitorKind Kind, VisitorState State, Vector2 Feet, PixelRect Bounds, VisitorProp Prop, SurfaceKey? Surface);

/// <summary>
/// The visitors (PLAN.md, section 6): a state machine per character. A visitor appears on the top edge of a window
/// (or the taskbar), walks along its visible part, jumps to a nearby window or turns around at the end, reacts to the
/// weather with a prop or a happy hop, falls when its window closes or is shaken, and leaves after a while. It stands
/// relative to its window, so it rides along when the window moves.
/// <para>No allocations in <see cref="Simulate"/> and <see cref="Emit"/>: a fixed array of at most <see cref="Capacity"/> visitors.</para>
/// </summary>
public sealed class VisitorSystem : IEffectSystem
{
    public const int Capacity = 3;

    /// <summary>Gravity in device-independent pixels per second squared.</summary>
    public const float Gravity = 1400f;

    /// <summary>How far a visitor jumps sideways (device-independent pixels).</summary>
    public const float JumpReach = 150f;

    /// <summary>How high a visitor can climb with a jump (device-independent pixels).</summary>
    public const float JumpClimb = 90f;

    public const float EnterSeconds = 0.6f;
    public const float LeaveSeconds = 0.8f;

    /// <summary>How long a visit lasts at most, before the visitor leaves by itself.</summary>
    public const float MaxVisitSeconds = 90f;

    private const float MinVisitSeconds = 40f;
    private const float SulkSeconds = 2.5f;
    private const float PropSpeed = 2f;

    private readonly Visitor[] _visitors = new Visitor[Capacity];
    private Pcg32 _random;
    private int _count;
    private int _nextId = 1;
    private float _scale = 1f;
    private double _time;

    public VisitorSystem(ulong seed = 1)
    {
        _random = new Pcg32(seed);
    }

    public string Name => "visitors";

    public bool IsActive => _count > 0;

    public int Count => _count;

    /// <summary>The weather on screen; the host updates it when the options change.</summary>
    public WeatherMood Mood { get; set; }

    /// <summary>A visitor (index below <see cref="Count"/>).</summary>
    public VisitorInfo this[int index]
    {
        get
        {
            ArgumentOutOfRangeException.ThrowIfGreaterThanOrEqual((uint)index, (uint)_count, nameof(index));
            ref Visitor v = ref _visitors[index];
            return new VisitorInfo(v.Id, v.Kind, v.State, new Vector2(v.X, v.Y), Bounds(in v), v.Prop, v.OnSurface ? v.Surface : null);
        }
    }

    /// <summary>
    /// Lets <paramref name="kind"/> appear on a random visible top edge (windows preferred, else the taskbar). Returns
    /// <see langword="false"/> when there is no room or no place wide enough.
    /// </summary>
    public bool Spawn(VisitorKind kind, SimulationContext context)
    {
        ArgumentNullException.ThrowIfNull(context);
        _scale = context.PixelScale;
        if (_count >= Capacity)
        {
            return false;
        }

        float height = HeightOf(kind);
        ReadOnlySpan<SurfaceSegment> segments = context.Surfaces.Segments;
        bool preferWindows = _random.NextFloat() < 0.8f;
        int chosen = PickSpawnSegment(segments, height * 3f, preferWindows);
        if (chosen < 0)
        {
            chosen = PickSpawnSegment(segments, height * 3f, !preferWindows);
        }

        if (chosen < 0)
        {
            return false;
        }

        SurfaceSegment segment = segments[chosen];
        float margin = height * 0.6f;
        Place(kind, segment, _random.NextFloat(segment.Start + margin, segment.End - margin));
        return true;
    }

    /// <summary>Lets <paramref name="kind"/> appear at column <paramref name="x"/> of a surface (tests, smoke test).</summary>
    public bool SpawnAt(VisitorKind kind, SimulationContext context, SurfaceKey surface, float x)
    {
        ArgumentNullException.ThrowIfNull(context);
        _scale = context.PixelScale;
        if (_count >= Capacity)
        {
            return false;
        }

        foreach (SurfaceSegment segment in context.Surfaces.Segments)
        {
            if (segment.Key == surface && x >= segment.Start && x < segment.End)
            {
                Place(kind, segment, x);
                return true;
            }
        }

        return false;
    }

    private void Place(VisitorKind kind, in SurfaceSegment segment, float x)
    {
        _visitors[_count++] = new Visitor
        {
            Id = _nextId++,
            Kind = kind,
            State = VisitorState.Entering,
            StateDuration = EnterSeconds,
            VisitLeft = _random.NextFloat(MinVisitSeconds, MaxVisitSeconds),
            X = x,
            Y = segment.Position,
            Facing = _random.NextFloat() < 0.5f ? -1f : 1f,
            Surface = segment.Key,
            OnSurface = true,
            LocalX = x - segment.Origin,
            BlinkTimer = _random.NextFloat(1f, 4f),
            Scale = 0f,
        };
    }

    public void Simulate(float dt, SimulationContext context)
    {
        ArgumentNullException.ThrowIfNull(context);
        _scale = context.PixelScale;
        _time = context.Time;
        int i = 0;
        while (i < _count)
        {
            ref Visitor v = ref _visitors[i];
            if (!Step(ref v, dt, context))
            {
                RemoveAt(i);
                continue;
            }

            i++;
        }
    }

    public void OnWindowEvent(in WindowEvent windowEvent, SimulationContext context)
    {
        ArgumentNullException.ThrowIfNull(context);
        _scale = context.PixelScale;
        if (windowEvent.Kind is not (WindowEventKind.Disappeared or WindowEventKind.Shaken))
        {
            // Moves and resizes: the visitor follows its window by itself, and falls if its spot is gone.
            return;
        }

        for (int i = 0; i < _count; i++)
        {
            ref Visitor v = ref _visitors[i];
            if (v.OnSurface && v.Surface.Edge == SurfaceEdge.Top && v.Surface.OwnerId == windowEvent.WindowId && v.State != VisitorState.Held)
            {
                Vector2 push = windowEvent.Kind == WindowEventKind.Shaken
                    ? new Vector2(_random.NextFloat(-220f, 220f), _random.NextFloat(-420f, -280f))
                    : new Vector2(_random.NextFloat(-40f, 40f), -60f);
                Launch(ref v, push * _scale);
            }
        }
    }

    public void Emit(RenderFrame frame, SimulationContext context)
    {
        ArgumentNullException.ThrowIfNull(frame);
        for (int i = 0; i < _count; i++)
        {
            ref Visitor v = ref _visitors[i];
            VisitorProfile profile = VisitorCatalog.Get(v.Kind);
            float height = HeightOf(v.Kind);
            float bob = 0f;
            float stretch = 1f - (0.25f * v.Squash);
            float tilt = 0f;
            switch (v.State)
            {
                case VisitorState.Walking:
                case VisitorState.Sulking:
                    bob = MathF.Abs(MathF.Sin(v.Step * MathF.PI * 2f)) * 2f * _scale;
                    break;
                case VisitorState.Idle:
                    bob = v.Hop > 0f ? MathF.Sin(v.Hop * MathF.PI) * 10f * _scale : 0f;
                    break;
                case VisitorState.Entering:
                    bob = MathF.Sin(Math.Clamp(v.StateTime / EnterSeconds, 0f, 1f) * MathF.PI) * 8f * _scale;
                    break;
                case VisitorState.Airborne:
                    stretch = 1.08f;
                    tilt = Math.Clamp(v.Vx / (600f * _scale), -0.35f, 0.35f);
                    break;
            }

            if (profile.Hovers && v.State != VisitorState.Held)
            {
                bob += (6f + (3f * MathF.Sin((float)(_time * 3.0) + v.Id))) * _scale;
            }

            float fade = v.State switch
            {
                VisitorState.Entering => Math.Clamp(v.StateTime / EnterSeconds, 0f, 1f),
                VisitorState.Leaving => 1f - Math.Clamp(v.StateTime / LeaveSeconds, 0f, 1f),
                _ => 1f,
            };

            frame.AddVisitor(new VisitorPose
            {
                Id = v.Id,
                Kind = v.Kind,
                X = v.X,
                Y = v.Y,
                Height = height,
                Facing = v.Facing,
                Tilt = tilt,
                Stretch = stretch,
                Bob = bob,
                Walking = v.State is VisitorState.Walking or VisitorState.Sulking,
                Step = v.Step - MathF.Floor(v.Step),
                Blink = v.Blink,
                Prop = v.Prop,
                PropAmount = v.PropAmount,
                Expression = Expression(in v, profile),
                Alpha = fade,
                Scale = v.State == VisitorState.Entering ? EaseOutBack(fade) : v.State == VisitorState.Leaving ? 0.6f + (0.4f * fade) : 1f,
                Held = v.State == VisitorState.Held,
            });
        }
    }

    /// <summary>Picks a visitor up (interactive mode). Returns whether there is such a visitor to hold.</summary>
    public bool PickUp(int id)
    {
        int index = IndexOf(id);
        if (index < 0 || _visitors[index].State is VisitorState.Leaving)
        {
            return false;
        }

        ref Visitor v = ref _visitors[index];
        v.State = VisitorState.Held;
        v.StateTime = 0f;
        v.OnSurface = false;
        v.Vx = 0f;
        v.Vy = 0f;
        return true;
    }

    /// <summary>Moves a held visitor so that its middle is at <paramref name="position"/> (desktop pixels).</summary>
    public void Drag(int id, Vector2 position)
    {
        int index = IndexOf(id);
        if (index >= 0 && _visitors[index].State == VisitorState.Held)
        {
            ref Visitor v = ref _visitors[index];
            float height = HeightOf(v.Kind);
            v.Facing = position.X >= v.X ? 1f : -1f;
            v.X = position.X;
            v.Y = position.Y + (height * 0.5f);
        }
    }

    /// <summary>
    /// Lets go of a held visitor. Over the box it is collected (returned, and gone); anywhere else it falls, sulks and
    /// leaves (PLAN.md, section 6).
    /// </summary>
    public VisitorKind? Release(int id, bool overBox)
    {
        int index = IndexOf(id);
        if (index < 0 || _visitors[index].State != VisitorState.Held)
        {
            return null;
        }

        ref Visitor v = ref _visitors[index];
        if (overBox)
        {
            VisitorKind kind = v.Kind;
            RemoveAt(index);
            return kind;
        }

        v.Offended = true;
        Launch(ref v, Vector2.Zero);
        return null;
    }

    /// <summary>
    /// Collects the visitor that came first, for the keyboard and Narrator (PLAN.md, section 6: "collect" without a mouse).
    /// </summary>
    public VisitorKind? CollectOldest()
    {
        for (int i = 0; i < _count; i++)
        {
            if (_visitors[i].State is not (VisitorState.Leaving or VisitorState.Sulking))
            {
                VisitorKind kind = _visitors[i].Kind;
                RemoveAt(i);
                return kind;
            }
        }

        return null;
    }

    /// <summary>Turns a visitor to look left (-1) or right (+1) (tests).</summary>
    internal void TurnTowards(int id, float facing)
    {
        int index = IndexOf(id);
        if (index >= 0)
        {
            _visitors[index].Facing = MathF.Sign(facing);
        }
    }

    /// <summary>Sends every visitor away (visitors switched off).</summary>
    public void LeaveAll()
    {
        for (int i = 0; i < _count; i++)
        {
            if (_visitors[i].State != VisitorState.Leaving)
            {
                Begin(ref _visitors[i], VisitorState.Leaving, LeaveSeconds);
            }
        }
    }

    /// <summary>Drawing height of a visitor in pixels.</summary>
    public float HeightOf(VisitorKind kind) => VisitorCatalog.StandardHeight * VisitorCatalog.Get(kind).Size * _scale;

    private static float EaseOutBack(float t)
    {
        const float C = 1.7f;
        float u = t - 1f;
        return 1f + ((C + 1f) * u * u * u) + (C * u * u);
    }

    private static int PickSpawnSegment(ReadOnlySpan<SurfaceSegment> segments, float minimumLength, bool windows, ref Pcg32 random)
    {
        int candidates = 0;
        foreach (SurfaceSegment segment in segments)
        {
            if (IsSpawnable(segment, minimumLength, windows))
            {
                candidates++;
            }
        }

        if (candidates == 0)
        {
            return -1;
        }

        int pick = random.NextInt(candidates);
        for (int i = 0; i < segments.Length; i++)
        {
            if (IsSpawnable(segments[i], minimumLength, windows) && pick-- == 0)
            {
                return i;
            }
        }

        return -1;
    }

    private static bool IsSpawnable(in SurfaceSegment segment, float minimumLength, bool windows) =>
        segment.Length >= minimumLength && (windows ? segment.Key.Edge == SurfaceEdge.Top : segment.Key.Edge == SurfaceEdge.Floor);

    private int PickSpawnSegment(ReadOnlySpan<SurfaceSegment> segments, float minimumLength, bool windows) =>
        PickSpawnSegment(segments, minimumLength, windows, ref _random);

    private VisitorExpression Expression(in Visitor v, VisitorProfile profile) =>
        v.Offended ? VisitorExpression.Sulky
        : profile.Affinity != WeatherAffinity.Any && Mood.Of(profile.Affinity) >= WeatherMood.Threshold ? VisitorExpression.Happy
        : VisitorExpression.Calm;

    /// <summary>One step of one visitor. Returns <see langword="false"/> when it is gone.</summary>
    private bool Step(ref Visitor v, float dt, SimulationContext context)
    {
        v.StateTime += dt;
        v.Squash = Math.Max(0f, v.Squash - (dt * 4f));
        Blink(ref v, dt);
        UpdateProp(ref v, dt);

        switch (v.State)
        {
            case VisitorState.Entering:
                v.Scale = 1f;
                if (!Follow(ref v, context))
                {
                    Launch(ref v, Vector2.Zero);
                }
                else if (v.StateTime >= EnterSeconds)
                {
                    Begin(ref v, VisitorState.Walking, 0f);
                }

                return true;

            case VisitorState.Walking:
            case VisitorState.Sulking:
                return Walk(ref v, dt, context);

            case VisitorState.Idle:
                if (!Follow(ref v, context))
                {
                    Launch(ref v, Vector2.Zero);
                    return true;
                }

                Linger(ref v, dt);
                if (v.Hop > 0f)
                {
                    v.Hop = Math.Max(0f, v.Hop - (dt * 2.5f));
                }
                else if (Expression(in v, VisitorCatalog.Get(v.Kind)) == VisitorExpression.Happy && _random.NextFloat() < dt * 0.8f)
                {
                    v.Hop = 1f;
                }

                if (v.StateTime >= v.StateDuration)
                {
                    if (_random.NextFloat() < 0.5f)
                    {
                        v.Facing = -v.Facing;
                    }

                    Begin(ref v, VisitorState.Walking, 0f);
                }

                return true;

            case VisitorState.Airborne:
                return Fly(ref v, dt, context);

            case VisitorState.Held:
                return true;

            default:
                return v.StateTime < LeaveSeconds;
        }
    }

    private bool Walk(ref Visitor v, float dt, SimulationContext context)
    {
        if (!Follow(ref v, context, out SurfaceSegment segment))
        {
            Launch(ref v, Vector2.Zero);
            return true;
        }

        VisitorProfile profile = VisitorCatalog.Get(v.Kind);
        bool sulking = v.State == VisitorState.Sulking;
        float speed = profile.WalkSpeed * _scale * (sulking ? 2.2f : 1f);
        float height = HeightOf(v.Kind);
        float margin = height * 0.3f;
        float next = v.X + (v.Facing * speed * dt);
        v.Step += speed * dt / (height * 0.6f);

        if (next < segment.Start + margin || next > segment.End - margin)
        {
            // The end of the visible edge: jump over to a nearby window, or turn around.
            if (TryJump(ref v, context))
            {
                return true;
            }

            v.Facing = -v.Facing;
            if (!sulking)
            {
                Begin(ref v, VisitorState.Idle, _random.NextFloat(0.6f, 1.4f));
            }

            return true;
        }

        v.X = next;
        v.LocalX = next - segment.Origin;

        if (sulking)
        {
            if (v.StateTime >= SulkSeconds)
            {
                Begin(ref v, VisitorState.Leaving, LeaveSeconds);
            }

            return true;
        }

        Linger(ref v, dt);
        if (v.State == VisitorState.Walking && _random.NextFloat() < dt * 0.15f)
        {
            Begin(ref v, VisitorState.Idle, _random.NextFloat(1f, 3f));
        }

        return true;
    }

    /// <summary>Counts down the visit; at its end the visitor leaves.</summary>
    private static void Linger(ref Visitor v, float dt)
    {
        v.VisitLeft -= dt;
        if (v.VisitLeft <= 0f)
        {
            Begin(ref v, VisitorState.Leaving, LeaveSeconds);
        }
    }

    private bool Fly(ref Visitor v, float dt, SimulationContext context)
    {
        float previousY = v.Y;
        v.Vy += Gravity * _scale * dt;
        v.X += v.Vx * dt;
        v.Y += v.Vy * dt;

        if (v.Vy > 0f && context.SurfaceIndex.TryFindLanding(v.X, previousY, v.Y, out SurfaceHit hit))
        {
            SurfaceSegment segment = context.Surfaces.Segments[hit.Segment];
            v.Y = segment.Position;
            v.Surface = segment.Key;
            v.OnSurface = true;
            v.LocalX = v.X - segment.Origin;
            v.Vx = 0f;
            v.Vy = 0f;
            v.Squash = 1f;
            Begin(ref v, v.Offended ? VisitorState.Sulking : VisitorState.Idle, v.Offended ? 0f : _random.NextFloat(0.5f, 1.2f));
            return true;
        }

        // Fell past every surface (off the desktop): gone.
        return v.Y < context.DesktopBounds.Bottom + (200f * _scale);
    }

    /// <summary>
    /// Finds a horizontal edge ahead within reach and jumps there on a ballistic arc. The landing itself is found by
    /// <see cref="Fly"/>, so a window that moves in the meantime is handled like any fall.
    /// </summary>
    private bool TryJump(ref Visitor v, SimulationContext context)
    {
        float height = HeightOf(v.Kind);
        float reach = JumpReach * _scale;
        float climb = JumpClimb * _scale;
        float best = float.MaxValue;
        float targetX = 0f;
        float targetY = 0f;
        foreach (SurfaceSegment segment in context.Surfaces.Segments)
        {
            if (!segment.IsHorizontal || segment.Key == v.Surface || segment.Length < height * 2f
                || segment.Position < v.Y - climb || segment.Position > v.Y + (reach * 3f))
            {
                continue;
            }

            float near = v.Facing > 0f ? segment.Start - v.X : v.X - segment.End;
            if (near < -height || near > reach)
            {
                continue;
            }

            float landing = v.Facing > 0f
                ? Math.Clamp(Math.Max(v.X, segment.Start) + height, segment.Start + height * 0.5f, segment.End - height * 0.5f)
                : Math.Clamp(Math.Min(v.X, segment.End) - height, segment.Start + height * 0.5f, segment.End - height * 0.5f);
            float distance = MathF.Abs(landing - v.X) + MathF.Abs(segment.Position - v.Y);
            if (distance < best)
            {
                best = distance;
                targetX = landing;
                targetY = segment.Position;
            }
        }

        if (best == float.MaxValue)
        {
            return false;
        }

        // Flight time grows with the distance; the start speed makes the arc end on the target.
        float time = 0.45f + (MathF.Abs(targetX - v.X) / (reach * 3f));
        float gravity = Gravity * _scale;
        var velocity = new Vector2((targetX - v.X) / time, (targetY - v.Y - (0.5f * gravity * time * time)) / time);
        Launch(ref v, velocity);
        return true;
    }

    private static void Launch(ref Visitor v, Vector2 velocity)
    {
        v.OnSurface = false;
        v.Vx = velocity.X;
        v.Vy = velocity.Y;
        if (MathF.Abs(velocity.X) > 1f)
        {
            v.Facing = MathF.Sign(velocity.X);
        }

        Begin(ref v, VisitorState.Airborne, 0f);
    }

    /// <summary>Puts a standing visitor where its surface is now. False when the spot is no longer visible.</summary>
    private static bool Follow(ref Visitor v, SimulationContext context) => Follow(ref v, context, out _);

    private static bool Follow(ref Visitor v, SimulationContext context, out SurfaceSegment found)
    {
        found = default;
        if (!v.OnSurface)
        {
            return false;
        }

        foreach (SurfaceSegment segment in context.Surfaces.Segments)
        {
            if (segment.Key != v.Surface)
            {
                continue;
            }

            float x = segment.Origin + v.LocalX;
            if (x >= segment.Start && x < segment.End)
            {
                v.X = x;
                v.Y = segment.Position;
                found = segment;
                return true;
            }
        }

        return false;
    }

    private void Blink(ref Visitor v, float dt)
    {
        v.BlinkTimer -= dt;
        if (v.BlinkTimer <= 0f)
        {
            v.Blink = 1f;
            v.BlinkTimer = _random.NextFloat(2f, 5f);
        }
        else
        {
            v.Blink = Math.Max(0f, v.Blink - (dt * 7f));
        }
    }

    /// <summary>Takes out the prop the weather asks for, and puts it away when the weather changes.</summary>
    private void UpdateProp(ref Visitor v, float dt)
    {
        VisitorProp wanted = PropFor(v.Kind, Mood);
        if (v.Prop == wanted)
        {
            v.PropAmount = Math.Min(1f, v.PropAmount + (dt * PropSpeed));
            return;
        }

        v.PropAmount = Math.Max(0f, v.PropAmount - (dt * PropSpeed));
        if (v.PropAmount <= 0f)
        {
            v.Prop = wanted;
        }
    }

    /// <summary>
    /// The prop for the strongest weather. A visitor in its favourite weather needs none: it just enjoys it (and some
    /// bring their own: Sol's sunglasses, Lumen's light, Bristle's leaf).
    /// </summary>
    public static VisitorProp PropFor(VisitorKind kind, WeatherMood mood)
    {
        WeatherAffinity weather = mood.Dominant;
        if (weather == VisitorCatalog.Get(kind).Affinity)
        {
            return VisitorProp.None;
        }

        return weather switch
        {
            WeatherAffinity.Rain => kind == VisitorKind.Nim ? VisitorProp.None : VisitorProp.Umbrella,
            WeatherAffinity.Snow => VisitorProp.Snowball,
            WeatherAffinity.Sun => VisitorProp.Sunglasses,
            WeatherAffinity.Fog => VisitorProp.Lantern,
            WeatherAffinity.Leaves => VisitorProp.Leaf,
            _ => VisitorProp.None,
        };
    }

    private PixelRect Bounds(in Visitor v)
    {
        float height = HeightOf(v.Kind);
        float width = height * 1.1f;
        return PixelRect.FromSize((int)MathF.Floor(v.X - (width * 0.5f)), (int)MathF.Floor(v.Y - height), (int)MathF.Ceiling(width), (int)MathF.Ceiling(height));
    }

    private int IndexOf(int id)
    {
        for (int i = 0; i < _count; i++)
        {
            if (_visitors[i].Id == id)
            {
                return i;
            }
        }

        return -1;
    }

    private static void Begin(ref Visitor v, VisitorState state, float duration)
    {
        v.State = state;
        v.StateTime = 0f;
        v.StateDuration = duration;
        v.Hop = 0f;
    }

    private void RemoveAt(int index)
    {
        _visitors[index] = _visitors[_count - 1];
        _count--;
    }

    private struct Visitor
    {
        public int Id;
        public VisitorKind Kind;
        public VisitorState State;
        public float StateTime;
        public float StateDuration;
        public float VisitLeft;
        public float X;
        public float Y;
        public float Vx;
        public float Vy;
        public float Facing;
        public SurfaceKey Surface;
        public bool OnSurface;
        public float LocalX;
        public float Step;
        public float BlinkTimer;
        public float Blink;
        public VisitorProp Prop;
        public float PropAmount;
        public float Squash;
        public float Hop;
        public float Scale;
        public bool Offended;
    }
}
