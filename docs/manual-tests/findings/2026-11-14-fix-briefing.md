# Fix briefing – desktop run 2026-11-14

This file is a briefing for a Claude Code session that fixes the problems found in the desktop test run
[2026-11-14-desktop-run.md](2026-11-14-desktop-run.md) (F-01 … F-21). Read that file first: every finding has steps
to reproduce, expected/actual behaviour, evidence, and a suspected location. **Suspected locations are guesses from
the test run. Confirm them in the code before you change anything.**

## 0. Ground rules

- Follow [CLAUDE.md](../../../CLAUDE.md) and PLAN.md §13. In particular:
  - Core stays Windows-free (`ArchitectureTests`). Put decision logic in Core, where it is testable; keep Win32 calls in Platform.
  - No allocations in the frame loop (`Simulate`/`Emit`, `OverlayHost.RenderAnimationFrame`).
  - Privacy invariants: geometry and process image names only, never window titles, no screen capture.
  - Visible strings only via `Texts.Get`/`Texts.Format`, with both `.resw` locales updated.
  - Non-trivial decisions get a short ADR (new one, or an "Amendment" section in the existing ADR).
- **One batch = one branch = one PR**, in the order of section 2. Branch names `fix/<batch>` (e.g. `fix/fullscreen-self-pause`). Do not mix batches.
- **Test first where possible:** write the failing Core/Render test that reproduces the finding, then fix it. Every fix that cannot be covered by an automated test gets a checklist item under `docs/manual-tests/` (extend `M13.md`/`M14.md` or a new `fixes-2026-11.md`).
- **Do not guess on visuals or real Windows behaviour** (CLAUDE.md rule 6). Where a fix changes how something looks, say so in the PR and leave the sign-off to the maintainer.
- **Questions (F-09, F-12, F-13) are not bugs to fix on your own.** Prepare options and ask (section 2, batch 9).
- Before each PR: `dotnet build Sillfall.slnx -p:Platform=x64`, `dotnet test --project tests/Sillfall.Core.Tests`,
  `dotnet test --project tests/Sillfall.Render.Tests`, `dotnet format Sillfall.CrossPlatform.slnf --verify-no-changes`,
  and `pwsh tools/ci/smoke-test.ps1` (green, including the snow run).
- When a finding is fixed, add `**Status:** fixed in <PR link>` under its heading in the findings file (same PR).

## 1. Overview

| Batch | Findings | Severity | Theme |
|---|---|---|---|
| 1 | F-01, F-18 | high, medium | Full-screen detection pauses the app because of its own overlay |
| 2 | F-04 | high | `sillfall://` links via the shell |
| 3 | F-05 | high | Near particles drawn over window content |
| 4 | F-07, F-06 | high, medium | Tracking during real drags/snaps; frame pacing |
| 5 | F-02, F-03 | medium | Frame-loop allocation (snow); performance budgets |
| 6 | F-17, F-19, F-21 | medium | Shell panels, visitor hit windows while paused, foreign overlays |
| 7 | F-08, F-10, F-11, F-20 | low | Settings UI polish |
| 8 | F-14, F-15, F-16 | low | Tooling (Sandbox, smoke test) |
| 9 | F-09, F-12, F-13 | question | Ask the maintainer, do not implement |

Batch 1 first: F-01 makes the weather blink whenever another app is in front, which confounds every manual check of
batches 3, 4 and 6.

## 2. Batches

### Batch 1 – Full-screen self-pause (F-01, F-18)

**Problem.** `SHQueryUserNotificationState` returns `QUNS_BUSY` while Sillfall's own topmost full-monitor overlay
exists. `SystemInfo.ScreenUse()` only ignores that when Sillfall itself is the foreground process. With any other app
in front, the overlay is removed (ADR-0016: a pause removes the overlays), the state clears, and the overlay comes
back, every 2 s (`OverlayHost.UpdateScreenUse` timer). F-18 is probably the same loop with the desktop (`Progman`/`WorkerW`) in
front, where it never resumes.

**Do:**
1. Confirm the cause. Show that `QUNS_BUSY` follows the overlay's existence and not the foreground app (the run measured busy = 100 % with the overlay up, 5/`ACCEPTS_NOTIFICATIONS` with Sillfall not running). If you cannot run on a desktop, write down the evidence from the findings file and ask the maintainer to confirm on a clean desktop (without other overlay apps).
2. Stop trusting `QUNS_BUSY` on its own. Suggested direction (verify, then decide; ADR-0016 amendment):
   - treat "full screen" as true only if the **foreground window** (not the shell desktop, not a Sillfall window) covers its whole monitor (`MonitorFromWindow` + DWM extended frame bounds, or `GetWindowRect` for borderless), **or** the state is `QUNS_RUNNING_D3D_FULL_SCREEN` / `QUNS_PRESENTATION_MODE`;
   - put that decision in Core (a pure function over a small snapshot: state, foreground bounds, monitor bounds, is-own-process, is-desktop) so it is unit-tested;
   - add hysteresis, so one sample does not toggle the overlays (e.g. require two consecutive checks before pausing or resuming).
3. Make sure the desktop (`Progman`/`WorkerW`, shell process `explorer.exe`) in front never counts as full screen (F-18). Win+D ("show desktop") must keep the weather.

**Tests:** Core tests for the decision function: own overlay only → not full screen; normal window in front → not; borderless window covering the monitor → full screen; desktop in front → not; D3D exclusive / presentation → pause. Smoke: add a run where a second, non-Sillfall window (e.g. a helper process window the script starts) is in front, and assert `Overlay windows: 1 of 1` for several seconds (no oscillation).

**Done when:** with Notepad or the desktop in front, the overlay stays present 100 % of a 30 s sample (the run used a 200 ms poll of `Sillfall.Overlay.*` windows); a real full-screen video/F11 still pauses and resumes; the "Paused while an app runs full screen" banner no longer flickers.

### Batch 2 – Scene links via the shell (F-04)

**Problem.** Windows hands the protocol handler `sillfall://scene/?d=…` (slash inserted after the host). `SceneCodec`
only accepts `sillfall://scene?d=`. `App.ApplySceneLink` returns silently on failure.

**Do:**
1. Parse links with `Uri` (or tolerant prefix handling): scheme `sillfall`, host `scene`, path empty or `/`, query parameter `d`. Keep `MaxSize` and the base64url/deflate validation.
2. Keep `SceneCodec.ToLink` producing the canonical `sillfall://scene?d=` form.
3. On a link that cannot be read, show feedback (Templates page `InfoBar` or a tray notification) with the reason that `TryFromLink` already returns, instead of `out _`. New strings in both `.resw` files.

**Tests:** `SceneTests`: round-trip for `scene?d=`, `scene/?d=`, upper-case scheme/host, trailing `&`/extra parameters, percent-encoded `d`; rejection of other hosts and oversized links.

**Done when:** Win+R with a copied link applies the scene (manual checklist item), and a broken link shows a message.

### Batch 3 – Near particles over window content (F-05)

**Problem.** Near drops/flakes/leaves are only stopped by *exposed top edges*. Where a window's top edge has no slot
(maximized or at the monitor top, ADR-0004) or is covered by another window, near particles fall across the window's
content. Free-falling gutter streams below a window's bottom corner (ADR-0006 §3) do the same over windows further
down. The user called it out on a maximized browser: "it rains through the window, … it should not".

This is a **design change** to ADR-0006 §1 (depth), ADR-0007 §2, ADR-0009 (leaves) and ADR-0004 (dropped top edge).
Write an ADR amendment before coding, with the chosen rule. Suggested rule (to confirm with the maintainer):
- a near particle whose position is inside the visible part of any tracked window that is **in front of the point where it entered** that window's area is treated like a distant one there (not drawn, or removed), i.e. near particles are occluded by windows they did not land on;
- a maximized/top-flush window behaves like a roof at the monitor top: near particles over it are culled (no puddles there);
- gutter streams end at the first window below them (drip off or splash on its top edge if exposed; otherwise end).

Use the existing occluder data (`RenderFrame` window rectangles, topmost first, as for fog/sun) or the surface set. Do it without allocations (struct-of-arrays, `Span`).

**Tests:** Core simulation tests with a maximized window and with two overlapping windows: after N steps, no near-particle sprite lies inside a window's client rectangle unless the particle is on that window's own top edge; gutter stream sprites never cross a lower window. Render golden image (WARP) for "maximized window in rain" (new FrameDump scene `rain-maximized.json`). Update `ASSETS.md` only if you add assets (scenes are not assets).

**Done when:** a maximized window and a window at y = 0 show no rain over their content; the Sandbox and FrameDump look unchanged elsewhere; visual sign-off is requested in the PR.

### Batch 4 – Tracking during interactive moves; frame pacing (F-07, F-06)

**F-07 problem.** When the *user* drags or snaps a window (Win+arrows, Snap Layouts, Aero Shake), puddles and rain
lag behind or are left behind. Script-driven `SetWindowPos` moves were clean. That points at the interactive
move/size loop or at DWM snap animations.

**Do (F-07):**
1. Reproduce with a scripted *interactive* move: `SendMessage(WM_SYSCOMMAND, SC_MOVE)` plus synthetic mouse input, or `SetWindowPos` while a `WM_ENTERSIZEMOVE` loop is active. Log the per-frame window rect seen by `WindowMotionTracker` vs. `DwmGetWindowAttribute(DWMWA_EXTENDED_FRAME_BOUNDS)`.
2. Check `WindowEnumerator` event coalescing (`EVENT_OBJECT_LOCATIONCHANGE`) during `EVENT_SYSTEM_MOVESIZESTART…END`. While a move is in progress, poll the moving window's bounds every frame instead of waiting for coalesced events.
3. After snap/maximize/restore, keep polling until the DWM frame bounds stop changing (snap animations), so surfaces do not freeze at an intermediate rectangle.

**F-06 problem.** `RenderAnimationFrame` paces with `Thread.Sleep(1)`. At the default 15.6 ms timer resolution,
"Smoothest" yields 64 fps instead of the display rate (180 Hz here), and the 60 fps cap yields ~49 fps. When another
app raises the timer resolution, the same build ran at 182 fps.

**Do (F-06):** pace with the swap chain's frame-latency waitable object (already latency waitable per PLAN.md §3) and/or a high-resolution waitable timer (`CreateWaitableTimerExW` with `CREATE_WAITABLE_TIMER_HIGH_RESOLUTION`). Do not call `timeBeginPeriod` globally. The cap logic stays in `RuntimePolicy`; only the wait changes. List the new APIs alphabetically in `NativeMethods.txt`.

**Tests:** a Core/Platform-independent pacing calculator if you factor one out; the smoke test's `Animation` line should report ≈ cap (Auto 60 ± 3 fps on a 180 Hz display; on CI only check "not above cap + 10 %").

**Done when:** Performance → "Right now" shows ~60 fps on Auto, ~30 on Least energy, the display rate on Smoothest, independent of other apps; the user-visible lag when dragging is gone (manual checklist, ask the maintainer to drag and snap).

### Batch 5 – Snow allocation and performance budgets (F-02, F-03)

**F-02.** `SnowEffect.FieldFor` lazily allocates a `SnowField` (`float[]` of width/2 bins plus dictionary growth)
when a flake first lands on a new edge, inside the frame loop. Smoke on this PC: 4048 B in the measured frames, 2 of 2.
Rain's `WaterField` avoids this. Check how (probably created on surface events), and do the same for snow: create or
resize fields in `OnSurfaceEvent`/`SetWindows` (outside `Simulate`/`Emit`), or pool them with pre-sized buffers. Check
leaves and grass for the same pattern. Extend the existing "does not allocate" Core test with "a window appears
mid-run and gets its first snow landing".

**F-03.** Measure before optimizing. Soak: CPU 9.3 % of a core (budget < 3 %), frame interval 22.3 ms, private memory
163→183 MB (budget 150 MB). All effects at 100 %: 19.3 % CPU, simulation 3.57 ms, 20 568 sprites, 287 MB, and
291 MB still allocated after all effects were switched off.
1. Separate the WinUI cost from the overlay: measure the budget scenario (all effects ~50 %, settings window *closed*, tray only) with `soak-test.ps1` changed to keep its window hidden, or add an option for that.
2. Profile (dotnet-trace / PerfView) the simulation at ~6 000 and ~20 000 sprites; the budget is sim < 1.0 ms p95 at ~50 %.
3. Memory: find what holds ~290 MB private (WinUI, D3D textures cached per monitor size, sprite buffers, audio). Release GPU textures and large buffers when the loop idles; do not keep the settings window's visual tree alive when it is hidden, if that turns out to be the bulk.
Record numbers before and after in the PR and in `docs/manual-tests/M13.md`.

**Done when:** smoke snow run reports `Loop allocations 0 byte(s)` on a fast machine (180 Hz), and the PR states the measured budget numbers (fixing everything in F-03 may need follow-up PRs; do not block batch 5 on it).

### Batch 6 – Shell panels, visitor hit windows, foreign overlays (F-17, F-19, F-21)

**F-17.** `RuntimePolicy.ShellProcesses` relies on the foreground process. The clipboard history (Win+V) and emoji
panel (Win+.) belong to `TextInputHost.exe` and do not take the foreground. Detect a *visible, uncloaked*
`TextInputHost` window (class `Windows.UI.Core.CoreWindow`, cloak state via `DWMWA_CLOAKED`; the run saw it cloaked
while closed) and treat it like an open shell panel. Keep it process-name only (privacy invariant). Core test for the
policy; manual checklist item for both panels.

**F-19.** During a pause the full-screen overlay is removed, but visitor hit windows (`Sillfall.Overlay.*`, 44 × 40)
stay and follow an invisible visitor, catching clicks. Destroy or hide all hit windows when the decision is
"paused" (`OverlayHost.Visitors.cs`, `SyncHitWindows`), and recreate them on resume. Extend the smoke self-check
"Click-through" to run during a pause as well (no hit windows may exist).

**F-21.** Other apps' transparent click-through overlays (seen: the Claude desktop app's `claude.exe`
`Chrome_WidgetWin_1`, ex-style `WS_EX_LAYERED | WS_EX_TRANSPARENT | WS_EX_TOPMOST | WS_EX_NOACTIVATE`) are tracked as
windows and get weather edges. Filter windows with `WS_EX_TRANSPARENT` (combined with `WS_EX_LAYERED`) in
`WindowEnumerator`, next to the existing cloak/tool-window/own-overlay filters, and document it in an ADR-0004
amendment. Core test if the filter is expressed on a `WindowInfo` snapshot.

### Batch 7 – Settings UI (F-08, F-10, F-11, F-20)

- **F-08:** pages build their content once when shown. Subscribe the visible page to the controller's change events (runtime decision → pause banner; weather sync state → "sync paused" notice, which must also go away when sync is turned off; visitor collection → counts, cards, achievements, share card) and refresh on the UI thread. Unsubscribe when the page is left. Smoke tour: add a check that the banner text follows a pause/resume.
- **F-10:** the settings window shows solid black for ~0.2 s before its first frame. Show/activate it only after the first content render (e.g. after `Loaded` + first `CompositionTarget.Rendering`), or create it hidden and show it once ready. Re-measure with a fast screen capture loop (the run's method: capture every ~30 ms after launch and count near-black pixels).
- **F-11:** replace `.wscene` with `.sillscene` in `Templates.Import` in both `Resources.resw` files. Better: use a format placeholder filled from `SceneCodec.FileExtension`, so it cannot drift again. Add a localization test that no resource string contains `.wscene`.
- **F-20:** `CityIndex` search should rank an exact (case/diacritics-insensitive) main-name match first, then prefix matches of the main name, then alternate-name matches, then by population if available. Core test: `Berlin` → first result `Berlin (DE)`.

### Batch 8 – Tooling (F-14, F-15, F-16)

- **F-14:** `tools/Sillfall.Sandbox/SandboxWindow.cs` copies only `_engine.Frame.Sprites` into its own scene, so fog and god-rays never render. Pass the engine's fog/sun layers (and window occluders) the way FrameDump passes `capture.Scene`. Check G and U by hand afterwards.
- **F-15:** `smoke-test.ps1` `Invoke-SandboxRun` looks only for a Release Sandbox, but the script builds only the app. Build the Sandbox in the script (same configuration/platform) before running it, or document the Release build in CLAUDE.md and the test-run briefing. Prefer building it in the script.
- **F-16:** the settings tour captures (RenderTargetBitmap) come out with a white nav pane, washed-out grey content and white text; `flyout.png` shows no text. Draw an opaque theme background under the captured element, or capture the window via `Windows.Graphics.Capture` of the app's *own* window (allowed: it is not screen capture of other content, but check the privacy invariant test first and ask if unsure).

### Batch 9 – Questions for the maintainer (F-09, F-12, F-13)

Do not implement. Open one GitHub issue (or a section in the PR of batch 7) per question, with the evidence from the
findings file and 2–3 concrete options each. Ask the user to decide:

- **F-09 rain sound** sounds "like some plastic falling down". Options: different noise colouring/filtering of the procedural rain in `Sillfall.Audio`/Core audio code, denser and softer impacts, a CC0 recorded rain loop (would need an `ASSETS.md` row). Needs a human ear.
- **F-12 first start / Clear sky ☀️.** All intensities default to 0, so a first start shows nothing; "Clear sky" sets everything to 0 although its emoji is a sun. Options: first-run default (e.g. light rain), Clear sky with sunlight, or rename/re-emoji the preset.
- **F-13 sunlight barely visible** on the real desktop. ADR-0010 §4 already leaves this to sign-off. Options: stronger ray contrast over the desktop, a visible warm tint, or accept as subtle.

## 3. Final check after all batches

1. All commands from section 0 green; soak `pwsh tools/ci/soak-test.ps1 -Minutes 10` green, numbers recorded.
2. Re-run the affected parts of the desktop test run ([desktop-test-run.md](../../../desktop-test-run.md), sections 3–7) on a real PC, especially: weather steady with Notepad / the desktop in front (F-01, F-18), Win+R link (F-04), maximized browser in rain (F-05), user drags and snaps (F-07), Performance fps per smoothness (F-06), Win+V/Win+. (F-17), clicks where an invisible visitor was during a pause (F-19).
3. Update each finding's **Status** line and give the user a table: finding, PR, status (fixed / partly / open / waiting for decision).
