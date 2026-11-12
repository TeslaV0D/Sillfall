using Microsoft.UI.Dispatching;
using Weatherly.Core.Overlay;
using Weatherly.Core.Scenes;
using Weatherly.Core.Settings;

namespace Weatherly.App;

/// <summary>
/// The one place that knows the user's settings (PLAN.md, section 7). Every window (settings, flyout, tray,
/// diagnostics) changes them through <see cref="Update"/>; the controller turns them into overlay options with
/// the runtime policy (pause, frame rate, battery) and the random walks, applies them, stores them debounced and
/// switches autostart.
/// </summary>
internal sealed class AppController
{
    /// <summary>Settings are written this long after the last change.</summary>
    private static readonly TimeSpan s_saveDelay = TimeSpan.FromMilliseconds(500);

    /// <summary>Slider drags are applied at most this often: every apply rebuilds the overlay's options.</summary>
    private static readonly TimeSpan s_applyDelay = TimeSpan.FromMilliseconds(60);

    private readonly MainWindow _window;
    private readonly SettingsStore? _store;
    private readonly bool _followSystem;
    private readonly IntensityDirector _director = new(seed: (ulong)Environment.TickCount64);
    private readonly DispatcherQueueTimer _saveTimer;
    private readonly DispatcherQueueTimer _applyTimer;
    private readonly DispatcherQueueTimer _randomTimer;
    private DateTime _lastRandomUpdate = DateTime.UtcNow;
    private bool _applying;

    /// <param name="window">The diagnostics window: it owns the overlay host.</param>
    /// <param name="store">Where the settings are saved; <see langword="null"/> keeps them in memory (smoke test).</param>
    /// <param name="settings">The settings to start with.</param>
    /// <param name="followSystem">Apply the runtime policy and the random walks (off in the smoke test, which sets its own state).</param>
    public AppController(MainWindow window, SettingsStore? store, AppSettings settings, bool followSystem)
    {
        _window = window;
        _store = store;
        _followSystem = followSystem;
        Settings = settings.Sanitized();

        DispatcherQueue queue = window.DispatcherQueue;
        _saveTimer = CreateTimer(queue, s_saveDelay, repeating: false, SaveNow);
        _applyTimer = CreateTimer(queue, s_applyDelay, repeating: false, Apply);
        _randomTimer = CreateTimer(queue, TimeSpan.FromSeconds(1), repeating: true, () =>
        {
            if (HasRandomEffect)
            {
                Apply();
            }
        });

        window.OptionsChanged += OnWindowOptionsChanged;
        if (followSystem)
        {
            window.SystemStateChanged += (_, _) => Apply();
            _randomTimer.Start();
        }
    }

    /// <summary>Raised on the UI thread after the settings or the runtime decision changed.</summary>
    public event EventHandler? Changed;

    public AppSettings Settings { get; private set; }

    /// <summary>The latest runtime decision: why the weather pauses, the frame rate, the battery factor.</summary>
    public RuntimeDecision Decision { get; private set; } = new(false, null, 60, 1f);

    public MainWindow Window => _window;

    /// <summary>Built-in presets followed by the user's own templates.</summary>
    public IReadOnlyList<Scene> AllTemplates => [.. ScenePresets.All, .. Settings.Templates];

    private bool HasRandomEffect => Enum.GetValues<EffectKind>().Any(kind => Settings.Effect(kind).Mode == IntensityMode.Random);

    /// <summary>Changes the settings. Applied soon (slider drags are throttled) and saved after a short pause.</summary>
    public void Update(Func<AppSettings, AppSettings> change)
    {
        ArgumentNullException.ThrowIfNull(change);
        AppSettings previous = Settings;
        Settings = change(Settings).Sanitized();
        if (previous.Advanced.Autostart != Settings.Advanced.Autostart)
        {
            _ = Autostart.SetAsync(Settings.Advanced.Autostart);
        }

        ApplySoon();
        ScheduleSave();
        Changed?.Invoke(this, EventArgs.Empty);
    }

    /// <summary>Sets one effect's intensity (tray menu, flyout).</summary>
    public void SetIntensity(EffectKind kind, float intensity) =>
        Update(settings => settings.WithEffect(kind, settings.Effect(kind) with { Intensity = intensity }));

    /// <summary>Applies a template's weather.</summary>
    public void ApplyScene(Scene scene)
    {
        ArgumentNullException.ThrowIfNull(scene);
        Update(scene.ApplyTo);
    }

    /// <summary>Back to the defaults (the templates the user made are kept).</summary>
    public void Reset() => Update(settings => new AppSettings { Templates = settings.Templates });

    /// <summary>Recomputes the options from the settings and the system and gives them to the overlay if they changed.</summary>
    public void Apply()
    {
        _applyTimer.Stop();
        DateTime now = DateTime.UtcNow;
        double elapsed = (now - _lastRandomUpdate).TotalSeconds;
        _lastRandomUpdate = now;

        AppSettings effective = _followSystem ? Settings : Settings with { Advanced = Settings.Advanced with { ReducedMotion = ReducedMotion.Never } };
        SystemState system = _followSystem ? _window.SystemState : SystemState.Default;
        Decision = RuntimePolicy.Decide(effective, system);
        ReadOnlySpan<float> intensities = _director.Update(Settings, elapsed);
        OverlayOptions options = RuntimeOptions.Build(Settings, _window.Options, intensities, Decision);

        _applying = true;
        try
        {
            if (!RuntimeOptions.Same(options, _window.Options))
            {
                _window.ApplyOptions(options);
            }

            _window.SoundVolume = Settings.Sound.Master * Settings.Sound.Rain;
            if (_window.SoundOn == Settings.Sound.Muted)
            {
                _window.SetSound(!Settings.Sound.Muted);
            }
        }
        finally
        {
            _applying = false;
        }

        _window.Tray?.SetTooltip(Decision.Reason is { } reason
            ? $"{Core.AppInfo.Name}: {reason.ToLowerInvariant()}"
            : Core.Tray.TrayMenu.Tooltip(options));
        Changed?.Invoke(this, EventArgs.Empty);
    }

    /// <summary>Writes the settings now (quit, or after the save delay).</summary>
    public void SaveNow()
    {
        _saveTimer.Stop();
        if (_store is null)
        {
            return;
        }

        try
        {
            _store.Save(Settings);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            // Not being able to remember a setting is no reason to interrupt the user. It is tried again on the next change.
        }
    }

    private static DispatcherQueueTimer CreateTimer(DispatcherQueue queue, TimeSpan interval, bool repeating, Action tick)
    {
        DispatcherQueueTimer timer = queue.CreateTimer();
        timer.Interval = interval;
        timer.IsRepeating = repeating;
        timer.Tick += (_, _) => tick();
        return timer;
    }

    private void ApplySoon()
    {
        if (!_applyTimer.IsRunning)
        {
            _applyTimer.Start();
        }
    }

    private void ScheduleSave()
    {
        _saveTimer.Stop();
        _saveTimer.Start();
    }

    /// <summary>
    /// The diagnostics window's own switches and sliders changed the options: they become the settings. Changes the
    /// controller made itself (pauses, random walks, the battery factor) are not written back.
    /// </summary>
    private void OnWindowOptionsChanged(object? sender, EventArgs e)
    {
        if (_applying)
        {
            return;
        }

        // While the policy pauses the weather the overlay switch shows off; that is not the user's master switch.
        bool enabled = Decision.Paused ? Settings.Enabled : _window.Options.Enabled;
        Settings = (Settings.WithOverlay(_window.Options, _window.SoundOn) with { Enabled = enabled }).Sanitized();
        ScheduleSave();
        Changed?.Invoke(this, EventArgs.Empty);
    }
}
