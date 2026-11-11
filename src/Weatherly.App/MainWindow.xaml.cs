using Microsoft.UI;
using Microsoft.UI.Xaml;
using Weatherly.Core;
using Weatherly.Core.Diagnostics;
using Weatherly.Core.Overlay;
using Weatherly.Platform.Desktop;
using Weatherly.Platform.Overlay;

namespace Weatherly.App;

/// <summary>
/// Test bench of milestones M1 and M2: switches for the overlay, the screenshot exclusion and the
/// surface debug layer, the monitor list and the self-checks. It is replaced by the tray flyout and the settings window
/// in later milestones. The window owns the overlay host and disposes it when it closes.
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
        SurfacesSwitch.IsOn = options.ShowSurfaces;
        PatternSwitch.IsOn = options.Content == OverlayContent.TestPattern;
        OverlaySwitch.IsEnabled = PatternSwitch.IsEnabled = CaptureSwitch.IsEnabled = SurfacesSwitch.IsEnabled = startError is null;
        _initialized = true;

        _overlay.LayoutChanged += OnOverlayLayoutChanged;
        Closed += OnClosed;

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

        // The app window is the reference for the tracking checks: its true position comes from DWM directly.
        nint handle = Win32Interop.GetWindowFromWindowId(AppWindow.Id);
        Diagnostics = StartupDiagnostics.Evaluate(status, _options, error, handle, WindowGeometry.GetVisibleBounds(handle));
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
            ShowSurfaces = SurfacesSwitch.IsOn,
            Content = PatternSwitch.IsOn ? OverlayContent.TestPattern : OverlayContent.None,
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

    private void OnClosed(object sender, WindowEventArgs args)
    {
        _overlay.LayoutChanged -= OnOverlayLayoutChanged;
        _overlay.Dispose();
    }

    /// <summary>Called on the overlay thread when monitors were added, removed or changed.</summary>
    private void OnOverlayLayoutChanged(object? sender, EventArgs e) => DispatcherQueue.TryEnqueue(() => Refresh());
}
