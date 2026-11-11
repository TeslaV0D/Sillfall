using System.Diagnostics;
using System.Globalization;
using Microsoft.UI;
using Microsoft.UI.Dispatching;
using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml;
using Weatherly.Core.Diagnostics;
using Weatherly.Core.Overlay;
using Weatherly.Platform.Overlay;
using Weatherly.Render;
using Windows.Graphics;

namespace Weatherly.App;

/// <summary>Composition root of the application.</summary>
public partial class App : Application
{
    /// <summary>Time for the freshly opened window to settle before the smoke test measures anything.</summary>
    private static readonly TimeSpan s_settleTime = TimeSpan.FromMilliseconds(500);

    /// <summary>Time between the two measurements that prove the overlay does not render while idle.</summary>
    private static readonly TimeSpan s_idleObservation = TimeSpan.FromMilliseconds(750);

    /// <summary>How far the smoke test moves the app window, and how long it gives the tracking to follow.</summary>
    private static readonly PointInt32 s_moveOffset = new(40, 30);
    private static readonly TimeSpan s_moveObservation = TimeSpan.FromMilliseconds(500);

    /// <summary>How long the render loop is observed, and how long it may take to stop once the drops are off.</summary>
    private static readonly TimeSpan s_animationObservation = TimeSpan.FromSeconds(2);
    private static readonly TimeSpan s_idleStopTimeout = TimeSpan.FromSeconds(8);

    private readonly SmokeTest? _smokeTest = SmokeTest.FromCommandLine(Environment.GetCommandLineArgs());
    private MainWindow? _window;

    public App()
    {
        InitializeComponent();
        UnhandledException += OnUnhandledException;
    }

    protected override void OnLaunched(LaunchActivatedEventArgs args)
    {
        // Milestones M1 and M2: calibration pattern and surface debug layer, no weather effects yet.
        var options = new OverlayOptions
        {
            Enabled = true,
            Content = (_smokeTest?.TestPattern ?? true) ? OverlayContent.TestPattern : OverlayContent.None,
            ExcludeFromCapture = _smokeTest?.ExcludeFromCapture ?? false,
            ShowSurfaces = _smokeTest?.ShowSurfaces ?? false,
            TestDrops = (_smokeTest?.TestDrops ?? false) ? 1f : 0f,
        };

        // The window owns the overlay from here on and disposes it when it closes.
        var overlay = new OverlayHost(new CompositionOverlayRenderer(), options);
        string? startError = null;
        try
        {
            overlay.Start();
        }
#pragma warning disable CA1031 // The window reports why the overlay is missing instead of the app dying at startup.
        catch (Exception exception)
#pragma warning restore CA1031
        {
            startError = $"{exception.GetType().Name}: {exception.Message}";
        }

        _window = new MainWindow(overlay, options, startError);
        if (_smokeTest is not null)
        {
            // Centered, so the screenshot check can verify that the overlay is drawn on top of this window.
            CenterOnPrimaryMonitor(_window);
        }

        _window.Activate();

        if (_smokeTest is not null)
        {
            // Low priority: measure only after the window has gone through layout and its first render.
            _window.DispatcherQueue.TryEnqueue(DispatcherQueuePriority.Low, () => _ = RunSmokeTestAsync(_smokeTest, _window));
        }
    }

    private static async Task RunSmokeTestAsync(SmokeTest smokeTest, MainWindow window)
    {
        try
        {
            await Task.Delay(s_settleTime);
            var diagnostics = new List<DiagnosticResult> { new("Window", true, "Activated") };

            if (smokeTest.TestDrops)
            {
                diagnostics.AddRange(await CheckRenderLoopAsync(window));
            }

            OverlayStatus? before = window.Refresh();
            await Task.Delay(s_idleObservation);
            OverlayStatus? after = window.Refresh();

            // Window tracking: move the app window and check that the tracker follows to the exact new position.
            PointInt32 position = window.AppWindow.Position;
            window.AppWindow.Move(new PointInt32(position.X + s_moveOffset.X, position.Y + s_moveOffset.Y));
            await Task.Delay(s_moveObservation);
            OverlayStatus? moved = window.Refresh();

            diagnostics.AddRange(window.Diagnostics);
            if (before is not null && after is not null)
            {
                diagnostics.Add(OverlayDiagnostics.EvaluateIdle(before, after));
            }

            if (after is not null && moved is not null)
            {
                diagnostics.Add(TrackingDiagnostics.EvaluateMoveEvents(after, moved));
            }

            // The script measures the window itself (DWM) and needs the handle: the process owns overlay windows too.
            string handleLine = string.Create(CultureInfo.InvariantCulture, $"App window handle {Win32Interop.GetWindowFromWindowId(window.AppWindow.Id)}");
            if (smokeTest.TestDrops)
            {
                // Drops on again for the screenshot, with time to land on the app window.
                window.SetTestDrops(1f);
                await Task.Delay(TimeSpan.FromSeconds(2.5));
            }

            int exitCode = smokeTest.WriteReport(diagnostics, [.. window.MonitorLines, handleLine]);

            // Stays on screen so the calling script can take its screenshot of the desktop.
            await Task.Delay(smokeTest.Hold);

            // Closing the window removes the overlays before the process ends.
            window.Close();
            Environment.Exit(exitCode);
        }
#pragma warning disable CA1031 // Any failure of the self-check is its result.
        catch (Exception exception)
#pragma warning restore CA1031
        {
            smokeTest.Fail(exception);
        }
    }

    /// <summary>
    /// With the drops running: the loop renders steadily and allocates nothing. Then the drops are
    /// switched off, and the loop has to stop by itself once the last drop has faded.
    /// </summary>
    private static async Task<List<DiagnosticResult>> CheckRenderLoopAsync(MainWindow window)
    {
        var results = new List<DiagnosticResult>();
        OverlayStatus? start = window.Refresh();
        await Task.Delay(s_animationObservation);
        OverlayStatus? running = window.Refresh();
        if (start is null || running is null)
        {
            results.Add(new DiagnosticResult("Animation", false, "Overlay not running"));
            return results;
        }

        results.Add(AnimationDiagnostics.EvaluateAnimation(start, running, s_animationObservation.TotalSeconds));
        results.Add(AnimationDiagnostics.EvaluateLoopAllocations(running));

        window.SetTestDrops(0f);
        var waited = Stopwatch.StartNew();
        OverlayStatus? status = window.Refresh();
        while (status is { IsAnimating: true } && waited.Elapsed < s_idleStopTimeout)
        {
            await Task.Delay(250);
            status = window.Refresh();
        }

        results.Add(status is null
            ? new DiagnosticResult("Idle stop", false, "Overlay not running")
            : AnimationDiagnostics.EvaluateIdleStop(status, waited.Elapsed));
        return results;
    }

    private static void CenterOnPrimaryMonitor(Window window)
    {
        RectInt32 workArea = DisplayArea.Primary.WorkArea;
        SizeInt32 size = window.AppWindow.Size;
        window.AppWindow.Move(new PointInt32(
            workArea.X + ((workArea.Width - size.Width) / 2),
            workArea.Y + ((workArea.Height - size.Height) / 2)));
    }

    private void OnUnhandledException(object sender, Microsoft.UI.Xaml.UnhandledExceptionEventArgs e)
    {
        _smokeTest?.Fail(e.Exception);
    }
}
