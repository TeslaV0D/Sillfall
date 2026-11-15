# ADR-0016: Hardening

- Status: accepted
- Date: 2026-11-12
- Milestone: M13

## Context

PLAN.md, M13 requires:

- fullscreen and game detection,
- pause on lock, standby, and Remote Desktop,
- GPU device loss recovery,
- handling the Start menu,
- local crash logs without telemetry,
- 8 hours without leaks.

Sign-off: soak test passed, budgets on reference hardware.

## Decisions

### 1. System states pause weather

`SystemState` (Core) knows:

- lock (including disconnected session and standby),
- fullscreen,
- presentation mode,
- Remote Desktop.

Foreground process indicates whether Start, Search, or notifications are open. `RuntimePolicy` decides in this order:

1. off
2. locked
3. fullscreen
4. presentation
5. Remote Desktop (can disable)
6. reduced motion
7. high contrast
8. battery
9. Start and Search (can disable)
10. app rules

A pause removes overlay windows entirely. Nothing sits above a game, not even empty windows. No hooks into other processes (anti-cheat).

Detection in `OverlayHost`:

- **Fullscreen and presentation:** `SHQueryUserNotificationState`, on every focus change and every 2 s. Shell does not push this. Timer runs on the control window so a game ending is noticed even with no overlays. Own process fullscreen does not count (no self-pause).
- **Lock and Remote Desktop:** `WTSRegisterSessionNotification` (lock, unlock, connect, disconnect) and `SM_REMOTESESSION`.
- **Standby:** `WM_POWERBROADCAST` (`PBT_APMSUSPEND`, `PBT_APMRESUME*`). After wake, elapsed time is not simulated; monitors and graphics are rechecked.
- **Start menu:** Shell process names in foreground (`StartMenuExperienceHost`, `SearchHost`, `ShellExperienceHost`, …). Only file name is read, like app rules.

### 2. GPU device loss

Since M1 (ADR-0003): device rebuilds on `DEVICE_REMOVED/RESET`, up to three times per frame. Core simulation is preserved. New: unlock, wake, and remote connect also trigger monitor and device checks.

### 3. Local crash reports

`CrashLog` (Core) writes a text report under `%LOCALAPPDATA%\<AppInfo.Name>\logs`; last ten kept.

- **Content:** version, Windows version, architecture, fault location, full stack trace.
- **Privacy:** profile path and username redacted. Nothing is sent.
- **Sources:** unhandled exceptions (app, tasks, WinUI) and errors the overlay thread catches and survives. Latter used to appear only in status; written at most once per session.
- **Access:** Advanced → Open crash reports.

### 4. Long-run test

- **Core:** `LongRunTests` simulates ten minutes with all effects, visitors, and constantly changing windows. Managed heap must not grow after warm-up.
- **App:** `--soak=<minutes>` (`SoakRun`) runs all effects, invites visitors, moves and resizes the window. Every 30 s measures private memory, managed heap, handles, GDI and USER objects. `SoakAnalysis` compares median of first vs last third after warm-up with base tolerance plus rate per hour. Report includes CPU and frame times.
- **CI:** Dedicated job runs 5 minutes on every push. Catches only fast leaks. Longer runs via workflow dispatch (~5.5 h max).
- **8 hours:** Full run on a real PC: `pwsh tools/ci/soak-test.ps1 -Minutes 480`.

## Amendment 2026-11: full-screen detection does not trust `QUNS_BUSY` alone

Desktop run 2026-11-14 (findings F-01, F-18): while Sillfall's own topmost, full-monitor overlay exists, the shell
reports `QUNS_BUSY` whatever is in front (measured: busy exactly while the overlay existed, `QUNS_ACCEPTS_NOTIFICATIONS`
without Sillfall). The own-process guard only helped with Sillfall's window in front. With any other app or the
desktop in front, the pause removed the overlay, the shell state cleared, the overlay came back: a 2 s blink loop.

Decision:

- `ScreenUseRules.Evaluate` (Core, unit-tested) decides from a `ScreenUseSample`: the shell state, the foreground
  window's visible frame, its monitor's bounds, and whether it is this process, the desktop/taskbar (`Progman`,
  `WorkerW`, `Shell_TrayWnd`, `Shell_SecondaryTrayWnd`), maximized, captioned. Only class names and rectangles are read.
- `QUNS_BUSY` counts as full screen only if the foreground window covers its whole monitor, is not the desktop, not
  this app, and is not a maximized window with a title bar (auto-hide taskbar). `QUNS_RUNNING_D3D_FULL_SCREEN` and
  `QUNS_PRESENTATION_MODE` are trusted as reported, except with this app in front.
- `ScreenUseFilter` adds hysteresis: a changed answer is published only after two samples in a row agree. The second
  sample is taken 400 ms after the first (a one-shot timer), so a real full-screen app still pauses within about half
  a second of taking the focus.
- Smoke run `foreground`: a helper process's window in front, the full-monitor overlay must stay for 6 s.

## Amendment 2026-11: clipboard history and emoji panel

Finding F-17: Win+V and Win+. open panels of `TextInputHost.exe`, which do not take the foreground, so the foreground
process check never saw them. `SystemState.TextInputPanelOpen` is set while a visible, uncloaked
`Windows.UI.Core.CoreWindow` of `TextInputHost.exe` exists (it stays cloaked while closed); `ShellOpen` includes it, so
"Hide the weather while Start, search or notifications are open" covers both panels. Checked when such a window shows,
hides, cloaks or uncloaks (WinEvents already hooked) and with the 2 s screen check. Only class and process names are
read.

Finding F-19: a pause removed the overlays but left a visitor's hit window catching clicks. The hit windows are now
synced after the overlays changed, so a pause hides them (a held visitor is let go). Smoke check "Click-through while
paused".

## Consequences

- Windows behavior here cannot be replayed in CI: lock, standby, exclusive fullscreen games, RDP, Start menu. Checklist M13.
- PLAN.md section 9 budgets (CPU under 3%, frame under 2.5 ms on iGPU) apply to real hardware. CI runner uses WARP on CPU and yields comparison values only.
