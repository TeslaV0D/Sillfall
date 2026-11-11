using Microsoft.UI;
using Microsoft.UI.Dispatching;
using Microsoft.UI.Xaml;
using Weatherly.Audio;
using Weatherly.Core;
using Weatherly.Core.Diagnostics;
using Weatherly.Core.Overlay;
using Weatherly.Platform.Desktop;
using Weatherly.Platform.Overlay;
using Weatherly.Platform.Shell;

namespace Weatherly.App;

/// <summary>
/// Test bench of the milestones so far: switches and sliders for the overlay and the effects, the monitor list and
/// the self-checks. With the tray icon (MVP), closing it only hides it. It is replaced by the tray flyout and the
/// settings window in M9. The window owns the overlay host and disposes it when it really closes.
/// </summary>
public sealed partial class MainWindow : Window
{
    private readonly OverlayHost _overlay;
    private readonly string? _startError;
    private readonly bool _initialized;
    private readonly DispatcherQueueTimer _soundTimer;
    private OverlayOptions _options;
    private RainSoundPlayer? _sound;
    private bool _quitting;
    private TrayIcon? _tray;

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
        DropsSwitch.IsOn = options.TestDrops > 0f;
        RainSlider.Value = Math.Round(options.Rain.Intensity * 100);
        RainDepthSlider.Value = Math.Round(options.RainDepth * 100);
        SnowSlider.Value = Math.Round(options.Snow.Intensity * 100);
        LeavesSlider.Value = Math.Round(options.Leaves.Intensity * 100);
        OverlaySwitch.IsEnabled = PatternSwitch.IsEnabled = CaptureSwitch.IsEnabled = SurfacesSwitch.IsEnabled = DropsSwitch.IsEnabled = startError is null;
        _initialized = true;

        _overlay.LayoutChanged += OnOverlayLayoutChanged;
        Closed += OnClosed;
        AppWindow.Closing += OnClosing;

        // The rain sound follows the rain: intensity from the settings, drop density from the simulation.
        _soundTimer = DispatcherQueue.CreateTimer();
        _soundTimer.Interval = TimeSpan.FromMilliseconds(100);
        _soundTimer.Tick += (_, _) => _sound?.Update(_options.Rain.ClampedIntensity, _overlay.RainHitsPerSecond, volume: 0.6f);

        Refresh();
    }

    /// <summary>Raised on the UI thread whenever a switch or slider changed the options or the sound.</summary>
    public event EventHandler? OptionsChanged;

    /// <summary>The options the overlay runs with.</summary>
    public OverlayOptions Options => _options;

    /// <summary>Whether the weather sound plays.</summary>
    public bool SoundOn => _sound is not null;

    /// <summary>
    /// Closing the window only hides it (the app lives on in the tray). <see cref="Quit"/> really closes it.
    /// </summary>
    public bool HideOnClose { get; set; }

    /// <summary>The tray icon, once attached.</summary>
    public TrayIcon? Tray => _tray;

    /// <summary>Hands the tray icon to the window, which removes it when it really closes.</summary>
    public void AttachTray(TrayIcon tray)
    {
        ArgumentNullException.ThrowIfNull(tray);
        _tray?.Dispose();
        _tray = tray;
    }

    /// <summary>Shows the window (also after it was hidden to the tray) and brings it to the front.</summary>
    public void ShowAndActivate()
    {
        AppWindow.Show();
        Activate();
    }

    /// <summary>Closes the window for good, even with <see cref="HideOnClose"/>.</summary>
    public void Quit()
    {
        _quitting = true;
        Close();
    }

    /// <summary>Switches the sound on or off, like the switch does.</summary>
    public void SetSound(bool on) => SoundSwitch.IsOn = on;

    /// <summary>Pauses or resumes all effects, like the overlay switch does.</summary>
    public void SetPaused(bool paused) => OverlaySwitch.IsOn = !paused;

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
            TestDrops = DropsSwitch.IsOn ? 1f : 0f,
            Rain = _options.Rain with { Intensity = (float)(RainSlider.Value / 100) },
            RainDepth = (float)(RainDepthSlider.Value / 100),
            Snow = _options.Snow with { Intensity = (float)(SnowSlider.Value / 100) },
            Leaves = _options.Leaves with { Intensity = (float)(LeavesSlider.Value / 100) },
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
        OptionsChanged?.Invoke(this, EventArgs.Empty);
    }

    private void OnCheckAgainClicked(object sender, RoutedEventArgs e) => Refresh();

    private void OnSliderChanged(object sender, Microsoft.UI.Xaml.Controls.Primitives.RangeBaseValueChangedEventArgs e) =>
        OnOptionToggled(sender, e);

    /// <summary>Sets the rain intensity (0 to 1), like the slider does.</summary>
    public void SetRain(float intensity)
    {
        // Setting the slider raises ValueChanged, which applies the new options.
        RainSlider.Value = Math.Round(intensity * 100);
    }

    /// <summary>Sets the snow intensity (0 to 1), like the slider does.</summary>
    public void SetSnow(float intensity)
    {
        SnowSlider.Value = Math.Round(intensity * 100);
    }

    /// <summary>Sets the leaf intensity (0 to 1), like the slider does.</summary>
    public void SetLeaves(float intensity)
    {
        LeavesSlider.Value = Math.Round(intensity * 100);
    }

    /// <summary>Switches the calibration drops on or off, like the switch does.</summary>
    public void SetTestDrops(float intensity)
    {
        // Setting the switch raises Toggled, which applies the new options.
        DropsSwitch.IsOn = intensity > 0f;
    }

    private void OnSoundToggled(object sender, RoutedEventArgs e)
    {
        if (SoundSwitch.IsOn && _sound is null)
        {
            _sound = RainSoundPlayer.TryCreate(out string? error);
            SoundStatus.Text = error ?? "Procedural rain: pink noise and drop ticks that follow the splashes.";
            if (_sound is not null)
            {
                _soundTimer.Start();
            }
        }
        else if (!SoundSwitch.IsOn && _sound is not null)
        {
            _soundTimer.Stop();
            _sound.Dispose();
            _sound = null;
            SoundStatus.Text = string.Empty;
        }

        OptionsChanged?.Invoke(this, EventArgs.Empty);
    }

    private void OnClosing(Microsoft.UI.Windowing.AppWindow sender, Microsoft.UI.Windowing.AppWindowClosingEventArgs args)
    {
        if (HideOnClose && !_quitting)
        {
            args.Cancel = true;
            sender.Hide();
        }
    }

    private void OnClosed(object sender, WindowEventArgs args)
    {
        _soundTimer.Stop();
        _sound?.Dispose();
        _sound = null;
        _overlay.LayoutChanged -= OnOverlayLayoutChanged;
        _overlay.Dispose();
        _tray?.Dispose();
        _tray = null;
    }

    /// <summary>Called on the overlay thread when monitors were added, removed or changed.</summary>
    private void OnOverlayLayoutChanged(object? sender, EventArgs e) => DispatcherQueue.TryEnqueue(() => Refresh());
}
