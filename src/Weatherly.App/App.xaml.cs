using Microsoft.UI.Dispatching;
using Microsoft.UI.Xaml;
using Weatherly.Core.Diagnostics;
using Weatherly.Core.Overlay;
using Weatherly.Platform.Overlay;
using Weatherly.Render;

namespace Weatherly.App;

/// <summary>Composition root of the application.</summary>
public partial class App : Application
{
    /// <summary>Time between the two measurements that prove the overlay does not render while idle.</summary>
    private static readonly TimeSpan s_idleObservation = TimeSpan.FromMilliseconds(750);

    private readonly SmokeTest? _smokeTest = SmokeTest.FromCommandLine(Environment.GetCommandLineArgs());
    private MainWindow? _window;

    public App()
    {
        InitializeComponent();
        UnhandledException += OnUnhandledException;
    }

    protected override void OnLaunched(LaunchActivatedEventArgs args)
    {
        // Milestone M1: the overlay shows the calibration pattern, there are no weather effects yet.
        var options = new OverlayOptions
        {
            Enabled = true,
            Content = OverlayContent.TestPattern,
            ExcludeFromCapture = _smokeTest?.ExcludeFromCapture ?? false,
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
            OverlayStatus? before = window.Refresh();
            await Task.Delay(s_idleObservation);
            OverlayStatus? after = window.Refresh();

            var diagnostics = new List<DiagnosticResult> { new("Window", true, "Activated") };
            diagnostics.AddRange(window.Diagnostics);
            if (before is not null && after is not null)
            {
                diagnostics.Add(OverlayDiagnostics.EvaluateIdle(before, after));
            }

            int exitCode = smokeTest.WriteReport(diagnostics, window.MonitorLines);

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

    private void OnUnhandledException(object sender, Microsoft.UI.Xaml.UnhandledExceptionEventArgs e)
    {
        _smokeTest?.Fail(e.Exception);
    }
}
