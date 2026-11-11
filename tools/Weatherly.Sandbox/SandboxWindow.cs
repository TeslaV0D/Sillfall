using System.Diagnostics;
using System.Numerics;
using System.Runtime.InteropServices;
using Weatherly.Core;
using Weatherly.Core.Desktop;
using Weatherly.Core.Displays;
using Weatherly.Core.Effects;
using Weatherly.Core.Geometry;
using Weatherly.Core.Overlay;
using Weatherly.Core.Rendering;
using Weatherly.Render;
using Windows.Win32;
using Windows.Win32.Foundation;
using Windows.Win32.UI.HiDpi;
using Windows.Win32.UI.Input.KeyboardAndMouse;
using Windows.Win32.UI.WindowsAndMessaging;

namespace Weatherly.Sandbox;

/// <summary>A normal top-level window that renders the effects over a fake desktop.</summary>
internal sealed class SandboxWindow : IDisposable
{
    private const string ClassName = "Weatherly.Sandbox";

    private readonly WNDPROC _windowProcedure;
    private readonly CompositionOverlayRenderer _renderer = new();
    private readonly SimulationEngine _engine = new(seed: 3);
    private readonly TestDropsEffect _drops = new() { Intensity = 1f };
    private readonly Core.Effects.Rain.RainEffect _rain = new(seed: 3);
    private readonly Core.Effects.Snow.SnowEffect _snow = new(seed: 3);
    private readonly Core.Effects.Leaves.LeafEffect _leaves = new(seed: 3);
    private readonly Core.Simulation.CursorMotion _cursor = new();
    private Vector2 _mouse = new(-10_000, -10_000);
    private readonly FakeDesktop _desktop = new();
    private readonly WindowSnapshot _snapshot = new();
    private readonly RenderFrame _scene = new();
    private readonly HWND _window;
    private IOverlaySurface? _surface;
    private PixelSize _size;
    private bool _showSurfaces;
    private bool _fillSnow;
    private bool _scatterLeaves;
    private bool _closed;

    /// <param name="effect">Effect to start with: "drops", "rain", "snow" or "leaves".</param>
    /// <param name="fillSnow">Start with snow lying on every window, ready to slide off.</param>
    /// <param name="scatterLeaves">Start with leaves lying on every window, ready to be blown or shaken off.</param>
    public SandboxWindow(string effect = "drops", bool fillSnow = false, bool scatterLeaves = false)
    {
        PInvoke.SetProcessDpiAwarenessContext(DPI_AWARENESS_CONTEXT.DPI_AWARENESS_CONTEXT_PER_MONITOR_AWARE_V2);
        _windowProcedure = WindowProcedure;
        _drops.Intensity = effect == "drops" ? 1f : 0f;
        _rain.Settings = new EffectSettings { Intensity = effect == "rain" ? 0.7f : 0f };
        _snow.Settings = new EffectSettings { Intensity = effect == "snow" ? 0.7f : 0f };
        _leaves.Settings = new EffectSettings { Intensity = effect == "leaves" ? 0.7f : 0f };
        _fillSnow = fillSnow;
        _scatterLeaves = scatterLeaves;
        _engine.Add(_drops);
        _engine.Add(_rain);
        _engine.Add(_snow);
        _engine.Add(_leaves);
        _window = CreateWindow();

        _desktop.Add(PixelRect.FromSize(80, 120, 460, 300));
        _desktop.Add(PixelRect.FromSize(420, 220, 420, 280));
        _desktop.Add(PixelRect.FromSize(760, 80, 360, 420));
    }

    /// <summary>Runs until the window is closed (or <paramref name="exitAfterSeconds"/> passed). Returns the frames rendered.</summary>
    public long Run(double? exitAfterSeconds)
    {
        PInvoke.ShowWindow(_window, SHOW_WINDOW_CMD.SW_SHOWNORMAL);
        var clock = Stopwatch.StartNew();
        double last = 0;
        long frames = 0;

        while (!_closed)
        {
            while (PInvoke.PeekMessage(out MSG message, HWND.Null, 0, 0, PEEK_MESSAGE_REMOVE_TYPE.PM_REMOVE))
            {
                if (message.message == PInvoke.WM_QUIT)
                {
                    _closed = true;
                    break;
                }

                PInvoke.TranslateMessage(message);
                PInvoke.DispatchMessage(message);
            }

            if (_closed || _surface is null)
            {
                Thread.Sleep(1);
                continue;
            }

            double now = clock.Elapsed.TotalSeconds;
            _desktop.Fill(_snapshot, now);
            _engine.SetWindows(_snapshot);
            if (_fillSnow)
            {
                _fillSnow = false;
                _snow.Fill(_engine.Context, 18f);
            }

            if (_scatterLeaves)
            {
                _scatterLeaves = false;
                _leaves.Scatter(_engine.Context, 3f);
            }

            _snow.RainIntensity = _rain.Settings.ClampedIntensity;
            _cursor.Update(_mouse, now);
            _engine.SetCursor(_cursor.Position, _cursor.Velocity);
            _engine.Advance(now - last);
            last = now;

            _scene.Clear();
            _desktop.Draw(_scene, _size);
            foreach (Sprite sprite in _engine.Frame.Sprites)
            {
                _scene.Add(sprite);
            }

            var frame = new OverlayFrame(
                OverlayContent.None,
                PInvoke.GetDpiForWindow(_window) / (float)MonitorInfo.DefaultDpi,
                PixelRect.FromSize(0, 0, _size.Width, _size.Height),
                _showSurfaces ? _engine.Context.Surfaces : null,
                _scene);
            if (_surface.Present(frame) == PresentResult.DeviceLost)
            {
                RecreateSurface(recreateDevice: true);
            }

            frames++;
            if (exitAfterSeconds is { } limit && now >= limit)
            {
                _closed = true;
            }
        }

        return frames;
    }

    public void Dispose()
    {
        _surface?.Dispose();
        _renderer.Dispose();
    }

    private void RecreateSurface(bool recreateDevice = false)
    {
        _surface?.Dispose();
        _surface = null;
        if (recreateDevice)
        {
            _renderer.RecreateDevice();
        }

        PInvoke.GetClientRect(_window, out RECT client);
        _size = new PixelSize(client.right - client.left, client.bottom - client.top);
        if (_size.IsEmpty)
        {
            return;
        }

        _surface = _renderer.CreateSurface(_window, _size);

        // The sandbox window is the whole "desktop": one monitor, with a taskbar at the bottom.
        PixelRect bounds = PixelRect.FromSize(0, 0, _size.Width, _size.Height);
        PixelRect workArea = PixelRect.FromSize(0, 0, _size.Width, _size.Height - _desktop.TaskbarHeight);
        _engine.SetMonitors([new MonitorInfo(@"\\.\SANDBOX", bounds, workArea, (int)PInvoke.GetDpiForWindow(_window), IsPrimary: true)]);
    }

    private LRESULT WindowProcedure(HWND window, uint message, WPARAM wParam, LPARAM lParam)
    {
        switch (message)
        {
            case PInvoke.WM_SIZE:
                if (window == _window)
                {
                    RecreateSurface();
                }

                return new LRESULT(0);

            case PInvoke.WM_LBUTTONDOWN:
                PInvoke.SetCapture(window);
                _desktop.Press(PointOf(lParam));
                return new LRESULT(0);

            case PInvoke.WM_MOUSEMOVE:
                _mouse = PointOf(lParam);
                _desktop.Drag(_mouse);
                return new LRESULT(0);

            case PInvoke.WM_LBUTTONUP:
                PInvoke.ReleaseCapture();
                _desktop.Release();
                return new LRESULT(0);

            case PInvoke.WM_KEYDOWN:
                OnKey((VIRTUAL_KEY)wParam.Value);
                return new LRESULT(0);

            case PInvoke.WM_DESTROY:
                _closed = true;
                PInvoke.PostQuitMessage(0);
                return new LRESULT(0);
        }

        return PInvoke.DefWindowProc(window, message, wParam, lParam);
    }

    private void OnKey(VIRTUAL_KEY key)
    {
        switch (key)
        {
            case VIRTUAL_KEY.VK_D:
                _drops.Intensity = _drops.Intensity > 0 ? 0f : 1f;
                break;
            case VIRTUAL_KEY.VK_R:
                _rain.Settings = new EffectSettings { Intensity = _rain.Settings.Intensity > 0 ? 0f : 0.7f };
                break;
            case VIRTUAL_KEY.VK_W:
                _snow.Settings = new EffectSettings { Intensity = _snow.Settings.Intensity > 0 ? 0f : 0.7f };
                break;
            case VIRTUAL_KEY.VK_F:
                _fillSnow = true;
                break;
            case VIRTUAL_KEY.VK_L:
                _leaves.Settings = new EffectSettings { Intensity = _leaves.Settings.Intensity > 0 ? 0f : 0.7f };
                break;
            case VIRTUAL_KEY.VK_K:
                _scatterLeaves = true;
                break;
            case VIRTUAL_KEY.VK_S:
                _showSurfaces = !_showSurfaces;
                break;
            case VIRTUAL_KEY.VK_N:
                _desktop.AddCascaded(_size);
                break;
            case VIRTUAL_KEY.VK_DELETE:
                _desktop.CloseTopmost();
                break;
            case VIRTUAL_KEY.VK_LEFT:
                _engine.Context.Wind.Base -= new Vector2(60, 0);
                break;
            case VIRTUAL_KEY.VK_RIGHT:
                _engine.Context.Wind.Base += new Vector2(60, 0);
                break;
            case VIRTUAL_KEY.VK_ESCAPE:
                _closed = true;
                break;
        }

        UpdateTitle();
    }

    private void UpdateTitle()
    {
        static string OnOff(bool on) => on ? "on" : "off";
        string title = $"{AppInfo.Name} Sandbox  |  drops {OnOff(_drops.Intensity > 0)}  |  rain {OnOff(_rain.Settings.Intensity > 0)}  |  snow {OnOff(_snow.Settings.Intensity > 0)}  |  leaves {OnOff(_leaves.Settings.Intensity > 0)}  |  surfaces {OnOff(_showSurfaces)}  |  wind {_engine.Context.Wind.Base.X:0} px/s  |  D R W F L K S N Del Left Right Esc";
        PInvoke.SetWindowText(_window, title);
    }

    private static Vector2 PointOf(LPARAM lParam)
    {
        // Low word x, high word y, both signed (the cursor can leave the window while captured).
        short x = (short)(lParam.Value & 0xFFFF);
        short y = (short)((lParam.Value >> 16) & 0xFFFF);
        return new Vector2(x, y);
    }

    private unsafe HWND CreateWindow()
    {
        HINSTANCE module = (HINSTANCE)PInvoke.GetModuleHandle((PCWSTR)null).Value;
        fixed (char* className = ClassName)
        {
            var windowClass = new WNDCLASSEXW
            {
                cbSize = (uint)Marshal.SizeOf<WNDCLASSEXW>(),
                lpfnWndProc = _windowProcedure,
                hInstance = module,
                hCursor = PInvoke.LoadCursor(HINSTANCE.Null, PInvoke.IDC_ARROW),
                lpszClassName = className,
            };
            PInvoke.RegisterClassEx(windowClass);

            // 1280 x 800 client area at 100 %, scaled for the primary monitor's DPI is left to Windows.
            var rect = new RECT { left = 0, top = 0, right = 1280, bottom = 800 };
            PInvoke.AdjustWindowRectExForDpi(ref rect, WINDOW_STYLE.WS_OVERLAPPEDWINDOW, false, 0, MonitorInfo.DefaultDpi);

            string titleText = $"{AppInfo.Name} Sandbox";
            HWND window;
            fixed (char* title = titleText)
            {
                window = PInvoke.CreateWindowEx(
                    0,
                    className,
                    title,
                    WINDOW_STYLE.WS_OVERLAPPEDWINDOW,
                    PInvoke.CW_USEDEFAULT,
                    PInvoke.CW_USEDEFAULT,
                    rect.right - rect.left,
                    rect.bottom - rect.top,
                    HWND.Null,
                    HMENU.Null,
                    module,
                    null);
            }

            return window.IsNull ? throw new InvalidOperationException("The sandbox window could not be created.") : window;
        }
    }
}
