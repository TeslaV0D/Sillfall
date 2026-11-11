using System.Collections.Concurrent;
using System.Diagnostics;
using System.Runtime.ExceptionServices;
using System.Runtime.InteropServices;
using Weatherly.Core.Desktop;
using Weatherly.Core.Displays;
using Weatherly.Core.Effects;
using Weatherly.Core.Overlay;
using Weatherly.Core.Surfaces;
using Weatherly.Platform.Desktop;
using Weatherly.Platform.Displays;
using Windows.Win32;
using Windows.Win32.Foundation;
using Windows.Win32.UI.Accessibility;
using Windows.Win32.UI.HiDpi;
using Windows.Win32.UI.WindowsAndMessaging;

namespace Weatherly.Platform.Overlay;

/// <summary>
/// Owns the overlay windows: one transparent, click-through, topmost window per monitor.
/// <para>
/// Everything runs on a dedicated thread with its own message loop, so the overlays do not depend on
/// the UI framework of the host application and a busy UI can never stall them. Public members are
/// thread-safe: they hand their work to the overlay thread and wait for the result.
/// </para>
/// <para>
/// The host also tracks the application windows (event-driven through out-of-context WinEvent hooks,
/// with a 10 Hz poll as fallback), coalesces changes to at most one snapshot per frame and computes
/// the visible surfaces from them.
/// </para>
/// <para>
/// While an effect is active, the thread runs a render loop paced by the swap chains (one frame per
/// display refresh at most). When no effect is active, the loop stops and the thread blocks in its
/// message loop: no frames, no GPU work (PLAN.md, section 2).
/// </para>
/// <para>
/// The host follows the monitor setup. When a monitor is plugged in, removed, moved, resized or
/// rescaled, the affected overlays are rebuilt (see <see cref="OverlayLayoutPlanner"/>).
/// </para>
/// </summary>
public sealed class OverlayHost : IDisposable
{
    private const uint CommandMessage = PInvoke.WM_APP + 1;
    private const uint StopMessage = PInvoke.WM_APP + 2;
    private const nuint RefreshTimerId = 1;
    private const nuint TrackingTimerId = 2;
    private const nuint PollTimerId = 3;

    /// <summary>Window events are coalesced: at most one snapshot per frame (60 Hz).</summary>
    private const uint TrackingDelayMilliseconds = 16;

    /// <summary>Fallback for changes that raise no event (PLAN.md, section 3.2: 10 Hz).</summary>
    private const uint PollIntervalMilliseconds = 100;

    /// <summary>Class name prefix of all overlay windows. The window tracker skips windows with it.</summary>
    private const string OverlayClassPrefix = "Weatherly.Overlay.";

    /// <summary>
    /// Display changes arrive as a burst of messages (one per window and per changed property).
    /// The layout is recomputed once, shortly after the last one.
    /// </summary>
    private const uint RefreshDelayMilliseconds = 250;

    private const int DeviceRecoveryAttempts = 3;

    /// <summary>Frames after the start of the loop that are not measured for allocations (JIT, buffer growth).</summary>
    private const int AllocationWarmUpFrames = 60;

    /// <summary>Upper frame rate. Normally the swap chain paces the loop; this only guards against a busy loop.</summary>
    private const double MinimumFrameSeconds = 1.0 / 240;

    private const int StateCreated = 0;
    private const int StateStarted = 1;
    private const int StateDisposed = 2;

    private readonly IOverlayRenderer _renderer;
    private readonly Thread _thread;
    private readonly WNDPROC _windowProcedure;
    private readonly WINEVENTPROC _winEventCallback;
    private readonly string _windowClass = $"{OverlayClassPrefix}{Guid.NewGuid():N}";
    private readonly ConcurrentQueue<Command> _commands = new();
    private readonly ManualResetEventSlim _startCompleted = new();
    private readonly Dictionary<string, OverlayWindow> _overlays = new(StringComparer.OrdinalIgnoreCase);

    private int _state = StateCreated;
    private volatile nint _controlWindow;
    private Exception? _startupError;

    // Only touched on the overlay thread.
    private HINSTANCE _module;
    private readonly List<HWINEVENTHOOK> _hooks = [];
    private OverlayOptions _options;
    private IReadOnlyList<MonitorInfo> _monitors = [];
    private string? _lastError;

    // Window tracking, only touched on the overlay thread. All buffers are reused.
    private readonly WindowEnumerator _enumerator = new(OverlayClassPrefix);
    private readonly WindowSnapshot _capture = new();
    private readonly SimulationEngine _engine = new();
    private readonly TestDropsEffect _testDrops = new();
    private readonly Core.Effects.Rain.RainEffect _rain = new();
    private readonly Core.Effects.Snow.SnowEffect _snow = new();
    private readonly Core.Effects.Leaves.LeafEffect _leaves = new();
    private readonly Core.Effects.Fog.FogEffect _fog = new();
    private readonly Core.Effects.Sun.SunEffect _sun = new();
    private readonly Core.Simulation.CursorMotion _cursor = new();
    private bool _trackingPending;
    private float _rainHitsPerSecond;

    // Render loop state and statistics, only touched on the overlay thread.
    private bool _animating;
    private long _lastFrameTimestamp;
    private long _animationFrames;
    private long _loopFramesSinceStart;
    private long _measuredLoopFrames;
    private long _loopAllocatedBytes;
    private readonly FrameStatistics _frameStatistics = new();

    /// <summary>Creates the host. It takes ownership of <paramref name="renderer"/> and disposes it on the overlay thread.</summary>
    public OverlayHost(IOverlayRenderer renderer, OverlayOptions options)
    {
        ArgumentNullException.ThrowIfNull(renderer);
        ArgumentNullException.ThrowIfNull(options);

        _renderer = renderer;
        _options = options;
        _windowProcedure = WindowProcedure;
        _winEventCallback = OnWinEvent;
        // Draw order follows PLAN.md, section 4.7: sun and fog behind, then snow piles, leaves, snow, rain.
        _engine.Add(_sun);
        _engine.Add(_fog);
        _engine.Add(_testDrops);
        _engine.Add(_rain);
        _engine.Add(_snow);
        _engine.Add(_leaves);
        _thread = new Thread(Run)
        {
            Name = "Weatherly overlay",
            IsBackground = true,
        };
    }

    /// <summary>
    /// Raised after the overlays were rebuilt because the monitor setup changed.
    /// Runs on the overlay thread: handlers must not block and must marshal to their own thread.
    /// </summary>
    public event EventHandler? LayoutChanged;

    /// <summary>Starts the overlay thread and creates the overlays. Returns when they are on screen.</summary>
    public void Start()
    {
        if (Interlocked.CompareExchange(ref _state, StateStarted, StateCreated) != StateCreated)
        {
            throw new InvalidOperationException("The overlay host was already started or disposed.");
        }

        _thread.Start();
        _startCompleted.Wait();

        if (_startupError is { } error)
        {
            _thread.Join();
            ExceptionDispatchInfo.Capture(error).Throw();
        }
    }

    /// <summary>Applies new options: enables or disables the overlays, toggles capture exclusion, changes the content.</summary>
    public void Apply(OverlayOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);

        Invoke(() =>
        {
            _options = options;
            RefreshOverlays();
            return true;
        });
    }

    /// <summary>Splashes per second of the rain in the last frame. Thread-safe, for the rain sound.</summary>
    public float RainHitsPerSecond => Volatile.Read(ref _rainHitsPerSecond);

    /// <summary>Measures the current state of all overlays.</summary>
    public OverlayStatus GetStatus() => Invoke(CaptureStatus);

    /// <summary>Removes all overlays and stops the overlay thread.</summary>
    public void Dispose()
    {
        int previousState = Interlocked.Exchange(ref _state, StateDisposed);
        if (previousState == StateCreated)
        {
            _renderer.Dispose();
        }
        else if (previousState == StateStarted)
        {
            // Without a control window the thread never got going or is already gone: nothing to stop.
            nint controlWindow = _controlWindow;
            if (controlWindow != 0)
            {
                PInvoke.PostMessage((HWND)controlWindow, StopMessage, 0, 0);
            }

            if (Environment.CurrentManagedThreadId != _thread.ManagedThreadId)
            {
                _thread.Join();
            }
        }

        if (previousState != StateDisposed && Environment.CurrentManagedThreadId != _thread.ManagedThreadId)
        {
            _startCompleted.Dispose();
        }
    }

    private T Invoke<T>(Func<T> action)
    {
        int state = Volatile.Read(ref _state);
        ObjectDisposedException.ThrowIf(state == StateDisposed, this);
        if (state != StateStarted)
        {
            throw new InvalidOperationException("The overlay host is not started.");
        }

        if (Environment.CurrentManagedThreadId == _thread.ManagedThreadId)
        {
            return action();
        }

        var completion = new TaskCompletionSource<T>(TaskCreationOptions.RunContinuationsAsynchronously);
        _commands.Enqueue(new Command(
            () =>
            {
                try
                {
                    completion.SetResult(action());
                }
#pragma warning disable CA1031 // The failure belongs to the caller that waits for this command, it is rethrown there.
                catch (Exception exception)
#pragma warning restore CA1031
                {
                    completion.SetException(exception);
                }
            },
            () => completion.SetException(new ObjectDisposedException(nameof(OverlayHost)))));

        nint controlWindow = _controlWindow;
        if (controlWindow == 0 || !PInvoke.PostMessage((HWND)controlWindow, CommandMessage, 0, 0))
        {
            // The overlay thread has ended. Its shutdown cancels queued commands,
            // but this one may have been queued too late for that.
            CancelPendingCommands();
        }

        return completion.Task.GetAwaiter().GetResult();
    }

    private void Run()
    {
        try
        {
            // Explicit, so coordinates are physical pixels even in a host process without a DPI manifest.
            PInvoke.SetThreadDpiAwarenessContext(DPI_AWARENESS_CONTEXT.DPI_AWARENESS_CONTEXT_PER_MONITOR_AWARE_V2);

            _module = GetModule();
            RegisterWindowClass();
            _controlWindow = CreateControlWindow();
            HookWindowEvents();
            RefreshOverlays();
        }
#pragma warning disable CA1031 // Rethrown on the thread that called Start.
        catch (Exception exception)
#pragma warning restore CA1031
        {
            _startupError = exception;
        }
        finally
        {
            _startCompleted.Set();
        }

        if (_startupError is null)
        {
            RunMessageLoop();
        }

        Shutdown();
    }

    /// <summary>
    /// Blocks in <c>GetMessage</c> while idle. While animating, drains the queue without blocking and
    /// renders one frame per pass: the swap chains' frame latency wait inside Present paces the loop.
    /// </summary>
    private void RunMessageLoop()
    {
        while (true)
        {
            if (!_animating)
            {
                if (PInvoke.GetMessage(out MSG message, HWND.Null, 0, 0).Value <= 0)
                {
                    return;
                }

                PInvoke.TranslateMessage(message);
                PInvoke.DispatchMessage(message);
                continue;
            }

            while (PInvoke.PeekMessage(out MSG message, HWND.Null, 0, 0, PEEK_MESSAGE_REMOVE_TYPE.PM_REMOVE))
            {
                if (message.message == PInvoke.WM_QUIT)
                {
                    return;
                }

                PInvoke.TranslateMessage(message);
                PInvoke.DispatchMessage(message);
            }

            if (_animating)
            {
                RenderAnimationFrame();
            }
        }
    }

    /// <summary>Starts or stops the render loop depending on whether any effect is active.</summary>
    private void UpdateAnimation()
    {
        bool shouldAnimate = _overlays.Count > 0 && _engine.IsActive;
        if (shouldAnimate && !_animating)
        {
            _animating = true;
            _engine.ResumeAfterIdle();
            _lastFrameTimestamp = Stopwatch.GetTimestamp();
            _loopFramesSinceStart = 0;
        }
        else if (!shouldAnimate && _animating)
        {
            _animating = false;
            Volatile.Write(ref _rainHitsPerSecond, 0f);
            _frameStatistics.Reset();

            // One last frame without sprites, so the last drops do not stay frozen on screen.
            _engine.Advance(0);
            PresentAll();
        }
    }

    private void RenderAnimationFrame()
    {
        long start = Stopwatch.GetTimestamp();
        double elapsed = (start - _lastFrameTimestamp) / (double)Stopwatch.Frequency;
        if (elapsed < MinimumFrameSeconds)
        {
            Thread.Sleep(1);
            return;
        }

        _lastFrameTimestamp = start;
        bool measure = _loopFramesSinceStart >= AllocationWarmUpFrames;
        long allocatedBefore = measure ? GC.GetAllocatedBytesForCurrentThread() : 0;

        // The cursor once per frame, for the leaves' gust: polling needs no hook (PLAN.md, section 4.3).
        if (PInvoke.GetCursorPos(out System.Drawing.Point cursor))
        {
            _cursor.Update(new System.Numerics.Vector2(cursor.X, cursor.Y), start / (double)Stopwatch.Frequency);
            _engine.SetCursor(_cursor.Position, _cursor.Velocity);
        }

        _engine.Advance(elapsed);
        Volatile.Write(ref _rainHitsPerSecond, _rain.HitsPerSecond);
        long simulated = Stopwatch.GetTimestamp();
        PresentAll();

        if (measure)
        {
            _loopAllocatedBytes += GC.GetAllocatedBytesForCurrentThread() - allocatedBefore;
            _measuredLoopFrames++;
        }

        _loopFramesSinceStart++;
        _animationFrames++;
        _frameStatistics.Add(elapsed, (simulated - start) / (double)Stopwatch.Frequency);

        if (!_engine.IsActive)
        {
            UpdateAnimation();
        }
    }

    private void Shutdown()
    {
        try
        {
            foreach (OverlayWindow overlay in _overlays.Values)
            {
                overlay.Dispose();
            }

            _overlays.Clear();
            _renderer.Dispose();

            foreach (HWINEVENTHOOK hook in _hooks)
            {
                PInvoke.UnhookWinEvent(hook);
            }

            _hooks.Clear();

            if (_controlWindow != 0)
            {
                PInvoke.DestroyWindow((HWND)_controlWindow);
                _controlWindow = 0;
            }

            UnregisterWindowClass();
        }
#pragma warning disable CA1031 // Nothing can handle a failure while the thread ends. It must not take the process down.
        catch (Exception exception)
#pragma warning restore CA1031
        {
            _lastError = exception.Message;
        }
        finally
        {
            CancelPendingCommands();
        }
    }

    private void CancelPendingCommands()
    {
        while (_commands.TryDequeue(out Command? command))
        {
            command.Cancel();
        }
    }

    private LRESULT WindowProcedure(HWND window, uint message, WPARAM wParam, LPARAM lParam)
    {
        try
        {
            bool isControlWindow = window == (HWND)_controlWindow;

            switch (message)
            {
                // Belt and braces next to WS_EX_TRANSPARENT: should a hit test ever reach an overlay, it passes through.
                case PInvoke.WM_NCHITTEST when !isControlWindow:
                    return new LRESULT(PInvoke.HTTRANSPARENT);

                case PInvoke.WM_MOUSEACTIVATE:
                    return new LRESULT((nint)PInvoke.MA_NOACTIVATE);

                case PInvoke.WM_DISPLAYCHANGE:
                    ScheduleRefresh();
                    break;

                // Windows suggests a new window rectangle here. Overlays ignore it and are rebuilt for the new DPI instead.
                case PInvoke.WM_DPICHANGED:
                    ScheduleRefresh();
                    return new LRESULT(0);

                // The work area changes when the taskbar moves, resizes or toggles auto-hide.
                case PInvoke.WM_SETTINGCHANGE when (uint)wParam.Value == (uint)SYSTEM_PARAMETERS_INFO_ACTION.SPI_SETWORKAREA:
                    ScheduleRefresh();
                    break;

                case PInvoke.WM_TIMER when isControlWindow && wParam.Value == RefreshTimerId:
                    PInvoke.KillTimer(window, RefreshTimerId);
                    RefreshOverlays();
                    LayoutChanged?.Invoke(this, EventArgs.Empty);
                    return new LRESULT(0);

                case PInvoke.WM_TIMER when isControlWindow && wParam.Value == TrackingTimerId:
                    PInvoke.KillTimer(window, TrackingTimerId);
                    _trackingPending = false;
                    UpdateWindows(force: false);
                    return new LRESULT(0);

                case PInvoke.WM_TIMER when isControlWindow && wParam.Value == PollTimerId:
                    UpdateWindows(force: false);
                    return new LRESULT(0);

                case CommandMessage when isControlWindow:
                    RunCommands();
                    return new LRESULT(0);

                case StopMessage when isControlWindow:
                    PInvoke.PostQuitMessage(0);
                    return new LRESULT(0);
            }
        }
#pragma warning disable CA1031 // An exception must not escape into the native message dispatcher, that would end the process.
        catch (Exception exception)
#pragma warning restore CA1031
        {
            _lastError = exception.Message;
        }

        return PInvoke.DefWindowProc(window, message, wParam, lParam);
    }

    /// <summary>
    /// Subscribes to the window events that change the geometry the effects see. Out-of-context hooks:
    /// Windows posts the events to this thread through its message loop, nothing is injected into other
    /// processes. Narrow event ranges keep unrelated events (name, value, selection changes) away.
    /// </summary>
    private void HookWindowEvents()
    {
        (uint First, uint Last)[] ranges =
        [
            (PInvoke.EVENT_SYSTEM_FOREGROUND, PInvoke.EVENT_SYSTEM_FOREGROUND),
            (PInvoke.EVENT_SYSTEM_MOVESIZESTART, PInvoke.EVENT_SYSTEM_MOVESIZEEND),
            (PInvoke.EVENT_SYSTEM_MINIMIZESTART, PInvoke.EVENT_SYSTEM_MINIMIZEEND),
            (PInvoke.EVENT_OBJECT_DESTROY, PInvoke.EVENT_OBJECT_REORDER),
            (PInvoke.EVENT_OBJECT_LOCATIONCHANGE, PInvoke.EVENT_OBJECT_LOCATIONCHANGE),
            (PInvoke.EVENT_OBJECT_CLOAKED, PInvoke.EVENT_OBJECT_UNCLOAKED),
        ];

        foreach ((uint first, uint last) in ranges)
        {
            HWINEVENTHOOK hook = PInvoke.SetWinEventHook(first, last, HMODULE.Null, _winEventCallback, 0, 0, PInvoke.WINEVENT_OUTOFCONTEXT);
            if (hook.IsNull)
            {
                throw new Win32ErrorException("SetWinEventHook");
            }

            _hooks.Add(hook);
        }
    }

    private void OnWinEvent(HWINEVENTHOOK hook, uint winEvent, HWND window, int objectId, int childId, uint eventThread, uint eventTime)
    {
        try
        {
            switch (winEvent)
            {
                // Another window came to the front: the overlays go back to the top of the topmost band.
                case PInvoke.EVENT_SYSTEM_FOREGROUND:
                    foreach (OverlayWindow overlay in _overlays.Values)
                    {
                        overlay.BringToTop();
                    }

                    ScheduleWindowUpdate();
                    break;

                case PInvoke.EVENT_SYSTEM_MOVESIZESTART:
                case PInvoke.EVENT_SYSTEM_MOVESIZEEND:
                case PInvoke.EVENT_SYSTEM_MINIMIZESTART:
                case PInvoke.EVENT_SYSTEM_MINIMIZEEND:
                    ScheduleWindowUpdate();
                    break;

                // Object events also fire for carets, cursors and child controls. Only whole windows matter.
                default:
                    if (objectId == (int)OBJECT_IDENTIFIER.OBJID_WINDOW && childId == (int)PInvoke.CHILDID_SELF)
                    {
                        ScheduleWindowUpdate();
                    }

                    break;
            }
        }
#pragma warning disable CA1031 // An exception must not escape into the native callback.
        catch (Exception exception)
#pragma warning restore CA1031
        {
            _lastError = exception.Message;
        }
    }

    /// <summary>Coalesces bursts of window events (a drag raises hundreds) into one update per frame.</summary>
    private void ScheduleWindowUpdate()
    {
        if (!_trackingPending && _controlWindow != 0 && _overlays.Count > 0)
        {
            _trackingPending = true;
            PInvoke.SetTimer((HWND)_controlWindow, TrackingTimerId, TrackingDelayMilliseconds, null);
        }
    }

    /// <summary>
    /// Takes a new window snapshot and hands it to the engine, which derives window events and
    /// recomputes the surfaces when it changed. If nothing animates but the debug layer is on, the new
    /// surfaces are presented right away; a running loop picks them up with its next frame.
    /// </summary>
    private void UpdateWindows(bool force)
    {
        if (_overlays.Count == 0)
        {
            return;
        }

        _enumerator.Capture(_capture, Stopwatch.GetTimestamp() / (double)Stopwatch.Frequency);
        bool changed = _engine.SetWindows(_capture);

        if (changed && !force && !_animating && _options.ShowSurfaces)
        {
            PresentAll();
        }
    }

    /// <summary>The poll runs only while overlays exist, so switched-off overlays cost nothing.</summary>
    private void UpdatePolling()
    {
        if (_controlWindow == 0)
        {
            return;
        }

        if (_overlays.Count > 0)
        {
            PInvoke.SetTimer((HWND)_controlWindow, PollTimerId, PollIntervalMilliseconds, null);
        }
        else
        {
            PInvoke.KillTimer((HWND)_controlWindow, PollTimerId);
            PInvoke.KillTimer((HWND)_controlWindow, TrackingTimerId);
            _trackingPending = false;
        }
    }

    private void ScheduleRefresh()
    {
        if (_controlWindow != 0)
        {
            // Setting a timer that is already running restarts it, which is exactly the coalescing wanted here.
            PInvoke.SetTimer((HWND)_controlWindow, RefreshTimerId, RefreshDelayMilliseconds, null);
        }
    }

    private void RunCommands()
    {
        while (_commands.TryDequeue(out Command? command))
        {
            command.Run();
        }
    }

    /// <summary>Brings windows, surfaces and content in line with the monitors and the options.</summary>
    private void RefreshOverlays()
    {
        _lastError = null;
        _monitors = MonitorService.GetMonitors();
        _engine.SetMonitors(_monitors);
        _testDrops.Intensity = Math.Clamp(_options.TestDrops, 0f, 1f);
        _rain.Settings = _options.Rain;
        _rain.Depth = Math.Clamp(_options.RainDepth, 0f, 1f);
        _snow.Settings = _options.Snow;
        _snow.RainIntensity = _options.Rain.ClampedIntensity;
        _leaves.Settings = _options.Leaves;
        _fog.Settings = _options.Fog;
        _sun.Settings = _options.Sun;

        IReadOnlyList<MonitorInfo> target = _options.Enabled ? _monitors : [];
        OverlayLayoutPlan plan = OverlayLayoutPlanner.Plan([.. _overlays.Values.Select(overlay => overlay.Monitor)], target);

        foreach (string deviceName in plan.ToRemove)
        {
            if (_overlays.Remove(deviceName, out OverlayWindow? overlay))
            {
                overlay.Dispose();
            }
        }

        foreach (MonitorInfo monitor in plan.ToKeep)
        {
            _overlays[monitor.DeviceName].Monitor = monitor;
        }

        foreach (MonitorInfo monitor in plan.ToCreate)
        {
            CreateOverlay(monitor);
        }

        UpdatePolling();
        if (_overlays.Count == 0)
        {
            UpdateAnimation();
            return;
        }

        // Monitors or options changed: surfaces are recomputed for the new layout before presenting.
        UpdateWindows(force: true);
        _engine.Advance(0);
        PresentAll();

        foreach (OverlayWindow overlay in _overlays.Values)
        {
            Guard(() => overlay.SetExcludedFromCapture(_options.ExcludeFromCapture));

            // Shown only after the first frame is presented, so a new overlay never flashes stale content.
            overlay.Show();
        }

        UpdateAnimation();
    }

    private void CreateOverlay(MonitorInfo monitor)
    {
        Guard(() =>
        {
            var overlay = OverlayWindow.Create(_windowClass, _module, monitor);
            try
            {
                overlay.CreateSurface(_renderer);
            }
            catch
            {
                overlay.Dispose();
                throw;
            }

            _overlays.Add(monitor.DeviceName, overlay);
        });
    }

    /// <summary>
    /// Presents the current content on every overlay. When the graphics device is lost (driver update,
    /// GPU switch, remote session), the device and all surfaces are rebuilt and the frame is presented again.
    /// </summary>
    private void PresentAll()
    {
        SurfaceSet? surfaces = _options.ShowSurfaces ? _engine.Context.Surfaces : null;
        Core.Rendering.RenderFrame? scene = _engine.Frame.SpriteCount > 0 ? _engine.Frame : null;

        for (int attempt = 0; attempt < DeviceRecoveryAttempts; attempt++)
        {
            // A plain loop instead of LINQ: this runs every frame and must not allocate.
            bool allPresented = true;
            foreach (OverlayWindow overlay in _overlays.Values)
            {
                allPresented &= overlay.Present(_options.Content, surfaces, scene) == PresentResult.Presented;
            }

            if (allPresented)
            {
                return;
            }

            Guard(RebuildSurfaces);
        }

        _lastError = "The graphics device was lost and could not be restored. Overlays stay blank until the display setup or the settings change.";
    }

    private void RebuildSurfaces()
    {
        foreach (OverlayWindow overlay in _overlays.Values)
        {
            overlay.DisposeSurface();
        }

        _renderer.RecreateDevice();

        foreach (OverlayWindow overlay in _overlays.Values)
        {
            overlay.CreateSurface(_renderer);
        }
    }

    /// <summary>One failing monitor must not keep the others from getting their overlay. The error shows up in the status.</summary>
    private void Guard(Action action)
    {
        try
        {
            action();
        }
#pragma warning disable CA1031 // Reported through OverlayStatus.LastError.
        catch (Exception exception)
#pragma warning restore CA1031
        {
            _lastError = exception.Message;
        }
    }

    private OverlayStatus CaptureStatus()
    {
        HWND foreground = PInvoke.GetForegroundWindow();

        var overlays = new List<OverlayWindowStatus>(_overlays.Count);
        foreach (MonitorInfo monitor in _monitors)
        {
            if (_overlays.TryGetValue(monitor.DeviceName, out OverlayWindow? overlay))
            {
                overlays.Add(overlay.Inspect());
            }
        }

        return new OverlayStatus(
            _monitors,
            overlays,
            _overlays.Count > 0 ? _renderer.AdapterName : null,
            _overlays.Values.Any(overlay => overlay.Handle == foreground),
            _lastError)
        {
            Windows = [.. _engine.Context.Windows.Windows],
            Surfaces = [.. _engine.Context.Surfaces.Segments],
            WindowUpdates = _engine.WindowUpdates,
            WindowEvents = new Dictionary<WindowEventKind, long>(_engine.WindowEventCounts),
            IsAnimating = _animating,
            AnimationFrames = _animationFrames,
            FrameMilliseconds = _frameStatistics.AverageFrameSeconds * 1000,
            SimulationMilliseconds = _frameStatistics.AverageSimulationSeconds * 1000,
            Sprites = _engine.Frame.SpriteCount,
            MeasuredLoopFrames = _measuredLoopFrames,
            LoopAllocatedBytes = _loopAllocatedBytes,
        };
    }

    private static unsafe HINSTANCE GetModule()
    {
        HMODULE module = PInvoke.GetModuleHandle((PCWSTR)null);
        return module.IsNull ? throw new Win32ErrorException("GetModuleHandle") : (HINSTANCE)module.Value;
    }

    private unsafe void RegisterWindowClass()
    {
        fixed (char* className = _windowClass)
        {
            var windowClass = new WNDCLASSEXW
            {
                cbSize = (uint)Marshal.SizeOf<WNDCLASSEXW>(),
                lpfnWndProc = _windowProcedure,
                hInstance = _module,
                lpszClassName = className,
            };

            if (PInvoke.RegisterClassEx(windowClass) == 0)
            {
                throw new Win32ErrorException("RegisterClassEx");
            }
        }
    }

    private unsafe void UnregisterWindowClass()
    {
        fixed (char* className = _windowClass)
        {
            PInvoke.UnregisterClass(className, _module);
        }
    }

    /// <summary>
    /// A hidden top-level window that receives the commands of other threads and the system
    /// broadcasts about display changes. It exists even while no overlay does.
    /// </summary>
    private unsafe nint CreateControlWindow()
    {
        fixed (char* className = _windowClass)
        {
            HWND window = PInvoke.CreateWindowEx(
                WINDOW_EX_STYLE.WS_EX_TOOLWINDOW | WINDOW_EX_STYLE.WS_EX_NOACTIVATE,
                className,
                null,
                WINDOW_STYLE.WS_POPUP,
                0,
                0,
                0,
                0,
                HWND.Null,
                HMENU.Null,
                _module,
                null);

            return window.IsNull ? throw new Win32ErrorException("CreateWindowEx (control)") : window;
        }
    }

    private sealed record Command(Action Run, Action Cancel);

    /// <summary>Rolling averages over roughly the last second of frames. Fixed-size ring: no allocation.</summary>
    private sealed class FrameStatistics
    {
        private const int Capacity = 120;
        private readonly double[] _frames = new double[Capacity];
        private readonly double[] _simulation = new double[Capacity];
        private int _count;
        private int _next;

        public double AverageFrameSeconds => Average(_frames);

        public double AverageSimulationSeconds => Average(_simulation);

        public void Add(double frameSeconds, double simulationSeconds)
        {
            _frames[_next] = frameSeconds;
            _simulation[_next] = simulationSeconds;
            _next = (_next + 1) % Capacity;
            _count = Math.Min(_count + 1, Capacity);
        }

        public void Reset()
        {
            _count = 0;
            _next = 0;
        }

        private double Average(double[] values)
        {
            if (_count == 0)
            {
                return 0;
            }

            double sum = 0;
            for (int i = 0; i < _count; i++)
            {
                sum += values[i];
            }

            return sum / _count;
        }
    }
}
