using System.Numerics;
using Weatherly.Core.Desktop;
using Weatherly.Core.Geometry;
using Weatherly.Core.Rendering;

namespace Weatherly.Sandbox;

/// <summary>
/// Fake windows inside the sandbox window. They can be dragged, raised, added and closed, and they are
/// fed to the engine exactly like real windows, so every effect reacts to them as on the desktop.
/// </summary>
internal sealed class FakeDesktop
{
    private const int TitleBarHeight = 28;

    private static readonly Vector4 s_desktopTop = new(0.16f, 0.22f, 0.32f, 1f);
    private static readonly Vector4 s_desktopBottom = new(0.10f, 0.13f, 0.20f, 1f);
    private static readonly Vector4 s_taskbar = new(0.06f, 0.07f, 0.09f, 1f);
    private static readonly Vector4 s_windowBody = new(0.93f, 0.93f, 0.94f, 1f);
    private static readonly Vector4 s_titleBar = new(0.82f, 0.84f, 0.88f, 1f);
    private static readonly Vector4 s_activeTitleBar = new(0.62f, 0.74f, 0.92f, 1f);

    /// <summary>Topmost first, like a window snapshot.</summary>
    private readonly List<TrackedWindow> _windows = [];
    private long _nextId = 1;
    private long _dragged;
    private Vector2 _dragOffset;

    public int TaskbarHeight { get; } = 40;

    public void Add(PixelRect bounds) => _windows.Insert(0, new TrackedWindow(_nextId++, bounds));

    /// <summary>Opens a new window, cascaded from the topmost one.</summary>
    public void AddCascaded(PixelSize desktop)
    {
        PixelRect from = _windows.Count > 0 ? _windows[0].Bounds : PixelRect.FromSize(40, 40, 0, 0);
        int left = (from.Left + 40) % Math.Max(desktop.Width - 300, 1);
        int top = (from.Top + 40) % Math.Max(desktop.Height - 260, 1);
        Add(PixelRect.FromSize(left, Math.Max(top, 20), 360, 240));
    }

    public void CloseTopmost()
    {
        if (_windows.Count > 0)
        {
            _windows.RemoveAt(0);
        }
    }

    /// <summary>Starts dragging the window under the point, if any, and raises it.</summary>
    public void Press(Vector2 point)
    {
        for (int i = 0; i < _windows.Count; i++)
        {
            if (_windows[i].Bounds.Contains((int)point.X, (int)point.Y))
            {
                TrackedWindow window = _windows[i];
                _windows.RemoveAt(i);
                _windows.Insert(0, window);
                _dragged = window.Id;
                _dragOffset = point - new Vector2(window.Bounds.Left, window.Bounds.Top);
                return;
            }
        }
    }

    public void Drag(Vector2 point)
    {
        if (_dragged == 0 || _windows.Count == 0 || _windows[0].Id != _dragged)
        {
            return;
        }

        PixelRect bounds = _windows[0].Bounds;
        Vector2 topLeft = point - _dragOffset;
        _windows[0] = _windows[0] with { Bounds = PixelRect.FromSize((int)topLeft.X, (int)topLeft.Y, bounds.Width, bounds.Height) };
    }

    public void Release() => _dragged = 0;

    public void Fill(WindowSnapshot snapshot, double time)
    {
        snapshot.Clear();
        snapshot.Timestamp = time;
        foreach (TrackedWindow window in _windows)
        {
            snapshot.Add(window);
        }
    }

    /// <summary>Draws desktop, taskbar and windows (bottom to top) as the first sprites of the frame.</summary>
    public void Draw(RenderFrame frame, PixelSize desktop)
    {
        // Desktop as a few horizontal bands, a cheap gradient.
        const int Bands = 12;
        float bandHeight = desktop.Height / (float)Bands;
        for (int i = 0; i < Bands; i++)
        {
            Vector4 color = Vector4.Lerp(s_desktopTop, s_desktopBottom, i / (float)(Bands - 1));
            frame.Add(Rectangle(0, i * bandHeight, desktop.Width, bandHeight + 1, color));
        }

        frame.Add(Rectangle(0, desktop.Height - TaskbarHeight, desktop.Width, TaskbarHeight, s_taskbar));

        for (int i = _windows.Count - 1; i >= 0; i--)
        {
            PixelRect bounds = _windows[i].Bounds;
            frame.Add(Rectangle(bounds.Left, bounds.Top, bounds.Width, bounds.Height, s_windowBody));
            frame.Add(Rectangle(bounds.Left, bounds.Top, bounds.Width, Math.Min(TitleBarHeight, bounds.Height), i == 0 ? s_activeTitleBar : s_titleBar));
        }
    }

    private static Sprite Rectangle(float left, float top, float width, float height, Vector4 color) =>
        new(new Vector2(left + (width / 2), top + (height / 2)), new Vector2(width / 2, height / 2), 0f, SpriteShape.Rectangle, color);
}
