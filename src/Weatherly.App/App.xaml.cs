using System.Diagnostics;
using System.Globalization;
using Microsoft.UI;
using Microsoft.UI.Dispatching;
using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml;
using Weatherly.Audio;
using Weatherly.Core.Diagnostics;
using Weatherly.Core.Effects;
using Weatherly.Core.Overlay;
using Weatherly.Core.Settings;
using Weatherly.Core.Tray;
using Weatherly.Platform.Overlay;
using Weatherly.Platform.Shell;
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

    /// <summary>Rain needs longer to stop: the drips run off and the edges dry first.</summary>
    private static readonly TimeSpan s_rainIdleStopTimeout = TimeSpan.FromSeconds(45);

    /// <summary>Snow needs longer still: the slowest flakes take half a minute to fall, then the piles melt.</summary>
    private static readonly TimeSpan s_snowIdleStopTimeout = TimeSpan.FromSeconds(75);

    private const float SmokeRainIntensity = 0.8f;

    private const float SmokeSnowIntensity = 1f;

    private const float SmokeLeavesIntensity = 1f;

    /// <summary>Fog and sun fade out in a few seconds; there is nothing that has to run off first.</summary>
    private static readonly TimeSpan s_atmosphereIdleStopTimeout = TimeSpan.FromSeconds(20);

    /// <summary>Leaves: the last ones need about ten seconds to fall, then the lying ones fade.</summary>
    private static readonly TimeSpan s_leavesIdleStopTimeout = TimeSpan.FromSeconds(60);

    /// <summary>Settings are written this long after the last change, so dragging a slider does not write a file per step.</summary>
    private static readonly TimeSpan s_saveDelay = TimeSpan.FromMilliseconds(500);

    private readonly SmokeTest? _smokeTest = SmokeTest.FromCommandLine(Environment.GetCommandLineArgs());
    private MainWindow? _window;
    private SettingsStore? _settings;
    private DispatcherQueueTimer? _saveTimer;

    public App()
    {
        InitializeComponent();
        UnhandledException += OnUnhandledException;
    }

    protected override void OnLaunched(LaunchActivatedEventArgs args)
    {
        // The smoke test starts from a fixed state and never touches the user's settings.
        var options = new OverlayOptions
        {
            Enabled = true,
            Content = (_smokeTest?.TestPattern ?? false) ? OverlayContent.TestPattern : OverlayContent.None,
            ExcludeFromCapture = _smokeTest?.ExcludeFromCapture ?? false,
            ShowSurfaces = _smokeTest?.ShowSurfaces ?? false,
            TestDrops = (_smokeTest?.TestDrops ?? false) ? 1f : 0f,
            Rain = new EffectSettings { Intensity = (_smokeTest?.Rain ?? false) ? SmokeRainIntensity : 0f },
            Snow = new EffectSettings { Intensity = (_smokeTest?.Snow ?? false) ? SmokeSnowIntensity : 0f },
            Leaves = new EffectSettings { Intensity = (_smokeTest?.Leaves ?? false) ? SmokeLeavesIntensity : 0f },
            Fog = new EffectSettings { Intensity = (_smokeTest?.Fog ?? false) ? 1f : 0f },
            Sun = new EffectSettings { Intensity = (_smokeTest?.Sun ?? false) ? 1f : 0f },
            Grass = new EffectSettings { Intensity = (_smokeTest?.Grass ?? false) ? 1f : 0f },
        };

        UserSettings settings = new();
        if (_smokeTest is null)
        {
            _settings = new SettingsStore(SettingsStore.DefaultPath);
            settings = _settings.Load();
            options = settings.ApplyTo(options);
        }

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

        bool trayShown = ShowTrayIcon(_window);

        // With a tray icon, closing the window keeps the weather running; the tray's Quit ends the app.
        _window.HideOnClose = trayShown && _smokeTest is null;
        if (_settings is not null)
        {
            if (settings.Sound)
            {
                _window.SetSound(true);
            }

            _saveTimer = _window.DispatcherQueue.CreateTimer();
            _saveTimer.Interval = s_saveDelay;
            _saveTimer.IsRepeating = false;
            _saveTimer.Tick += (_, _) => SaveSettings();
        }

        _window.OptionsChanged += OnOptionsChanged;
        _window.Activate();

        if (_smokeTest is not null)
        {
            // Low priority: measure only after the window has gone through layout and its first render.
            _window.DispatcherQueue.TryEnqueue(DispatcherQueuePriority.Low, () => _ = RunSmokeTestAsync(_smokeTest, _window, trayShown));
        }
    }

    /// <summary>Adds the tray icon. Returns whether the shell shows it; without it the app behaves like a normal window.</summary>
    private bool ShowTrayIcon(MainWindow window)
    {
        TrayIcon? tray = null;
        try
        {
            tray = new TrayIcon(
                Path.Combine(AppContext.BaseDirectory, "Assets", "AppIcon.ico"),
                TrayMenu.Tooltip(window.Options),
                () => TrayMenu.Build(window.Options, window.SoundOn, new TrayCommands(
                    Open: window.ShowAndActivate,
                    SetRain: window.SetRain,
                    SetSnow: window.SetSnow,
                    SetLeaves: window.SetLeaves,
                    SetFog: window.SetFog,
                    SetSun: window.SetSun,
                    SetGrass: window.SetGrass,
                    SetSound: window.SetSound,
                    SetPaused: window.SetPaused,
                    Quit: Quit)),
                window.ShowAndActivate);
            if (!tray.Show())
            {
                tray.Dispose();
                return false;
            }

            // The window owns the icon from here on and removes it when it really closes.
            window.AttachTray(tray);
            return true;
        }
#pragma warning disable CA1031 // No tray icon is no reason not to start: the window stays the way to control the app.
        catch (Exception)
#pragma warning restore CA1031
        {
            tray?.Dispose();
            return false;
        }
    }

    private void OnOptionsChanged(object? sender, EventArgs e)
    {
        if (_window is null)
        {
            return;
        }

        _window.Tray?.SetTooltip(TrayMenu.Tooltip(_window.Options));
        if (_saveTimer is not null)
        {
            _saveTimer.Stop();
            _saveTimer.Start();
        }
    }

    private void SaveSettings()
    {
        if (_settings is null || _window is null)
        {
            return;
        }

        try
        {
            _settings.Save(UserSettings.From(_window.Options, _window.SoundOn));
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            // Not being able to remember a setting is no reason to interrupt the user. It is tried again on the next change.
        }
    }

    private void Quit()
    {
        _saveTimer?.Stop();
        SaveSettings();
        _window?.Quit();
        Exit();
    }

    private static async Task RunSmokeTestAsync(SmokeTest smokeTest, MainWindow window, bool trayShown)
    {
        try
        {
            await Task.Delay(s_settleTime);
            var diagnostics = new List<DiagnosticResult>
            {
                new("Window", true, "Activated"),
                new("Tray icon", trayShown, trayShown ? "Shown in the notification area" : "The shell did not accept the icon"),
            };
            var monitorNotes = new List<string>();

            if (smokeTest.TestDrops)
            {
                diagnostics.AddRange(await CheckRenderLoopAsync(window, window.SetTestDrops, s_idleStopTimeout));
            }

            if (smokeTest.Rain)
            {
                diagnostics.AddRange(await CheckRenderLoopAsync(window, window.SetRain, s_rainIdleStopTimeout));
            }

            if (smokeTest.Snow)
            {
                diagnostics.AddRange(await CheckRenderLoopAsync(window, window.SetSnow, s_snowIdleStopTimeout));
            }

            if (smokeTest.Leaves)
            {
                diagnostics.AddRange(await CheckRenderLoopAsync(window, window.SetLeaves, s_leavesIdleStopTimeout));
            }

            if (smokeTest.Fog)
            {
                diagnostics.AddRange(await CheckRenderLoopAsync(window, window.SetFog, s_atmosphereIdleStopTimeout));
            }

            if (smokeTest.Sun)
            {
                diagnostics.AddRange(await CheckRenderLoopAsync(window, window.SetSun, s_atmosphereIdleStopTimeout));
            }

            if (smokeTest.Grass)
            {
                // Grass withers in 3 s once it is off.
                diagnostics.AddRange(await CheckRenderLoopAsync(window, window.SetGrass, s_atmosphereIdleStopTimeout));
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

            if (smokeTest.Rain)
            {
                // Audio is information only: CI runners usually have no sound device.
                using RainSoundPlayer? player = RainSoundPlayer.TryCreate(out string? audioError);
                monitorNotes.Add(audioError ?? "Audio output available for the rain sound");
            }

            // The script measures the window itself (DWM) and needs the handle: the process owns overlay windows too.
            string handleLine = string.Create(CultureInfo.InvariantCulture, $"App window handle {Win32Interop.GetWindowFromWindowId(window.AppWindow.Id)}");
            if (smokeTest.TestDrops)
            {
                // Drops on again for the screenshot, with time to land on the app window. The window goes to
                // the top of the screen first: any window with a higher top edge would catch the drops before.
                RectInt32 workArea = DisplayArea.Primary.WorkArea;
                window.AppWindow.Move(new PointInt32(window.AppWindow.Position.X, workArea.Y + 12));
                window.SetTestDrops(1f);
                await Task.Delay(TimeSpan.FromSeconds(2.5));
            }

            if (smokeTest.Rain || smokeTest.Snow || smokeTest.Leaves || smokeTest.Fog || smokeTest.Sun || smokeTest.Grass)
            {
                // Window to the top, weather still off: the script takes a clear screenshot first.
                RectInt32 workArea = DisplayArea.Primary.WorkArea;
                window.AppWindow.Move(new PointInt32(window.AppWindow.Position.X, workArea.Y + 12));
                await Task.Delay(TimeSpan.FromMilliseconds(300));
            }

            int exitCode = smokeTest.WriteReport(diagnostics, [.. window.MonitorLines, .. monitorNotes, handleLine]);

            if (smokeTest.Rain)
            {
                // Then rain, so the wet screenshot shows streaks, splashes and the water film on the edge.
                await Task.Delay(TimeSpan.FromMilliseconds(1500));
                window.SetRain(SmokeRainIntensity);
            }

            if (smokeTest.Snow)
            {
                // Then snow, so the second screenshot shows flakes and the first snow on the edge.
                await Task.Delay(TimeSpan.FromMilliseconds(1500));
                window.SetSnow(SmokeSnowIntensity);
            }

            if (smokeTest.Leaves)
            {
                // Then leaves: they fall slowly, the screenshot catches them on the way and on the edge.
                await Task.Delay(TimeSpan.FromMilliseconds(1500));
                window.SetLeaves(SmokeLeavesIntensity);
            }

            if (smokeTest.Fog || smokeTest.Sun)
            {
                // Then fog or sun, faded in fully before the second screenshot.
                await Task.Delay(TimeSpan.FromMilliseconds(500));
                if (smokeTest.Fog)
                {
                    window.SetFog(1f);
                }

                if (smokeTest.Sun)
                {
                    window.SetSun(1f);
                }
            }

            if (smokeTest.Grass)
            {
                // Then grass: it sprouts on the edge right away and grows a few pixels before the screenshot.
                await Task.Delay(TimeSpan.FromMilliseconds(500));
                window.SetGrass(1f);
            }

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
    /// With an effect running: the loop renders steadily and allocates nothing. Then the effect is
    /// switched off, and the loop has to stop by itself once the last particle has faded.
    /// </summary>
    private static async Task<List<DiagnosticResult>> CheckRenderLoopAsync(MainWindow window, Action<float> setEffect, TimeSpan idleStopTimeout)
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

        setEffect(0f);
        var waited = Stopwatch.StartNew();
        OverlayStatus? status = window.Refresh();
        while (status is { IsAnimating: true } && waited.Elapsed < idleStopTimeout)
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
