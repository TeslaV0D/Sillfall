# ADR-0008: MVP cut – tray icon and persisted settings

- Status: accepted
- Date: 2026-11-11
- Milestone: MVP cut (after M5, PLAN.md, section 12)

## Context

After M5 the plan calls for a daily-driver build: M0–M5 plus a minimal tray menu. Until then the app was a test window. Closing ended the weather and nothing persisted.

## Decisions

### 1. Tray icon via Win32

`TrayIcon` (Platform) uses `Shell_NotifyIcon` with a hidden window on the app UI thread. WinUI 3 has no tray icon; a third-party package is not worth it for icon plus menu.

- It is an invisible top-level window, not message-only. Only top-level windows receive `TaskbarCreated`, so the icon returns after an Explorer restart. Invisible windows are ignored by window tracking.
- `Shell_NotifyIcon` and `NOTIFYICONDATAW` are declared manually. CsWin32 emits them only for a fixed CPU architecture because the struct packing differs on x86. The app ships only x64 and ARM64 with the same layout.
- Menu (`TrayMenu`, Core) is a data model and tested. Rebuilt on each open so it always reflects current state.

Menu:

- “Open Sillfall” (also left-click),
- Rain and Snow each Off, Light, Medium, Heavy,
- Sound,
- “Pause all effects”,
- “Quit”.

Tooltip describes what is running.

### 2. Close hides the window

With a tray icon, close only hides the window; weather keeps running. Only “Quit” in the tray exits. If the shell rejects the icon, behavior reverts to before so there is still a way back.

### 3. Settings as JSON

`SettingsStore` (Core) saves `UserSettings` under `%LOCALAPPDATA%\<AppInfo.Name>\settings.json`:

- Pause,
- Rain,
- Rain depth,
- Snow,
- Sound.

Writes happen 0.5 s after the last change so dragging a slider does not hit disk every step. File is written to a temp file then swapped. Corrupt or hand-edited files yield defaults or clamped values, never a crash. `Version` is reserved for migration as settings grow in M9.

Smoke test does not read or write settings. It always starts in the same state.

### 4. No test pattern on normal start

The app used to show the calibration pattern at launch. Normal start leaves it off; the test window can still enable it. Smoke test still requires it.

## Not in the MVP

These arrive with M9:

- Autostart,
- Tray flyout,
- Settings window,
- Colors,
- Random mode.
