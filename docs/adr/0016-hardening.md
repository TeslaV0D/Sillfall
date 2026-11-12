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

## Consequences

- Windows behavior here cannot be replayed in CI: lock, standby, exclusive fullscreen games, RDP, Start menu. Checklist M13.
- PLAN.md section 9 budgets (CPU under 3%, frame under 2.5 ms on iGPU) apply to real hardware. CI runner uses WARP on CPU and yields comparison values only.
