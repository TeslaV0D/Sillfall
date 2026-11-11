using System.Collections.Concurrent;
using System.Runtime.ExceptionServices;
using System.Runtime.InteropServices;
using Weatherly.Core.Displays;
using Weatherly.Core.Overlay;
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
/// The host follows the monitor setup. When a monitor is plugged in, removed, moved, resized or
/// rescaled, the affected overlays are rebuilt (see <see cref="OverlayLayoutPlanner"/>).
/// </para>
/// </summary>
public sealed class OverlayHost : IDisposable
{
    private const uint CommandMessage = PInvoke.WM_APP + 1;
    private const uint StopMessage = PInvoke.WM_APP + 2;
    private const nuint RefreshTimerId = 1;

    /// <summary>
    /// Display changes arrive as a burst of messages (one per window and per changed property).
    /// The layout is recomputed once, shortly after the last one.
    /// </summary>
    private const uint RefreshDelayMilliseconds = 250;

    private const int DeviceRecoveryAttempts = 3;

    private const int StateCreated = 0;
    private const int StateStarted = 1;
    private const int StateDisposed = 2;

    private readonly IOverlayRenderer _renderer;
    private readonly Thread _thread;
    private readonly WNDPROC _windowProcedure;
    private readonly WINEVENTPROC _foregroundChanged;
    private readonly string _windowClass = $"Weatherly.Overlay.{Guid.NewGuid():N}";
    private readonly ConcurrentQueue<Command> _commands = new();
    private readonly ManualResetEventSlim _startCompleted = new();
    private readonly Dictionary<string, OverlayWindow> _overlays = new(StringComparer.OrdinalIgnoreCase);

    private int _state = StateCreated;
    private volatile nint _controlWindow;
    private Exception? _startupError;

    // Only touched on the overlay thread.
    private HINSTANCE _module;
    private HWINEVENTHOOK _foregroundHook;
    private OverlayOptions _options;
    private IReadOnlyList<MonitorInfo> _monitors = [];
    private string? _lastError;

    /// <summary>Creates the host. It takes ownership of <paramref name="renderer"/> and disposes it on the overlay thread.</summary>
    public OverlayHost(IOverlayRenderer renderer, OverlayOptions options)
    {
        ArgumentNullException.ThrowIfNull(renderer);
        ArgumentNullException.ThrowIfNull(options);

        _renderer = renderer;
        _options = options;
        _windowProcedure = WindowProcedure;
        _foregroundChanged = OnForegroundChanged;
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
            _foregroundHook = HookForegroundChanges();
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

    private static void RunMessageLoop()
    {
        while (PInvoke.GetMessage(out MSG message, HWND.Null, 0, 0).Value > 0)
        {
            PInvoke.TranslateMessage(message);
            PInvoke.DispatchMessage(message);
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

            if (!_foregroundHook.IsNull)
            {
                PInvoke.UnhookWinEvent(_foregroundHook);
                _foregroundHook = HWINEVENTHOOK.Null;
            }

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
    /// Every time another window comes to the foreground, the overlays go back to the top of the
    /// topmost band. Out-of-context hook: Windows calls back on this thread through the message loop,
    /// nothing is injected into other processes.
    /// </summary>
    private HWINEVENTHOOK HookForegroundChanges()
    {
        HWINEVENTHOOK hook = PInvoke.SetWinEventHook(
            PInvoke.EVENT_SYSTEM_FOREGROUND,
            PInvoke.EVENT_SYSTEM_FOREGROUND,
            HMODULE.Null,
            _foregroundChanged,
            0,
            0,
            PInvoke.WINEVENT_OUTOFCONTEXT);

        return hook.IsNull ? throw new Win32ErrorException("SetWinEventHook") : hook;
    }

    private void OnForegroundChanged(HWINEVENTHOOK hook, uint winEvent, HWND window, int objectId, int childId, uint eventThread, uint eventTime)
    {
        try
        {
            foreach (OverlayWindow overlay in _overlays.Values)
            {
                overlay.BringToTop();
            }
        }
#pragma warning disable CA1031 // An exception must not escape into the native callback.
        catch (Exception exception)
#pragma warning restore CA1031
        {
            _lastError = exception.Message;
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

        if (_overlays.Count == 0)
        {
            return;
        }

        PresentAll();

        foreach (OverlayWindow overlay in _overlays.Values)
        {
            Guard(() => overlay.SetExcludedFromCapture(_options.ExcludeFromCapture));

            // Shown only after the first frame is presented, so a new overlay never flashes stale content.
            overlay.Show();
        }
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
        for (int attempt = 0; attempt < DeviceRecoveryAttempts; attempt++)
        {
            if (_overlays.Values.All(overlay => overlay.Present(_options.Content) == PresentResult.Presented))
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
            _lastError);
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
}
