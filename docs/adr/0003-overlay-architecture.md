# ADR-0003: Overlay architecture

- Status: accepted
- Date: 2026-11-11
- Milestone: M1

## Context

PLAN.md, section 3.1 fixes the technology: one window per monitor, DirectComposition, premultiplied alpha, click-through via `WS_EX_TRANSPARENT`. Open questions were: which thread owns the overlays, how monitor changes are handled, and how to verify behavior without a Windows desktop.

## Decisions

### 1. Dedicated overlay thread with its own message loop

`OverlayHost` starts a thread with a Win32 message loop. All overlay windows, the D3D11 device, and DirectComposition objects live there. The app talks to the host only through thread-safe methods (`Start`, `Apply`, `GetStatus`, `Dispose`).

Rationale: Overlays are not tied to the WinUI dispatcher. A blocked UI thread does not freeze them, and the tool window or tray can work independently. The host also remains usable in the Sandbox app and in tests without WinUI.

### 2. Layer separation via `IOverlayRenderer`

Platform (windows) and Render (D3D/DComp) do not reference each other. Contract `IOverlayRenderer` / `IOverlaySurface` lives in Core. The app wires both. That keeps the rule “Platform, Render, and Audio reference only Core” from `ArchitectureTests` intact.

### 3. Monitor changes: rebuild instead of patch

On `WM_DISPLAYCHANGE`, `WM_DPICHANGED`, and work-area changes, after 250 ms of quiet the layout is replanned (`OverlayLayoutPlanner`, tested in Core). An overlay whose monitor changed position, size, or DPI is destroyed and recreated instead of resizing the window and swap chain. That is rare and avoids stale state. If only work area or primary flag changes, the overlay stays.

Monitor identity is the device name (`\\.\DISPLAY1`). If Windows renames a monitor when cables change, the overlay is recreated. That is harmless.

### 4. Topmost does not stay on top by itself

`WS_EX_TOPMOST` alone is not enough. CI screenshots showed: the app window activated after the overlays sat above them, as did the taskbar. So the host listens with `SetWinEventHook(EVENT_SYSTEM_FOREGROUND, WINEVENT_OUTOFCONTEXT)` for foreground changes and pushes overlays back to the top of the topmost band (`SetWindowPos(HWND_TOPMOST)` without activation). The hook runs out-of-process; nothing is injected.

The “Z-order” check walks from the overlay upward and lists visible windows overlapping the monitor (class name and topmost flag only, never titles). A normal window above is an error. Other topmost windows are only reported.

Start menu, search, and notifications sit in their own Z-order bands above desktop windows. The overlay cannot go above them, by design. Hiding when the start menu is open follows in M13.

### 5. Device lost

If `Present` returns `DXGI_ERROR_DEVICE_REMOVED/RESET/HUNG`, all surfaces are released, the device is recreated, and surfaces are rebuilt. After three failed attempts the error stays visible in status. A test on real hardware is in checklist M1.

### 6. No render loop in M1

M1 shows a static test pattern. Drawing happens only on changes (start, options, monitors). That makes “0% GPU when idle” trivial and measurable: the smoke test compares frame counters between two measurements. The fixed-timestep frame loop arrives in M3.

### 7. Verification without a Windows desktop

- **WARP tests** render the test pattern offscreen and compare pixels to `TestPatternLayout`, the same definition the shader uses for constants.
- **App self-check:** `OverlayDiagnostics` evaluates measured window state (styles, `WindowFromPoint` hit test, foreground window, display affinity, frame counters).
- **CI screenshot:** The smoke script captures the Windows runner desktop and checks that the pattern is on screen, including above the centered app window, and missing when screenshot exclusion is on.
- All images go to CI artifact `previews`.

What that does not cover (real mouse clicks through the overlay, multi-monitor mixed DPI, hot-plug, GPU load on real hardware) is in `docs/manual-tests/M1.md`.

## Consequences

- Every call from outside is a thread switch with waiting. Fine for options and status. Per-frame data (from M3 onward) does not go this way; it is produced on the overlay thread.
- `WindowFromPoint` as a click-through probe is a strong hint, not proof. Proof requires clicks on real hardware.
