using System.Diagnostics;
using System.Globalization;
using Microsoft.UI;
using Microsoft.UI.Dispatching;
using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml;
using Weatherly.Audio;
using Weatherly.Core;
using Weatherly.Core.Diagnostics;
using Weatherly.Core.Effects;
using Weatherly.Core.Overlay;
using Weatherly.Core.Scenes;
using Weatherly.Core.Settings;
using Weatherly.Core.Tray;
using Weatherly.Core.Weather.Providers;
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

    private readonly SmokeTest? _smokeTest = SmokeTest.FromCommandLine(Environment.GetCommandLineArgs());
    private MainWindow? _window;
    private AppController? _controller;
    private SettingsWindow? _settingsWindow;
    private FlyoutWindow? _flyout;

    public App()
    {
        InitializeComponent();
        UnhandledException += OnUnhandledException;

        // The app lives in the notification area: closing (hiding) its windows does not end it, only Quit does.
        if (_smokeTest is null)
        {
            DispatcherShutdownMode = DispatcherShutdownMode.OnExplicitShutdown;
        }
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

        // One app per session: a second start hands its scene link (if any) to the first one and ends.
        string? sceneLink = _smokeTest is null ? FindSceneLink() : null;
        SingleInstance? instance = null;
        if (_smokeTest is null)
        {
            instance = SingleInstance.TryClaim();
            if (instance is null)
            {
                SingleInstance.SendToRunning(sceneLink ?? string.Empty);
                Exit();
                return;
            }

            RegisterProtocolIfUnpackaged();
        }

        bool background = Environment.GetCommandLineArgs().Contains(Integration.BackgroundSwitch, StringComparer.OrdinalIgnoreCase);

        AppSettings settings = new();
        SettingsStore? store = null;
        if (_smokeTest is null)
        {
            store = new SettingsStore(SettingsStore.DefaultPath);
            settings = store.Load();
            options = settings.ToOverlayOptions(options);
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

        // The smoke test sets its own state: the controller then neither follows the system, nor saves anything, nor
        // talks to the weather service. Otherwise the diagnostics window owns the provider (it lives as long as the app).
        MetNorwayProvider? weather = _smokeTest is null ? new MetNorwayProvider() : null;
        if (weather is not null)
        {
            _window.Own(weather);
        }

        _controller = new AppController(_window, store, settings, followSystem: _smokeTest is null, weather);
        _settingsWindow = new SettingsWindow(_controller);
        _flyout = new FlyoutWindow(_controller, () => _settingsWindow.ShowPage("effects"));

        bool trayShown = ShowTrayIcon(_window);

        // The diagnostics window and the settings only hide when closed; the tray's Quit ends the app.
        // Without a tray icon, closing the settings window ends the app: otherwise nothing could reach it.
        _window.HideOnClose = _smokeTest is null;
        _settingsWindow.HideOnClose = trayShown;
        _settingsWindow.Closed += (_, _) => Quit();
        if (instance is not null)
        {
            _window.Own(instance);
            instance.MessageReceived += (_, message) => OnInstanceMessage(message);
        }

        if (_smokeTest is not null)
        {
            _window.Activate();
        }
        else
        {
            _controller.Apply();

            // An autostart opens in the notification area; without a tray icon the settings are the only way in.
            if (!background || !trayShown)
            {
                _settingsWindow.ShowPage("effects");
            }
        }

        if (sceneLink is not null)
        {
            ApplySceneLink(sceneLink);
        }

        if (_smokeTest is { SettingsTour: { } tourDirectory })
        {
            _window.DispatcherQueue.TryEnqueue(DispatcherQueuePriority.Low, () => _ = RunSettingsTourAsync(_smokeTest, tourDirectory, _controller, _settingsWindow, _flyout, _window));
        }
        else if (_smokeTest is not null)
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
                () => TrayMenu.Build(Controller.Settings.ToOverlayOptions(window.Options), !Controller.Settings.Sound.Muted, new TrayCommands(
                    Open: () => _settingsWindow?.ShowPage("effects"),
                    SetRain: value => Controller.SetIntensity(EffectKind.Rain, value),
                    SetSnow: value => Controller.SetIntensity(EffectKind.Snow, value),
                    SetLeaves: value => Controller.SetIntensity(EffectKind.Leaves, value),
                    SetFog: value => Controller.SetIntensity(EffectKind.Fog, value),
                    SetSun: value => Controller.SetIntensity(EffectKind.Sun, value),
                    SetGrass: value => Controller.SetIntensity(EffectKind.Grass, value),
                    SetSound: on => Controller.Update(s => s with { Sound = s.Sound with { Muted = !on } }),
                    SetPaused: paused => Controller.Update(s => s with { Enabled = !paused }),
                    Quit: Quit)),
                ToggleFlyout);
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

    private AppController Controller => _controller ?? throw new InvalidOperationException("The app has not started yet.");

    /// <summary>Left click on the tray icon: opens the flyout, or closes it when it is open.</summary>
    private void ToggleFlyout()
    {
        if (_flyout is null)
        {
            return;
        }

        if (_flyout.IsShown)
        {
            _flyout.AppWindow.Hide();
        }
        else
        {
            _flyout.ShowNearTray();
        }
    }

    /// <summary>A scene link from the command line (unpackaged) or from a protocol activation (packaged).</summary>
    private static string? FindSceneLink()
    {
        string? fromCommandLine = Environment.GetCommandLineArgs().FirstOrDefault(arg => arg.StartsWith("weatherly:", StringComparison.OrdinalIgnoreCase));
        if (fromCommandLine is not null)
        {
            return fromCommandLine;
        }

        try
        {
            Microsoft.Windows.AppLifecycle.AppActivationArguments activation = Microsoft.Windows.AppLifecycle.AppInstance.GetCurrent().GetActivatedEventArgs();
            return activation.Kind == Microsoft.Windows.AppLifecycle.ExtendedActivationKind.Protocol
                && activation.Data is Windows.ApplicationModel.Activation.IProtocolActivatedEventArgs protocol
                ? protocol.Uri.OriginalString
                : null;
        }
#pragma warning disable CA1031 // No activation details (unpackaged start): there is no link.
        catch (Exception)
#pragma warning restore CA1031
        {
            return null;
        }
    }

    private static void RegisterProtocolIfUnpackaged()
    {
        try
        {
            if (!Integration.IsPackaged() && Environment.ProcessPath is { } path)
            {
                Integration.RegisterProtocol(AppInfo.Name, path);
            }
        }
        catch (Exception exception) when (exception is UnauthorizedAccessException or IOException or System.Security.SecurityException)
        {
            // Links just do not open the app then; everything else works.
        }
    }

    /// <summary>A later start handed over its command line: show the settings, and apply a scene link if there is one.</summary>
    private void OnInstanceMessage(string message)
    {
        if (message.StartsWith("weatherly:", StringComparison.OrdinalIgnoreCase))
        {
            ApplySceneLink(message);
        }

        _settingsWindow?.ShowPage(message.Length > 0 ? "templates" : "effects");
    }

    /// <summary>Applies a shared scene and keeps it as a template (the link's contents are untrusted and sanitized).</summary>
    private void ApplySceneLink(string link)
    {
        if (_controller is null || !SceneCodec.TryFromLink(link, out Scene scene, out _))
        {
            return;
        }

        _controller.Update(settings => scene.ApplyTo(settings) with
        {
            Templates = [.. settings.Templates.Where(template => template.Name != scene.Name), scene],
        });
    }

    private bool _quitting;

    private void Quit()
    {
        if (_quitting)
        {
            return;
        }

        _quitting = true;
        _controller?.SaveNow();
        _flyout?.Quit();
        _settingsWindow?.Quit();
        _window?.Quit();
        Exit();
    }

    /// <summary>
    /// Smoke test of the settings window and the flyout: every page is shown and written as a PNG, so the pages can be
    /// looked at without a Windows desktop. Fails when a page throws or renders nothing.
    /// </summary>
    private static async Task RunSettingsTourAsync(
        SmokeTest smokeTest, string directory, AppController controller, SettingsWindow settings, FlyoutWindow flyout, MainWindow window)
    {
        try
        {
            await Task.Delay(s_settleTime);
            var diagnostics = new List<DiagnosticResult>();
            for (int index = 0; index < SettingsWindow.Pages.Count; index++)
            {
                (string key, string title, _) = SettingsWindow.Pages[index];
                settings.ShowPage(key);
                await Task.Delay(TimeSpan.FromMilliseconds(700));
                (int width, int height) = await settings.SavePageAsync(Path.Combine(directory, string.Create(CultureInfo.InvariantCulture, $"settings-{index:00}-{key}.png")));
                diagnostics.Add(new DiagnosticResult($"Settings page {title}", width > 0 && height > 0, string.Create(CultureInfo.InvariantCulture, $"{width} x {height}")));
            }

            // The sync page once more with the sync switched on (in memory only: this run has no weather service, so
            // nothing is sent), a city and a paused element, to show all of its parts.
            AppSettings before = controller.Settings;
            controller.Update(s => s with
            {
                WeatherSync = s.WeatherSync with
                {
                    Enabled = true,
                    City = new CityChoice { Name = "Berlin", Country = "DE", Latitude = 52.52, Longitude = 13.41 },
                    Paused = [SyncElement.Rain],
                },
            });
            settings.ShowPage("sync");
            await Task.Delay(TimeSpan.FromMilliseconds(700));
            (int syncWidth, int syncHeight) = await settings.SavePageAsync(Path.Combine(directory, "settings-02-sync-on.png"));
            diagnostics.Add(new DiagnosticResult("Settings page Weather sync (on)", syncWidth > 0 && syncHeight > 0, string.Create(CultureInfo.InvariantCulture, $"{syncWidth} x {syncHeight}")));
            controller.Update(_ => before);

            flyout.StayOpen = true;
            flyout.ShowNearTray();
            await Task.Delay(TimeSpan.FromMilliseconds(700));
            (int flyoutWidth, int flyoutHeight) = await flyout.SaveAsync(Path.Combine(directory, "flyout.png"));
            diagnostics.Add(new DiagnosticResult("Flyout", flyoutWidth > 0 && flyoutHeight > 0, string.Create(CultureInfo.InvariantCulture, $"{flyoutWidth} x {flyoutHeight}")));

            int exitCode = smokeTest.WriteReport(diagnostics, []);

            // Closing the diagnostics window removes the overlays before the process ends.
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

            if (smokeTest.Visitors)
            {
                diagnostics.AddRange(await CheckVisitorsAsync(window));
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

            // What the runtime policy will see: power source, animations, foreground app (its name only).
            SystemState system = window.SystemState;
            monitorNotes.Add(string.Create(
                CultureInfo.InvariantCulture,
                $"System: {(system.OnBattery ? "on battery" : "on mains power")}, animations {(system.AnimationsEnabled ? "on" : "off")}, foreground app {system.ForegroundProcess ?? "unknown"}"));

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

            if (smokeTest.Rain || smokeTest.Snow || smokeTest.Leaves || smokeTest.Fog || smokeTest.Sun || smokeTest.Grass || smokeTest.Visitors)
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

            if (smokeTest.Visitors)
            {
                // Then a visitor onto the app window's top edge for the second screenshot.
                await Task.Delay(TimeSpan.FromMilliseconds(1500));
                window.InviteVisitor(Core.Visitors.VisitorKind.Tuck, Win32Interop.GetWindowFromWindowId(window.AppWindow.Id));
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
    /// Visitors: one comes onto the app window, the render loop runs for it, the keyboard collect takes it (and reports
    /// it), and the loop stops again.
    /// </summary>
    private static async Task<List<DiagnosticResult>> CheckVisitorsAsync(MainWindow window)
    {
        var results = new List<DiagnosticResult>();
        Core.Visitors.VisitorKind? collected = null;
        void OnCollected(object? sender, VisitorCollectedEventArgs e) => collected = e.Kind;
        window.VisitorCollected += OnCollected;
        try
        {
            nint handle = Win32Interop.GetWindowFromWindowId(window.AppWindow.Id);
            bool invited = window.InviteVisitor(Core.Visitors.VisitorKind.Pip, handle);
            await Task.Delay(TimeSpan.FromSeconds(1.5));
            OverlayStatus? visiting = window.Refresh();
            Core.Visitors.VisitorInfo? visitor = visiting is { Visitors.Count: > 0 } ? visiting.Visitors[0] : null;
            results.Add(new DiagnosticResult(
                "Visitor arrives on the app window",
                invited && visitor is { Kind: Core.Visitors.VisitorKind.Pip } && visitor.Value.Surface?.OwnerId == handle && visiting!.IsAnimating,
                visitor is { } v ? string.Create(CultureInfo.InvariantCulture, $"{v.Kind} {v.State} at {v.Feet.X:0}, {v.Feet.Y:0} on {v.Surface}") : "no visitor"));

            Core.Visitors.VisitorKind? taken = window.CollectVisitor();
            await Task.Delay(TimeSpan.FromSeconds(1));
            OverlayStatus? after = window.Refresh();
            results.Add(new DiagnosticResult(
                "Keyboard collect takes the visitor",
                taken == Core.Visitors.VisitorKind.Pip && collected == Core.Visitors.VisitorKind.Pip && after?.Visitors.Count == 0,
                string.Create(CultureInfo.InvariantCulture, $"collected {taken?.ToString() ?? "nothing"}, event {collected?.ToString() ?? "none"}, {after?.Visitors.Count ?? -1} left")));
            results.Add(new DiagnosticResult("Render loop stops after the visit", after is { IsAnimating: false }, after?.IsAnimating == true ? "still animating" : "idle"));
        }
        finally
        {
            window.VisitorCollected -= OnCollected;
        }

        return results;
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
