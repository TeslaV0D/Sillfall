# ADR-0012: Settings, runtime policy, and UI

- Status: accepted (visuals and UX subject to maintainer sign-off)
- Date: 2026-11-12
- Milestone: M9

## Context

PLAN.md, section 7 defines the full UI:

- Tray flyout,
- settings window with ten pages,
- per-effect color, brightness, fixed/random,
- templates with sharing,
- per-monitor settings,
- app rules,
- performance and battery,
- autostart,
- single instance,
- `sillfall://` protocol,
- versioned settings with migration.

## Decisions

### 1. One model, one controller

- `AppSettings` (Core) is the only persisted model, versioned. Version 1 from the MVP migrates on load. Values are sanitized on load.
- `AppController` (App) is the only place that changes settings. Persists 0.5 s after the last change and applies at most every 60 ms.
- `OverlayOptions` come from settings, system state, and random runs via `RuntimePolicy`, `IntensityDirector`, and `RuntimeOptions` in Core, all tested.
- What the controller sets at runtime is never written back: pauses, random draws, battery factor.

### 2. Runtime policy

**Pause.** Reasons in this order:

1. Master switch,
2. Reduced motion (always, or following Windows “Show animations”),
3. Battery with “Pause”,
4. App rule for the foreground program.

**Smoothness.**

| Setting | Frame rate |
|---|---|
| Auto | 60 fps, 30 on battery |
| Least energy | 30 fps |
| Balanced | 60 fps |
| Smoothest | Monitor refresh |

“Reduce” on battery halves intensity and caps at 30 fps.

**System state.** Overlay host reads power source, animation setting, and foreground program file name and reports changes. Only the file name is read, never a window title.

**Known:** On Windows Server (including the CI runner) animations are off by default. With “Follow Windows” the app pauses there. Intended for users. Smoke test disables runtime policy.

### 3. Per monitor and app rules

- **Monitors:** Disabled monitors get no overlay. Others draw with their own opacity as a factor in sprite and atmosphere shaders.
- **“Hide over this app’s windows”:** That program’s windows are made transparent again after drawing (no blending). The full window rectangle is cut out even where another window stacks above. Exact clipping is not worth the complexity yet.

### 4. UI in code instead of XAML

Settings window is a `NavigationView` with ten pages. Flyout is a small borderless window with acrylic above the tray. Little XAML; pages are built in C# from shared `Ui` blocks.

- **Why:** WinUI app builds only on Windows. CI catches code errors reliably; XAML errors often appear only at runtime. Shared blocks look consistent.
- **Pages:** Effects, Templates, Weather sync (placeholder until M10), Visitors (placeholder until M11), Sound, Displays, Apps, Performance (live numbers), Advanced, About.
- **Check:** Smoke run `--settings-tour` shows each page and the flyout and saves PNGs to previews. Pages can be reviewed without a Windows desktop.

### 5. Windows and lifetime

- **Background app:** Runs in the notification area (`DispatcherShutdownMode.OnExplicitShutdown`). Only “Quit” exits.
- **Tray clicks:** Left click opens flyout; menu “Open” opens settings.
- **Diagnostics window:** Former test window (`MainWindow`) stays reachable (Advanced). It still owns overlay host, tray icon, and single-instance lock. Smoke test drives it.
- **No tray icon:** If the shell rejects the icon, closing settings exits the app. Otherwise it would be unreachable in the background.

### 6. Single instance, links, autostart

- **Single instance:** Named mutex per session and a hidden message window. A second launch sends its command line via `WM_COPYDATA` to the running instance and exits.
- **`sillfall://scene?d=…`:** Packaged MSIX declares the protocol in the manifest. Unpackaged, the app registers it per user on each start. A link is sanitized, applied, and kept as a template.
- **Autostart:** Packaged via `StartupTask` (off by default), unpackaged via Run key. Autostart path uses `--background` for notification area only.

## Consequences

- Windows behavior on real hardware is sign-off only (checklist M9): flyout position and focus, MSIX autostart, protocol activation, battery switch, foreground app rule.
- UI language is English for now. German arrives in M12 via `.resw`.
