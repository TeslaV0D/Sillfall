using System.Numerics;
using Weatherly.Core.Geometry;

namespace Weatherly.Core.Desktop;

/// <summary>
/// Turns a sequence of window snapshots into window events: appeared, disappeared, moved
/// (with velocity), resized and shaken. Feed it every snapshot in order.
/// </summary>
public sealed class WindowMotionTracker
{
    private readonly Dictionary<long, State> _states = new();
    private readonly HashSet<long> _seen = new();
    private readonly List<long> _gone = new();

    /// <summary>Compares <paramref name="snapshot"/> with the previous one and appends the events to <paramref name="events"/>.</summary>
    public void Update(WindowSnapshot snapshot, List<WindowEvent> events)
    {
        ArgumentNullException.ThrowIfNull(snapshot);
        ArgumentNullException.ThrowIfNull(events);

        double time = snapshot.Timestamp;
        _seen.Clear();

        foreach (TrackedWindow window in snapshot.Windows)
        {
            _seen.Add(window.Id);

            if (!_states.TryGetValue(window.Id, out State? state))
            {
                _states.Add(window.Id, new State(window.Bounds, time));
                events.Add(new WindowEvent(WindowEventKind.Appeared, window.Id, window.Bounds, default, Vector2.Zero));
                continue;
            }

            PixelRect previous = state.Bounds;
            PixelRect current = window.Bounds;
            if (previous == current)
            {
                continue;
            }

            if (previous.Width != current.Width || previous.Height != current.Height)
            {
                events.Add(new WindowEvent(WindowEventKind.Resized, window.Id, current, previous, Vector2.Zero));
            }

            if (previous.Left != current.Left || previous.Top != current.Top)
            {
                double elapsed = Math.Max(time - state.Time, 1e-3);
                var velocity = new Vector2(
                    (float)((current.Left - previous.Left) / elapsed),
                    (float)((current.Top - previous.Top) / elapsed));
                events.Add(new WindowEvent(WindowEventKind.Moved, window.Id, current, previous, velocity));

                bool shakenX = state.ShakeX.Add(time, current.Left);
                bool shakenY = state.ShakeY.Add(time, current.Top);
                if (shakenX || shakenY)
                {
                    // Both axes start over, so one gesture never counts twice.
                    state.ShakeX.Reset();
                    state.ShakeY.Reset();
                    state.ShakeX.Add(time, current.Left);
                    state.ShakeY.Add(time, current.Top);
                    events.Add(new WindowEvent(WindowEventKind.Shaken, window.Id, current, previous, velocity));
                }
            }

            state.Bounds = current;
            state.Time = time;
        }

        _gone.Clear();
        foreach (KeyValuePair<long, State> entry in _states)
        {
            if (!_seen.Contains(entry.Key))
            {
                _gone.Add(entry.Key);
                events.Add(new WindowEvent(WindowEventKind.Disappeared, entry.Key, entry.Value.Bounds, entry.Value.Bounds, Vector2.Zero));
            }
        }

        foreach (long id in _gone)
        {
            _states.Remove(id);
        }
    }

    private sealed class State
    {
        public ShakeDetector ShakeX;
        public ShakeDetector ShakeY;

        public State(PixelRect bounds, double time)
        {
            Bounds = bounds;
            Time = time;

            // The starting position counts: the first swing away from it already sets the direction.
            ShakeX.Add(time, bounds.Left);
            ShakeY.Add(time, bounds.Top);
        }

        public PixelRect Bounds { get; set; }

        public double Time { get; set; }
    }
}
