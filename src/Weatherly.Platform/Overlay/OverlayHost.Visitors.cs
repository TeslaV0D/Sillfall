using Weatherly.Core.Geometry;
using Weatherly.Core.Rendering;
using Weatherly.Core.Visitors;
using Windows.Win32;
using Windows.Win32.Foundation;
using Windows.Win32.UI.Input.KeyboardAndMouse;
using Windows.Win32.UI.WindowsAndMessaging;

namespace Weatherly.Platform.Overlay;

/// <summary>
/// Interactive visitors (PLAN.md, section 6). The overlay stays click-through; every visible visitor gets a small
/// window of its own that follows it: invisible (alpha 1/255), topmost, never activated. Only there can it be picked up.
/// While it is held a box appears in the corner of the primary monitor; dropped over the box it is collected, anywhere
/// else it sulks and leaves. Ctrl+Alt+V collects the first visitor without a mouse.
/// </summary>
public sealed partial class OverlayHost
{
    /// <summary>The keyboard alternative: Ctrl+Alt+V collects the visitor that came first.</summary>
    public const string CollectShortcut = "Ctrl+Alt+V";

    private const int CollectHotkeyId = 1;
    private const uint CollectHotkeyKey = 0x56; // V

    /// <summary>Size of the box and its gap to the work area's corner, in device-independent pixels.</summary>
    private const float BoxSize = 84f;
    private const float BoxMargin = 24f;

    private readonly nint[] _hitWindows = new nint[VisitorSystem.Capacity];
    private readonly int[] _hitVisitors = new int[VisitorSystem.Capacity];
    private readonly PixelRect[] _hitBounds = new PixelRect[VisitorSystem.Capacity];
    private int _heldVisitor;
    private float _boxVisibility;
    private bool _hotkeyRegistered;
    private HCURSOR _handCursor;

    /// <summary>A visitor appeared. Raised on the overlay thread.</summary>
    public event EventHandler<VisitorEventArgs>? VisitorArrived;

    /// <summary>A visitor was dropped outside the box and leaves offended. Raised on the overlay thread.</summary>
    public event EventHandler<VisitorEventArgs>? VisitorDropped;

    /// <summary>Whether Ctrl+Alt+V could be registered (another program may own it).</summary>
    public bool CollectShortcutRegistered => Volatile.Read(ref _hotkeyRegistered);

    /// <summary>Where the box stands: bottom right of the primary monitor's work area.</summary>
    private PixelRect BoxBounds
    {
        get
        {
            Core.Displays.MonitorInfo? primary = null;
            for (int i = 0; i < _monitors.Count; i++)
            {
                if (_monitors[i].IsPrimary)
                {
                    primary = _monitors[i];
                }
            }

            if (primary is null)
            {
                return default;
            }

            float scale = primary.ScaleFactor;
            int size = (int)MathF.Round(BoxSize * scale);
            int margin = (int)MathF.Round(BoxMargin * scale);
            PixelRect area = primary.WorkArea;
            return PixelRect.FromSize(area.Right - size - margin, area.Bottom - size - margin, size, size);
        }
    }

    private void RegisterCollectShortcut()
    {
        _hotkeyRegistered = PInvoke.RegisterHotKey(
            (HWND)_controlWindow, CollectHotkeyId, HOT_KEY_MODIFIERS.MOD_CONTROL | HOT_KEY_MODIFIERS.MOD_ALT | HOT_KEY_MODIFIERS.MOD_NOREPEAT, CollectHotkeyKey);
    }

    private void UnregisterCollectShortcut()
    {
        if (_hotkeyRegistered)
        {
            PInvoke.UnregisterHotKey((HWND)_controlWindow, CollectHotkeyId);
            _hotkeyRegistered = false;
        }
    }

    /// <summary>Collects the first visitor on the overlay thread (hotkey, <see cref="CollectVisitor"/>).</summary>
    private VisitorKind? CollectFirstVisitor()
    {
        if (_heldVisitor != 0)
        {
            return null;
        }

        VisitorKind? kind = _visitors.CollectOldest();
        if (kind is not null)
        {
            VisitorCollected?.Invoke(this, new VisitorCollectedEventArgs(kind.Value, _visitors.Mood));
            SyncHitWindows();
        }

        return kind;
    }

    private void OnVisitorArrived(VisitorKind kind) => VisitorArrived?.Invoke(this, new VisitorEventArgs(kind));

    private bool IsHitWindow(HWND window)
    {
        foreach (nint hit in _hitWindows)
        {
            if (hit != 0 && hit == (nint)window)
            {
                return true;
            }
        }

        return false;
    }

    /// <summary>
    /// Puts a hit window over every visitor (interactive mode only) and hides the rest. Called after every frame; moves
    /// a window only when its visitor moved, so a standing visitor costs nothing.
    /// </summary>
    private void SyncHitWindows()
    {
        int used = 0;
        if (_options.VisitorsInteractive && _overlays.Count > 0)
        {
            for (int i = 0; i < _visitors.Count && used < _hitWindows.Length; i++)
            {
                VisitorInfo visitor = _visitors[i];
                if (visitor.State is VisitorState.Leaving or VisitorState.Sulking)
                {
                    continue;
                }

                PlaceHitWindow(used++, visitor.Id, visitor.Bounds);
            }
        }

        for (int slot = used; slot < _hitWindows.Length; slot++)
        {
            if (_hitVisitors[slot] != 0)
            {
                // The held visitor's window keeps the mouse capture until the button is released.
                if (_hitVisitors[slot] == _heldVisitor)
                {
                    continue;
                }

                PInvoke.ShowWindow((HWND)_hitWindows[slot], SHOW_WINDOW_CMD.SW_HIDE);
                _hitVisitors[slot] = 0;
                _hitBounds[slot] = default;
            }
        }
    }

    private void PlaceHitWindow(int slot, int visitorId, PixelRect bounds)
    {
        if (_hitWindows[slot] == 0)
        {
            _hitWindows[slot] = CreateHitWindow();
        }

        if (_hitVisitors[slot] == visitorId && _hitBounds[slot] == bounds)
        {
            return;
        }

        _hitVisitors[slot] = visitorId;
        _hitBounds[slot] = bounds;
        PInvoke.SetWindowPos(
            (HWND)_hitWindows[slot],
            HWND.HWND_TOPMOST,
            bounds.Left,
            bounds.Top,
            bounds.Width,
            bounds.Height,
            SET_WINDOW_POS_FLAGS.SWP_NOACTIVATE | SET_WINDOW_POS_FLAGS.SWP_SHOWWINDOW);
    }

    /// <summary>
    /// A window that only catches the mouse: layered with alpha 1 (invisible, yet hit-testable, unlike alpha 0), a tool
    /// window that never takes the focus. Its class carries the overlay prefix, so window tracking ignores it.
    /// </summary>
    private unsafe nint CreateHitWindow()
    {
        HWND window;
        fixed (char* className = _windowClass)
        {
            window = PInvoke.CreateWindowEx(
                WINDOW_EX_STYLE.WS_EX_LAYERED | WINDOW_EX_STYLE.WS_EX_TOPMOST | WINDOW_EX_STYLE.WS_EX_TOOLWINDOW | WINDOW_EX_STYLE.WS_EX_NOACTIVATE,
                className,
                null,
                WINDOW_STYLE.WS_POPUP,
                0,
                0,
                1,
                1,
                HWND.Null,
                HMENU.Null,
                _module,
                null);
        }

        if (window.IsNull)
        {
            throw new Win32ErrorException("CreateWindowEx (visitor hit window)");
        }

        PInvoke.SetLayeredWindowAttributes(window, new COLORREF(0), 1, LAYERED_WINDOW_ATTRIBUTES_FLAGS.LWA_ALPHA);
        return window;
    }

    private void DestroyHitWindows()
    {
        for (int slot = 0; slot < _hitWindows.Length; slot++)
        {
            if (_hitWindows[slot] != 0)
            {
                PInvoke.DestroyWindow((HWND)_hitWindows[slot]);
                _hitWindows[slot] = 0;
                _hitVisitors[slot] = 0;
            }
        }
    }

    /// <summary>Handles the mouse on a hit window. Returns whether the message was taken care of.</summary>
    private bool OnHitWindowMessage(HWND window, uint message, out LRESULT result)
    {
        result = new LRESULT(0);
        switch (message)
        {
            case PInvoke.WM_SETCURSOR when IsHitWindow(window):
                if (_handCursor.IsNull)
                {
                    _handCursor = PInvoke.LoadCursor(HINSTANCE.Null, PInvoke.IDC_HAND);
                }

                PInvoke.SetCursor(_handCursor);
                result = new LRESULT(1);
                return true;

            case PInvoke.WM_LBUTTONDOWN when IsHitWindow(window):
                PickUp(window);
                return true;

            case PInvoke.WM_MOUSEMOVE when _heldVisitor != 0:
                DragHeld();
                return true;

            case PInvoke.WM_LBUTTONUP when _heldVisitor != 0:
                Drop(cancelled: false);
                return true;

            // Another window took the mouse (a dialog, Alt+Tab): the visitor is let go where it is.
            case PInvoke.WM_CAPTURECHANGED when _heldVisitor != 0:
                Drop(cancelled: true);
                return true;
        }

        return false;
    }

    private void PickUp(HWND window)
    {
        for (int slot = 0; slot < _hitWindows.Length; slot++)
        {
            if (_hitWindows[slot] == (nint)window && _hitVisitors[slot] != 0 && _visitors.PickUp(_hitVisitors[slot]))
            {
                _heldVisitor = _hitVisitors[slot];
                PInvoke.SetCapture(window);
                DragHeld();
                UpdateAnimation();
                return;
            }
        }
    }

    private void DragHeld()
    {
        if (PInvoke.GetCursorPos(out System.Drawing.Point cursor))
        {
            _visitors.Drag(_heldVisitor, new System.Numerics.Vector2(cursor.X, cursor.Y));
        }
    }

    private void Drop(bool cancelled)
    {
        int id = _heldVisitor;

        // Cleared first: releasing the capture sends WM_CAPTURECHANGED, which must not drop the visitor twice.
        _heldVisitor = 0;
        if (!cancelled)
        {
            PInvoke.ReleaseCapture();
        }

        bool overBox = !cancelled && PInvoke.GetCursorPos(out System.Drawing.Point cursor) && BoxBounds.Contains(cursor.X, cursor.Y);
        VisitorKind? kind = null;
        for (int i = 0; i < _visitors.Count; i++)
        {
            if (_visitors[i].Id == id)
            {
                kind = _visitors[i].Kind;
            }
        }

        if (_visitors.Release(id, overBox) is { } collected)
        {
            VisitorCollected?.Invoke(this, new VisitorCollectedEventArgs(collected, _visitors.Mood));
        }
        else if (kind is { } dropped)
        {
            VisitorDropped?.Invoke(this, new VisitorEventArgs(dropped));
        }

        SyncHitWindows();
        UpdateAnimation();
    }

    /// <summary>The box fades in while a visitor is held and lights up while it is over it.</summary>
    private void UpdateBox(double elapsed)
    {
        float target = _heldVisitor != 0 ? 1f : 0f;
        float step = (float)elapsed * 5f;
        _boxVisibility = target > _boxVisibility ? Math.Min(target, _boxVisibility + step) : Math.Max(target, _boxVisibility - step);
        if (_boxVisibility <= 0f)
        {
            return;
        }

        PixelRect bounds = BoxBounds;
        bool hot = _heldVisitor != 0 && PInvoke.GetCursorPos(out System.Drawing.Point cursor) && bounds.Contains(cursor.X, cursor.Y);
        _engine.Frame.Box = new VisitorBox(bounds, _boxVisibility, hot);
    }

    /// <summary>The box, for the smoke test and the status.</summary>
    private PixelRect CurrentBoxBounds() => BoxBounds;
}

/// <summary>Which visitor something happened to.</summary>
public sealed class VisitorEventArgs(VisitorKind kind) : EventArgs
{
    public VisitorKind Kind { get; } = kind;
}
