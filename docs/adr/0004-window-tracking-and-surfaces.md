# ADR-0004: Window tracking and surfaces

- Status: accepted
- Date: 2026-11-11
- Milestone: M2

## Context

Effects need the visible edges of open windows: top edges (snow, leaves, puddles, grass), sides (gutters), and one ground per monitor. PLAN.md, sections 2, 3.2, and 3.3 define the Win32 side. Open questions were exact occlusion rules, which windows to ignore, and where tracking runs.

## Decisions

### 1. Tracking on the overlay thread

`OverlayHost` owns tracking too. WinEvent hooks need a message loop, and the overlay thread already has one. Snapshot, surfaces, and drawing stay on one thread without locks.

- Hooks (`WINEVENT_OUTOFCONTEXT`, narrow ranges): foreground, move/size start/end, minimize start/end, object destroy/show/hide/reorder, location change, cloaked/uncloaked. Object events count only for whole windows (`OBJID_WINDOW`, `CHILDID_SELF`), not caret, cursor, or controls.
- Events are coalesced: the first starts a 16 ms timer; more until then do nothing. At most one snapshot per frame, even while dragging a window.
- Additional 10 Hz poll for changes without events. Runs only while overlays exist.
- A snapshot identical to the previous one triggers nothing. Idle does not redraw.

### 2. Which windows count

`EnumWindows` yields Z-order top to bottom. Ignored: invisible, minimized, and cloaked windows (other virtual desktops, suspended Store apps), tool windows, click-through layered windows (overlays, including third-party), windows under 100 × 60 px, and shell classes `Progman`, `WorkerW` (desktop), `Shell_TrayWnd`, `Shell_SecondaryTrayWnd` (taskbars). Own overlays are recognized by class-name prefix.

Only geometry and class name are read. Bounds come from `DWMWA_EXTENDED_FRAME_BOUNDS`, not `GetWindowRect`, so invisible resize borders are excluded.

### 3. Occlusion rules

| Surface | Location | Occluded when a higher window … |
|---|---|---|
| Top edge | Row above `Top` | … covers the edge row or the row above it. A window flush on the edge leaves no room for snow. |
| Left/right side | Column just outside the frame | … covers that column or the window’s margin column. |
| Ground | Bottom of the work area (top of taskbar) | … reaches down to the ground (including through the window itself, e.g. maximized). |

Everything is clipped per monitor. A top edge on the monitor top (maximized window) has no slot and is dropped. A window spanning two monitors yields two pieces with the same key.

### 4. Identity and local coordinates

Each piece carries `SurfaceKey(window id, edge)` and the `Origin` of the full edge (left window edge or top). Accumulations from M4 onward store relative to origin and move with the window. Occluded parts drop from the list; data behind them remains in the effect.

Ground is bound to monitor index. If the monitor list changes, ground starts empty. Rare and acceptable.

### 5. Motion and shake

`WindowMotionTracker` derives events from consecutive snapshots: appeared, gone (closed, hidden, minimized), moved (with velocity in px/s), resized, shaken. Shake: at least three direction changes within 600 ms with over 20 px swing, separately for x and y.

### 6. No allocations while dragging

`SurfaceExtractor`, `WindowSnapshot`, and `SurfaceSet` are reusable and allocation-free after warm-up (checked with `GC.GetAllocatedBytesForCurrentThread`). Exception: a new window allocates state once in the motion tracker.

### 7. Verification without a desktop

- 36 unit tests for occlusion and clipping, plus motion and shake tests.
- WARP tests render the debug layer and check every drawn pixel lies in a band of extracted surfaces and vice versa.
- In the smoke test the app moves its own window and checks tracking adopts the new position exactly. The script checks the desktop screenshot: debug lines sit on the two rows above and two columns beside the app window. Window position is read via DWM independently of the tracker.

## Amendment 2026-11: other apps' overlays

Finding F-21: another app's transparent, click-through layer (the Claude desktop app's computer-use layer,
`WS_EX_LAYERED | WS_EX_TRANSPARENT | WS_EX_TOPMOST | WS_EX_NOACTIVATE`) appeared to get weather edges after a scaling
change. The enumerator already skipped layered + transparent windows, so the exact cause could not be confirmed (the
layer was not on screen while fixing). The visibility and style rules now live in `WindowFilter` (Core, tested):
invisible, minimized, cloaked, tool windows, layered + transparent windows, and layered windows with a constant alpha
of 0 are not tracked. Checklist item for the maintainer in `docs/manual-tests/fixes-2026-11.md`.

## Amendment 2026-11: tracking during interactive moves

Finding F-07: while the user drags or snaps a window, effects lagged behind or were left behind; scripted
`SetWindowPos` moves were clean. Cause: window updates went through a 16 ms `SetTimer`, and `WM_TIMER` is only
generated when the thread's queue is empty. A drag's flood of `EVENT_OBJECT_LOCATIONCHANGE` events kept it from firing
(and the 10 Hz poll too), so the tracked rectangle fell behind.

Decision: while a move/size loop is active (`EVENT_SYSTEM_MOVESIZESTART` … `END`), and for 0.6 s after any location,
move/size or minimize event (snap, maximize and restore animate the frame afterwards), the render loop reads the
windows itself every frame before simulating. The timer path stays for the idle case (debug layer without effects).
Measured with a scripted mouse drag at about 1 900 px/s and a 60 fps cap: lag between the real and the tracked left
edge mean 30 → 5 px, median 25 → 0 px, p95 83 → 28 px (one frame of motion).

## Amendment 2026-11: a dropped top edge is a roof

A top edge without a slot (maximized window, window at the monitor top) still has no surface. Since finding F-05 the
window is not "open at the top" for near particles either: they are hidden over it (ADR-0006, amendment 2026-11).

## Consequences

- A 10 Hz poll costs some CPU even when idle (one `EnumWindows` per 100 ms). Intentional (PLAN.md) and measured on reference hardware in M13.
- Windows that change geometry without events are seen within at most 100 ms.
- Popup menus, tooltips, and similar are usually tool windows or too small and get no edges.
