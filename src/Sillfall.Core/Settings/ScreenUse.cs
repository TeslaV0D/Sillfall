using Sillfall.Core.Geometry;

namespace Sillfall.Core.Settings;

/// <summary>What <c>SHQueryUserNotificationState</c> reports (the values of <c>QUERY_USER_NOTIFICATION_STATE</c>).</summary>
public enum ShellNotificationState
{
    Unknown = 0,
    NotPresent = 1,
    Busy = 2,
    RunningDirect3DFullScreen = 3,
    PresentationMode = 4,
    AcceptsNotifications = 5,
    QuietTime = 6,
    App = 7,
}

/// <summary>
/// One look at the screen: what the shell says, and the geometry of the foreground window. No titles, no content.
/// </summary>
/// <param name="State">The shell's notification state.</param>
/// <param name="ForegroundBounds">The foreground window's visible frame, in physical pixels.</param>
/// <param name="MonitorBounds">The whole monitor the foreground window is on (not the work area).</param>
/// <param name="IsOwnProcess">The foreground window belongs to this app (settings, flyout).</param>
/// <param name="IsDesktop">The foreground window is the shell's desktop or taskbar (<c>Progman</c>, <c>WorkerW</c>, <c>Shell_TrayWnd</c>).</param>
/// <param name="IsMaximized">The foreground window is maximized.</param>
/// <param name="HasCaption">The foreground window has a title bar (<c>WS_CAPTION</c>).</param>
public readonly record struct ScreenUseSample(
    ShellNotificationState State,
    PixelRect ForegroundBounds,
    PixelRect MonitorBounds,
    bool IsOwnProcess = false,
    bool IsDesktop = false,
    bool IsMaximized = false,
    bool HasCaption = false);

/// <summary>
/// Decides whether another app runs full screen (ADR-0016, amendment 2026-11).
/// <para>
/// The shell's state alone cannot be trusted: Sillfall's own overlay is a topmost window that covers the monitor, and
/// while it exists the shell reports <see cref="ShellNotificationState.Busy"/> whatever is in front (finding F-01).
/// "Busy" therefore only counts when the foreground window itself covers its whole monitor and is not a plain
/// maximized window, not the desktop and not one of this app's windows. Direct3D exclusive mode and presentation mode
/// are reported by the shell for other apps only and are trusted as they are.
/// </para>
/// </summary>
public static class ScreenUseRules
{
    public static (bool FullScreen, bool Presentation) Evaluate(in ScreenUseSample sample)
    {
        bool presentation = sample.State == ShellNotificationState.PresentationMode;
        if (sample.IsOwnProcess)
        {
            return (false, presentation);
        }

        bool fullScreen = sample.State switch
        {
            ShellNotificationState.RunningDirect3DFullScreen => true,
            ShellNotificationState.Busy => !sample.IsDesktop && CoversMonitor(sample) && !(sample.IsMaximized && sample.HasCaption),
            _ => false,
        };

        return (fullScreen, presentation);
    }

    /// <summary>Whether the foreground window covers every pixel of its monitor.</summary>
    public static bool CoversMonitor(in ScreenUseSample sample)
    {
        PixelRect window = sample.ForegroundBounds;
        PixelRect monitor = sample.MonitorBounds;
        return !monitor.IsEmpty
            && window.Left <= monitor.Left
            && window.Top <= monitor.Top
            && window.Right >= monitor.Right
            && window.Bottom >= monitor.Bottom;
    }
}

/// <summary>
/// Hysteresis for the screen use: a new answer is taken only after <see cref="ConfirmSamples"/> samples in a row agree,
/// so one odd sample never removes and rebuilds the overlays.
/// </summary>
public sealed class ScreenUseFilter
{
    /// <summary>Samples in a row needed before the published state changes.</summary>
    public const int ConfirmSamples = 2;

    private (bool FullScreen, bool Presentation) _candidate;
    private int _agreeing;

    /// <summary>The state that is published.</summary>
    public (bool FullScreen, bool Presentation) Current { get; private set; }

    /// <summary>
    /// Whether a sample differed from <see cref="Current"/> and waits for confirmation: the caller should look again soon.
    /// </summary>
    public bool Pending => _agreeing > 0;

    /// <summary>Feeds one sample and returns the published state.</summary>
    public (bool FullScreen, bool Presentation) Update((bool FullScreen, bool Presentation) sample)
    {
        if (sample == Current)
        {
            _agreeing = 0;
            return Current;
        }

        _agreeing = sample == _candidate ? _agreeing + 1 : 1;
        _candidate = sample;
        if (_agreeing >= ConfirmSamples)
        {
            Current = sample;
            _agreeing = 0;
        }

        return Current;
    }
}
