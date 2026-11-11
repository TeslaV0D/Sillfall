using Microsoft.UI.Dispatching;
using Microsoft.UI.Xaml;

namespace Weatherly.App;

/// <summary>Composition root of the application.</summary>
public partial class App : Application
{
    private readonly SmokeTest? _smokeTest = SmokeTest.FromCommandLine(Environment.GetCommandLineArgs());
    private Window? _window;

    public App()
    {
        InitializeComponent();
        UnhandledException += OnUnhandledException;
    }

    protected override void OnLaunched(LaunchActivatedEventArgs args)
    {
        IReadOnlyList<DiagnosticResult> diagnostics = StartupDiagnostics.Run();

        _window = new MainWindow(diagnostics);
        _window.Activate();

        if (_smokeTest is not null)
        {
            // Low priority: report only after the window has gone through layout and its first render.
            _window.DispatcherQueue.TryEnqueue(
                DispatcherQueuePriority.Low,
                () => _smokeTest.Complete(diagnostics));
        }
    }

    private void OnUnhandledException(object sender, Microsoft.UI.Xaml.UnhandledExceptionEventArgs e)
    {
        _smokeTest?.Fail(e.Exception);
    }
}
