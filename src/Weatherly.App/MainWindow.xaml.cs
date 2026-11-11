using Microsoft.UI.Xaml;
using Weatherly.Core;
using Weatherly.Core.Diagnostics;
using Weatherly.Core.Overlay;
using Weatherly.Platform.Overlay;

namespace Weatherly.App;

/// <summary>
/// Test bench of the overlay spike: switches for the overlay and the screenshot exclusion, the
/// monitor list and the self-checks. It is replaced by the tray flyout and the settings window
/// in later milestones.
/// </summary>
public sealed partial class MainWindow : Window
{
    private readonly OverlayHost _overlay;
    private readonly string? _startError;
    private readonly bool _initialized;
    private OverlayOptions _options;

    public MainWindow(OverlayHost overlay, OverlayOptions options, string? startError)
    {
        ArgumentNullException.ThrowIfNull(overlay);
        ArgumentNullException.ThrowIfNull(options);

        _overlay = overlay;
        _options = options;
        _startError = startError;

        InitializeComponent();

        Title = AppInfo.Name;
        AppTitleBar.Title = AppInfo.Name;
        ExtendsContentIntoTitleBar = true;
        SetTitleBar(AppTitleBar);
        AppWindow.SetIcon("Assets/AppIcon.ico");

        HeadingText.Text = $"{AppInfo.Name} {AppInfo.DisplayVersion}";
        OverlaySwitch.IsOn = options.Enabled;
        CaptureSwitch.IsOn = options.ExcludeFromCapture;
        OverlaySwitch.IsEnabled = CaptureSwitch.IsEnabled = startError is null;
        _initialized = true;

        _overlay.LayoutChanged += OnOverlayLayoutChanged;
        Closed += (_, _) => _overlay.LayoutChanged -= OnOverlayLayoutChanged;

        Refresh();
    }

    /// <summary>Results of the most recent <see cref="Refresh"/>.</summary>
    public IReadOnlyList<DiagnosticResult> Diagnostics { get; private set; } = [];

    /// <summary>Monitor descriptions of the most recent <see cref="Refresh"/>.</summary>
    public IReadOnlyList<string> MonitorLines { get; private set; } = [];

    /// <summary>Measures the overlay again and updates the lists. Returns the measured state, if there is one.</summary>
    public OverlayStatus? Refresh()
    {
        OverlayStatus? status = null;
        string? error = _startError;
        if (error is null)
        {
            try
            {
                status = _overlay.GetStatus();
            }
            catch (ObjectDisposedException)
            {
                error = "The overlay thread has stopped.";
            }
        }

        Diagnostics = StartupDiagnostics.Evaluate(status, _options, error);
        MonitorLines = status is null ? [] : StartupDiagnostics.DescribeMonitors(status);

        DiagnosticsList.ItemsSource = Diagnostics;
        MonitorList.ItemsSource = MonitorLines;
        return status;
    }

    private void OnOptionToggled(object sender, RoutedEventArgs e)
    {
        // The switches also fire while the constructor sets their initial state.
        if (!_initialized)
        {
            return;
        }

        _options = _options with
        {
            Enabled = OverlaySwitch.IsOn,
            ExcludeFromCapture = CaptureSwitch.IsOn,
        };

        try
        {
            _overlay.Apply(_options);
        }
        catch (ObjectDisposedException)
        {
            // Refresh reports the stopped overlay thread.
        }

        Refresh();
    }

    private void OnCheckAgainClicked(object sender, RoutedEventArgs e) => Refresh();

    /// <summary>Called on the overlay thread when monitors were added, removed or changed.</summary>
    private void OnOverlayLayoutChanged(object? sender, EventArgs e) => DispatcherQueue.TryEnqueue(() => Refresh());
}
