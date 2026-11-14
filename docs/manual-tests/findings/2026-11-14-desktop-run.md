# Desktop test run 2026-11-14

Setup: Windows 11 Home 25H2 (build 26200.9974), AMD Ryzen 5 5600, NVIDIA GeForce RTX 5080 (driver 32.0.16.2124),
one monitor 2560 × 1440 at 180 Hz, 100 % scaling (DISPLAY6, primary), AC power (desktop, no battery), commit 9d25d89.

Briefing: `desktop-test-run.md` (untracked, in the repository root; its links assume `docs/manual-tests/`).
Run artifacts (screenshots, logs, reports) are in `%TEMP%\sillfall-desktop-run\`; smoke outputs in
`%TEMP%\sillfall-smoke\` (copies of runs 1 and 2 in `%TEMP%\sillfall-desktop-run\smoke-run1|2\`).
Settings at start: the user's `settings.json` was moved to `%TEMP%\sillfall-desktop-run\backup\` (no `collection.json`
existed), so the pass ran with default settings.

## Summary
| ID | Severity | Area | Title |
|---|---|---|---|
| F-01 | high | System state | Weather blinks off and on every 2 s while another app is in front (self-triggered "full screen" pause) |
| F-04 | high | Startup | `sillfall://` links opened through Windows are silently ignored (`scene/?d=`) |
| F-05 | high | Rain | Near particles fall across window content where the top edge is not exposed (maximized, y = 0, covered) |
| F-07 | high | Tracking | Effects lag or are left behind when the user drags or snaps a window |
| F-02 | medium | Performance | Smoke test fails in the snow run: 4048 B allocated in the frame loop |
| F-03 | medium | Performance | CPU, frame time and memory above the PLAN.md budgets |
| F-06 | medium | Performance | Frame rate does not match the Smoothness setting on a 180 Hz monitor |
| F-17 | medium | System state | Weather not hidden over clipboard history (Win+V) and emoji panel (Win+.) |
| F-18 | medium | System state | No weather while the desktop itself is in front |
| F-19 | medium | Visitors | Invisible visitor hit window stays and moves while paused |
| F-21 | medium | Tracking | Other apps' transparent click-through overlays are tracked as windows |
| F-08 | low | Settings UI | Pages show stale state (pause/sync banners, collection) until reopened |
| F-10 | low | Startup | Settings window is solid black for ~0.2 s at start |
| F-11 | low | Settings UI | Templates button still says ".wscene" |
| F-14 | low | Fog | Sandbox does not draw fog or sun rays |
| F-15 | low | Startup (tooling) | Local smoke test cannot find the Sandbox after the documented Debug build |
| F-16 | low | Settings UI (tooling) | Smoke settings-tour pictures are hard to review |
| F-20 | low | Settings UI | City search ranks the exact match last |
| F-09 | question | Sound | Rain sound does not sound like rain |
| F-12 | question | Startup | First start shows no weather; "Clear sky ☀️" turns everything off |
| F-13 | question | Sun | Sunlight is hard to see on the real desktop |

## Automated baseline
build ✅ · Core tests ✅ (627) · Render tests ✅ (85) · smoke ❌ (snow run, F-02; all other runs ✅ when run separately, Sandbox step needs a Release build, F-15) · soak ✅ leak checks (10 min; CPU 9.3 % of a core, frame 22.3 ms, private memory 163→183 MB, see F-03) · FrameDump ✅ (18 scenes, contact sheets look as described)

Smoke screenshots reviewed: calibration pattern and corner markers ✅, excluded-from-capture ✅, surface debug lines on the exact rows/columns ✅, resting drops ✅, every effect's dry/wet pair ✅, Sandbox ✅; settings tour see F-16.

## Checked and fine
Single instance (second start exits 0, existing window comes to front) · `--background` starts tray-only with weather · tray quit removes the overlay at once, process gone, no ghost icon · 5× fast start/quit clean · settings (intensity, colour) persist across restarts · effects off → render loop idles (CPU 0–1 %) · no halos over light or dark windows (user) · fog looks fine on the desktop (user) · Sandbox smooth, no flicker with rain/snow/leaves/grass (user) · snow piles, sheds on shake, falls on minimize, nothing left floating · leaves land and are blown off by a cursor sweep (Sandbox) · grass grows and disappears with its window (Sandbox) · scripted fling/resize/minimize/maximize leave nothing behind · presets apply their documented values · visitor appears (Tuck), Ctrl+Alt+V collects, album and share card update after reopening the page · sound off by default, mute is instant · lock/unlock resumes without a burst · full-screen video/F11 pauses and resumes · reduced motion (animations off) pauses and resumes · high contrast pauses, UI readable · light/dark switch follows · 150 % scaling: overlay rebuilt at 144 DPI, all settings pages laid out without cut-offs · capture exclusion on/off works (captured rain pixels 412 → 62 → 377; the 62 were inside another app's thumbnails) · GPU reset (Win+Ctrl+Shift+B) recovers · app rule "pause while in the foreground" holds the pause (overlay 0 %), "hide over windows" keeps Notepad clear · autostart on writes the Run entry with `--background`, off removes it · error-report folder opens · keyboard focus visible, Narrator names every control (user) · weather sync: Berlin clear night → no weather per mapping, invert → rain, manual change pauses sync, attribution shown · no new crash report during the run.

## Findings

## F-01 – Weather blinks off and on every 2 s while another app is in the foreground (self-triggered "full screen" pause)
- **Severity:** high (could be critical: makes the weather unusable whenever any other app is active; see the open point under Actual)
- **Area:** System state
- **Steps to reproduce:**
  1. Default settings, then Effects → Rain intensity 50 % (any visible effect works).
  2. Quit Sillfall; start it with `Sillfall.App.exe --background` (or start normally and click any other window, e.g. an empty Notepad, not maximized).
  3. Watch the rain, or poll the overlay window (`Sillfall.Overlay.*`) and `SHQueryUserNotificationState` every 200 ms.
- **Expected:** weather runs continuously; it pauses only for a real full-screen app or presentation mode (ADR-0016 §1: "Own process fullscreen does not count (no self-pause)"; `SystemInfo.ScreenUse` doc comment: "the app cannot pause itself").
- **Actual:** the overlay is removed and recreated on a strict 2 s rhythm (16 changes in 30 s, visible 50–52 % of the time). The Effects page shows the banner "Paused while an app runs full screen". The user confirmed the rain visibly blinks. Always (3 of 3 measurements with another app in front).
  Measurements (`SHQueryUserNotificationState`, 200 ms samples):
  - Sillfall not running: state 5 (`QUNS_ACCEPTS_NOTIFICATIONS`) 100 % of 20 s.
  - Sillfall running, its own Settings window in front: shell reports `QUNS_BUSY` 100 %, but no pause (own-process guard), overlay 100 % visible.
  - Sillfall running, Notepad in front: shell `QUNS_BUSY` 50 %, overlay 50 %: busy exactly while the overlay exists.
  Side effects of the same loop: the "Paused while an app runs full screen" banner on the Effects/Templates pages
  appears and disappears with it and pushes the page content down by about one row, so clicks land on the wrong
  control (a click meant for the Snow header opened Rain). With sound on, the user heard the rain sound go silent
  every 2 s.
  Open point: the Claude desktop app's computer-use layer (a transparent, click-through, topmost, full-monitor `claude.exe` window) was on screen during the whole run. Without Sillfall it did not trigger `QUNS_BUSY`, but it was not possible to repeat the test without it. The maintainer should re-check on a clean desktop; if it reproduces there, severity is critical.
- **Evidence:** this session's measurement scripts (`osc.ps1`), results quoted above; banner seen on the Effects page; user observation "Yes, it blinks".
- **Also in Sandbox?** no (the Sandbox has no system-state logic).
- **Suspected location (guess):** `src/Sillfall.Platform/Shell/SystemInfo.cs` `ScreenUse()` treats `QUNS_BUSY` as full screen unless the foreground window is this process. The overlay itself (topmost, full monitor) appears to be what the shell reports as "busy", so a self-check is needed that ignores the app's own overlay windows (or verifies the foreground window really covers its monitor). `OverlayHost.UpdateScreenUse` (2 s timer) then removes the overlays (ADR-0016: "A pause removes overlay windows entirely"), which clears the state, which resumes: a feedback loop.

## F-02 – Smoke test fails in the snow run: 4048 B allocated in the frame loop
- **Severity:** medium
- **Area:** Performance
- **Steps to reproduce:**
  1. `pwsh tools/ci/smoke-test.ps1` on this PC (180 Hz monitor, 100 % scaling).
- **Expected:** `Loop allocations 0 byte(s)` (PLAN.md §4, CLAUDE.md rule 3; checked by `SmokeTest` for every weather run).
- **Actual:** `FAILED Loop allocations 4048 byte(s) in 147 measured frame(s)` (run 1) and `4048 byte(s) in 148 measured frame(s)` (run 2): same number both times, 2 of 2. The script stops there, so leaves/fog/sun/grass/visitors/settings/sandbox never run (they pass when run separately). Rain, drops, leaves, fog, sun, grass: 0 B.
- **Evidence:** `%TEMP%\sillfall-desktop-run\smoke.log`, `smoke2.log`, `smoke-run1\smoke-report-snow.txt`.
- **Also in Sandbox?** not applicable (Sandbox does not measure allocations).
- **Suspected location (guess):** `SnowEffect.FieldFor` creates a `SnowField` lazily the first time a flake lands on a new top edge. For the 1906 px smoke window with 2 px bins that is `new float[953]` (3836 B) plus the `SnowField` object and `_fields` dictionary growth, close to 4048 B. On this machine the first landing falls inside the measured frames; on CI it probably happens during warm-up, which would explain why CI is green. Rain's `WaterField` does not show it.

## F-03 – Soak: CPU, frame time and memory above the PLAN.md budgets
- **Severity:** medium
- **Area:** Performance
- **Steps to reproduce:** `pwsh tools/ci/soak-test.ps1 -Minutes 10` (all six effects at 40–60 %, visitors, window moved/resized every few seconds).
- **Expected:** PLAN.md §4: CPU < 3 % of one core, RAM < 150 MB, 60 fps (Auto smoothness on mains).
- **Actual:** leak checks pass (private memory +2.3 MB, managed heap +0.0 MB, handles +8, GDI 0, USER +2). But `CPU 9.3 % of a core, frame 22.3 ms` (≈ 45 fps, below the 60 fps cap) `simulation 0.92 ms, 11970 sprites`; private memory 162.9 MB at start, 182.6 MB at the end (`soak.csv`). Once.
  Real desktop, settings window open: all six effects at 100 %: CPU 19.3 % of one core, GPU 3D ≈ 0.8 % (RTX 5080),
  47 fps, simulation 3.57 ms, 20 568 sprites, private memory 287 MB. After switching everything off the loop idles
  (CPU 0–1 %), but private memory stays at 291 MB.
  Note: the soak also drives its own WinUI window (move and resize every few seconds), so the CPU figure includes UI work and is not a clean measurement of the budget scenario. It is still well above 3 %.
- **Evidence:** `%TEMP%\sillfall-desktop-run\soak\soak-report.txt`, `soak.csv`, `soak.log`.
- **Also in Sandbox?** not applicable.
- **Suspected location:** not investigated.

## F-04 – `sillfall://` links opened through Windows (Run, shell) are silently ignored
- **Severity:** high
- **Area:** Startup
- **Steps to reproduce:**
  1. Settings → Templates → "Save the current weather" (e.g. rain 50 %, own colour), then "Copy link" on the saved template.
  2. Apply another template (e.g. Clear sky) so the weather changes.
  3. Win+R, paste the link, Enter (same result with PowerShell `Start-Process 'sillfall://scene?d=…'`).
- **Expected:** the scene applies (briefing §3; PLAN.md §0 "Scenes … `sillfall://scene?d=…` links"; §7 "Single-instance + `sillfall://` protocol").
- **Actual:** nothing changes and no message is shown; the user saw only a busy cursor for about a second. The second instance receives the link as `"sillfall://scene/?d=…"`: Windows inserts a `/` after the host (command line captured with `Win32_Process`). Passing the original form directly (`Sillfall.App.exe "sillfall://scene?d=…"`) does apply the scene. Always (2 of 2 via shell).
- **Evidence:** captured command line `"…\Sillfall.App.exe" "sillfall://scene/?d=1VTBTgMh…"`; `settings.json` unchanged after the shell launch, changed after the direct launch.
- **Also in Sandbox?** not applicable.
- **Suspected location (guess):** `SceneCodec.TryFromLink` / `TryDecodeLink` check `link.StartsWith(LinkPrefix)` with `LinkPrefix = "sillfall://scene?d="`, so `scene/?d=` is rejected. `App.ApplySceneLink` then returns without any feedback (`out _` discards the reason). `SceneTests` only cover the exact prefix. A browser link will probably fail the same way.

## F-05 – Rain (and other near particles) falls across window content wherever a window's top edge is not exposed (maximized, at y = 0, or covered by another window)
- **Severity:** high (the user confirmed it on a maximized browser: "it rains through the window, it is visible on the screen, it should not")
- **Area:** Rain
- **Steps to reproduce:**
  1. Rain 50 %.
  2. Maximize a window, or move a normal window so its frame top is exactly 0 (Explorer at frame `337,0,1289,634`; a Notepad moved to `1450,0`).
  3. Keep Sillfall's settings window in front (see F-01) and watch the window's content.
- **Expected:** "Nothing drawn inside a window's client area where it should be occluded" (briefing §4). Distant drops fall behind windows (ADR-0006 §1); near drops should hit the window's top edge.
- **Actual:** near drops fall in front of the whole window, across its content, all the way down. The same Notepad at `y = 380` gets a puddle on its top edge and rivulets on its sides and no drops inside. Always.
  ADR-0004 says "A top edge on the monitor top (maximized window) has no slot and is dropped". So the drops have nothing to hit, but they are still drawn as "near". This affects every maximized window and every window snapped to the top of the screen (Snap Layouts), i.e. the most common layouts.
  The same happens below a window whose top edge is hidden by another window: with Explorer (at y = 0) covering the
  Settings window's top edge, rain fell across the whole Settings content. Free-falling gutter streams from the bottom
  corner of a window above run as long vertical lines straight across the content of the windows below (two such
  streams crossed the Settings window, lossless capture). Snow flakes and leaves behave the same (flakes over
  Explorer's content at y = 0).
- **Evidence:** screenshots during the run (Explorer and Notepad at y = 0 with rain over their content; Notepad at y = 380 without); `%TEMP%\sillfall-desktop-run\rain\max-all-settings.png` (lossless; rivulet streams and drops over the Settings content). Note: this capture also shows parts of other windows, so do not commit it.
- **Also in Sandbox?** not tried (Sandbox windows never touch the top).
- **Suspected location:** `RainEffect` near-drop handling combined with the dropped top-edge slot from `SurfaceExtractor` (ADR-0004 table), and the free fall of gutters below a window (ADR-0006 §3 "At the window bottom they fall freely"). Near particles are only stopped by exposed top edges; they are never occluded by the windows they pass in front of. Same in `SnowEffect`/`LeavesEffect` near layers.

## F-06 – Frame rate does not match the Smoothness setting on a 180 Hz monitor
- **Severity:** medium
- **Area:** Performance
- **Steps to reproduce:**
  1. 180 Hz monitor, rain 50 %, Settings → Performance, look at "Right now".
  2. Switch Smoothness between Auto, Least energy and Smoothest.
- **Expected:** Auto/Balanced 60 fps on mains, Least energy 30 fps, Smoothest "the display's rate" (180 fps) (PLAN.md §3, Performance page texts).
- **Actual:** Auto: 48–52 fps ("19.6 ms per frame, … Frame rate limit: 60 fps"). Least energy: 30 fps (correct). Smoothest: 64 fps, 15.5 ms per frame ("Frame rate limit: the display's rate"). In the smoke runs the same build measured 65 fps in one run and 182 fps in another (rain, grass), i.e. it depends on something outside the app.
- **Evidence:** Performance page readings; `smoke.log` (rain 65 fps) vs `smoke2.log` (rain 182 fps).
- **Also in Sandbox?** not applicable.
- **Suspected location (guess):** `OverlayHost.RenderAnimationFrame` waits with `Thread.Sleep(1)` until `0.92 / cap` (or `MinimumFrameSeconds`) has passed. With the default 15.6 ms timer resolution `Sleep(1)` sleeps ~15.6 ms, so "Smoothest" ends up at 64 fps and the 60 fps cap overshoots to the next 180 Hz vblank (~50 fps). When another app has raised the timer resolution, `Sleep(1)` is 1 ms and the rate reaches 182 fps. A waitable timer (high resolution) or pacing on the swap chain's frame latency waitable object would make it independent.

## F-07 – Effects attached to a window lag behind it, or are left behind, when it is dragged or snapped by the user
- **Severity:** high
- **Area:** Tracking
- **Steps to reproduce:**
  1. All six effects at maximum (Effects page), Smoothness Auto, 180 Hz monitor.
  2. Drag a window with puddles and rivulets on it around with the mouse.
- **Expected:** "the effect follows without lag or a trail of stray particles at the old position" (briefing §5); ADR-0006 "Move carries gutters along".
- **Actual (user's words):** "When you move the window around, the effects (raindrops that are already on the window or rolling down) jerk around and can't keep up with the window. It feels like it's not running at 60+ FPS." The app reported 47 fps, 20.9 ms per frame, simulation 3.57 ms, 20 568 sprites at that moment (see F-06 for the frame pacing).
  Repeated later at normal intensity (rain ~20–50 %): the user moved and snapped a Notepad with the mouse and keyboard (Win+Left, Win+Up, Snap Layouts, Aero Shake) and reported: "the effects like puddles and rain were left behind, when I was moving the window".
  By contrast, moving the same Notepad from a script with `SetWindowPos` (a 0.35 s fling across the screen, resizes, minimize/restore/maximize) left nothing behind: the puddle travelled with the window and nothing stayed at the old position (lossless captures). So the problem seems specific to the real interactive move/size loop (and snap animations), not to position changes as such.
- **Evidence:** user observations; Performance page reading; `%TEMP%\sillfall-desktop-run\tracking\fling-*.png`, `resize-1.png`, `restore-*.png` (scripted moves, no leftovers).
- **Also in Sandbox?** no: the user rated the Sandbox with rain, snow, leaves and grass on (window drags included) "Smooth, no flicker". So this is a tracking/overlay problem (window position updates vs. render rate), not an effect problem.
- **Suspected location:** not investigated. Candidates: the `SetWinEventHook` location-change coalescing in `WindowEnumerator`/`WindowMotionTracker` during the modal move loop (scripted `SetWindowPos` moves are fine), DWM snap animations (the frame bounds animate after the final position), and F-06 (the loop runs at ~48 fps instead of the display rate). F-01 was active during the user's test (Notepad in front), so the 2 s overlay rebuilds may add to it. Re-check after F-01 is fixed.

## F-08 – Settings pages show stale state (pause banner, visitor collection) until the page is opened again
- **Severity:** low
- **Area:** Settings UI
- **Steps to reproduce:**
  1. Trigger a pause (F-01: any other app in front), then bring the settings window to the front so the weather runs again.
  2. Watch the "Paused while an app runs full screen" banner on the Effects page.
  3. Visitors page open, "Invite a visitor now", collect it with Ctrl+Alt+V.
- **Expected:** the page reflects the current state (ADR-0012: `AppController` is the single source).
- **Actual:** the banner stayed for more than 10 s while the overlay was verifiably running (overlay 100 % visible, Sillfall in front). It went away only after switching to another page and back. After the collect, `collection.json` held Tuck × 1 / 25 points / achievement FirstGuest, but the open page still said "0 collected, 0 points" and "Tuck – Not met yet" until the page was opened again. Always (2 of 2).
  Also: after turning Weather sync off, the "Weather sync paused – Rain keeps the value you set by hand – Resume sync"
  notice stayed on the page although sync was off.
- **Evidence:** `collection.json` contents vs. page text (screenshots during the run).
- **Also in Sandbox?** not applicable.
- **Suspected location:** the settings pages in `SettingsWindow.xaml.cs` build their content when a page is shown and do not subscribe to the runtime decision / collection changes.

## F-09 – Rain sound does not sound like rain
- **Severity:** question
- **Area:** Sound
- **Steps to reproduce:** rain 50 %, Settings → Sound → Sound on (default volumes).
- **Expected:** "Procedural rain" (PLAN.md §8), rain sound follows intensity (briefing §4).
- **Actual (user's words):** "it doesn't sound like rain, but like some plastic falling down". The 2 s dropouts the user also heard are F-01. Mute is instant, and sound is off by default (both OK).
- **Evidence:** user observation.
- **Also in Sandbox?** not applicable (Sandbox has no audio).
- **Suspected location:** `src/Sillfall.Audio` procedural rain synthesis (not investigated). Needs a maintainer's ear and a sign-off on what it should sound like.

## F-10 – Settings window shows as a solid black rectangle for ~0.2 s at start
- **Severity:** low
- **Area:** Startup
- **Steps to reproduce:** quit Sillfall, start `Sillfall.App.exe` (no arguments), capture the screen every ~30 ms.
- **Expected:** no black or white rectangle at start (briefing §3).
- **Actual:** from 1.27 s to 1.48 s after launch the settings window (1040 × 760 at 192,184) is pure black with only the caption buttons, then the dark Mica UI appears. No white flash, nothing drawn in a wrong place. The overlay window exists from 0.6 s and draws nothing (default settings have no weather). Once (single measured start).
- **Evidence:** `%TEMP%\sillfall-desktop-run\startup-flash\040-01388ms.png` and neighbours (black ≈ 20 % of the screen). The capture also shows other windows, so do not commit it.
- **Also in Sandbox?** not applicable.
- **Suspected location:** WinUI first frame before the backdrop/content is ready; showing the window after the first `Rendering`/`Loaded` would hide it. Not investigated.

## F-11 – Templates button still says ".wscene" (both languages)
- **Severity:** low
- **Area:** Settings UI
- **Steps to reproduce:** Settings → Templates.
- **Expected:** `.sillscene` (PLAN.md §0, `SceneCodec.FileExtension`).
- **Actual:** button "Import a .wscene file…" / ".wscene-Datei importieren…". The file picker itself filters `.sillscene` correctly. Always.
- **Evidence:** `src/Sillfall.Core/Localization/Strings/en-US/Resources.resw:202`, `de-DE/Resources.resw:202`; `settings-01-templates.png` from the smoke tour.
- **Also in Sandbox?** not applicable.
- **Suspected location:** the two `.resw` lines above (leftover from an earlier product name).

## F-12 – First start shows no weather; the "Clear sky ☀️" template turns everything off
- **Severity:** question
- **Area:** Startup
- **Steps to reproduce:** start with no `settings.json`; or Templates → Clear sky.
- **Expected:** unclear. The briefing measures "time to the first visible weather" at a cold start, and "every scene preset … looks like its name".
- **Actual:** all effect intensities default to 0, so a first start shows nothing but the settings window. "Clear sky" (☀️) is a scene with no effects at all (`ScenePresets.cs`), so it switches the weather off, including sunlight; its emoji suggests sun. Is "no weather" the intended first-run experience, and should Clear sky have sunlight?
- **Evidence:** `AppSettings`/`EffectConfig` defaults; `settings.json` after Clear sky: all intensities 0.
- **Also in Sandbox?** not applicable.
- **Suspected location:** `src/Sillfall.Core/Scenes/ScenePresets.cs` (Clear sky), default `EffectConfig.Intensity`.

## F-13 – Sunlight is hard to see on the real desktop
- **Severity:** question
- **Area:** Sun
- **Steps to reproduce:** Sunlight at 100 % alone, or the Spring morning template (sun 0.7, grass, fog 0.2).
- **Expected:** "God-rays come from a plausible direction, windows cast shadows" (briefing §4; ADR-0010 §4).
- **Actual:** in lossless captures only a faint glow and dust motes are visible; no clear rays or window shadows. The user could not judge it ("can not see clearly … looks good I guess"). ADR-0010 already flags "Whether that reads warm rather than milky on real desktops is for sign-off". Raised for that sign-off.
- **Evidence:** `%TEMP%\sillfall-desktop-run\presets\5-spring-morning.png`; smoke `desktop-sun.png`.
- **Also in Sandbox?** cannot be checked there (F-14).
- **Suspected location:** `AtmospherePass` sun strength / alpha (ADR-0010 §4). Not investigated.

## F-14 – Sandbox does not draw fog or sun rays (G and U keys)
- **Severity:** low
- **Area:** Fog
- **Steps to reproduce:** start `tools/Sillfall.Sandbox`, press G (fog) or U (sun) and wait 30 s.
- **Expected:** the Sandbox "is the reference for how an effect should look" (briefing §2) for every effect.
- **Actual:** the title says "fog on" / "sun on", but no fog appears at all; sun shows only dust sprites. Always.
- **Evidence:** Sandbox screenshots during the run; code below.
- **Also in Sandbox?** yes (this is the Sandbox).
- **Suspected location:** `tools/Sillfall.Sandbox/SandboxWindow.cs`: the render loop copies only `_engine.Frame.Sprites` into its own `_scene` and never passes the fog/sun layers. FrameDump passes `capture.Scene` and does render them.

## F-15 – Local smoke test cannot find the Sandbox after the documented build
- **Severity:** low
- **Area:** Startup (tooling)
- **Steps to reproduce:** `dotnet build Sillfall.slnx -p:Platform=x64` (Debug, as in the briefing and CLAUDE.md), then `pwsh tools/ci/smoke-test.ps1`.
- **Expected:** the smoke test's last step (sandbox screenshot) runs.
- **Actual:** `Sillfall.Sandbox.exe was not found. Build the solution first.` `Invoke-SandboxRun` only looks for a Release build, and the script builds only the app, not the Sandbox. CI builds Release first, so it only fails locally. After `dotnet build tools/Sillfall.Sandbox -c Release -p:Platform=x64` it passes.
- **Evidence:** `%TEMP%\sillfall-desktop-run\smoke-rest.log`.
- **Also in Sandbox?** not applicable.
- **Suspected location:** `tools/ci/smoke-test.ps1` `Invoke-SandboxRun` (or the briefing's build command).

## F-16 – Smoke settings-tour pictures are hard to review (white text on a washed-out background)
- **Severity:** low
- **Area:** Settings UI (tooling)
- **Steps to reproduce:** `pwsh tools/ci/smoke-test.ps1`, open `%TEMP%\sillfall-smoke\settings\*.png`.
- **Expected:** pictures that show the pages as the user sees them (briefing §1: "the settings tour … look at it").
- **Actual:** the navigation pane is blank white, page content is light grey with white text, and `flyout.png` shows no text at all. The live window looks correct (dark theme, readable, Mica), so this is a capture artifact: probably `RenderTargetBitmap` over a transparent Mica background, with dark-theme text.
- **Evidence:** `%TEMP%\sillfall-desktop-run\smoke-run2\settings\` (copy) vs. live screenshots.
- **Also in Sandbox?** not applicable.
- **Suspected location:** `RunSettingsTourAsync` in `App.xaml.cs`; draw an opaque theme background under the capture.

## F-17 – Weather is not hidden over the clipboard history (Win+V) and the emoji panel (Win+.)
- **Severity:** medium
- **Area:** System state
- **Steps to reproduce:** rain on, Advanced → "Hide the weather while Start, search or notifications are open" on (default). Open and close each of: Start (Win), search (Win+S), notifications (Win+N), quick settings (Win+A), clipboard (Win+V), emoji panel (Win+.).
- **Expected:** weather steps aside while each is open and comes back afterwards (briefing §6, `HideOverShell`).
- **Actual:** Start, search, notifications and quick settings: hidden and back (log: foreground `SearchHost`, `ShellExperienceHost`, `ShellHost` → overlay removed; back on close). Clipboard history and emoji panel: the rain kept falling over them (user). Always (once each).
- **Evidence:** user observation; `%TEMP%\sillfall-desktop-run\log-shell.txt` (overlay/foreground log, 200 ms).
- **Also in Sandbox?** not applicable.
- **Suspected location (guess):** the shell process list in `OverlayHost`/`SystemInfo` (ADR-0016 §1: `StartMenuExperienceHost`, `SearchHost`, `ShellExperienceHost`, …). The clipboard and emoji panels belong to `TextInputHost.exe`, which does not take the foreground. So a foreground-process check cannot see them; a visible-window check for `TextInputHost` would be needed.

## F-18 – No weather while the desktop itself is in front (empty virtual desktop, after minimizing the last window)
- **Severity:** medium
- **Area:** System state
- **Steps to reproduce:**
  1. Rain on. Win+Ctrl+D (new, empty virtual desktop).
  2. Wait; then open Explorer; then minimize it again (desktop in front).
- **Expected:** weather on the empty desktop (it falls onto the taskbar floor); nothing from desktop-1 windows.
- **Actual:** no weather at all while the desktop is in front: log `overlay=0 fg=explorer` for 7.6 s right after Win+Ctrl+D, and for 21 s after Explorer was minimized. The rain came back only when the user returned to Sillfall's settings. With Explorer open it ran in the F-01 2-second cycle. No edge effects of desktop-1 windows showed on desktop 2 (good). User: "When no window open, nothing … I minimized the window – it disappears, I opened the window – it didn't show up."
- **Evidence:** `%TEMP%\sillfall-desktop-run\log-shell.txt` lines 22:59:13–22:59:52; user observation.
- **Also in Sandbox?** not applicable.
- **Suspected location (guess):** probably the same full-screen misdetection as F-01. With the desktop (`Progman`/`WorkerW`, process explorer) in front, the shell state is "busy" whenever the overlay exists, so the pause never ends. The same would apply to Win+D ("show desktop"). Re-check after F-01.

## F-19 – A visitor's invisible hit window stays and moves while the weather is paused
- **Severity:** medium
- **Area:** Visitors
- **Steps to reproduce:**
  1. Visitors → "Pick up visitors and drop them into the box" on; "Invite a visitor now".
  2. Cause a pause (another app in front, F-01; or any real pause such as an app rule).
  3. Enumerate Sillfall's top-level windows.
- **Expected:** "A pause removes overlay windows entirely" (ADR-0016 §1). Clicks next to (and certainly without) a visitor reach the window below (briefing §4, Visitors).
- **Actual:** during the paused phase the full-screen overlay is gone, but a 44 × 40 `Sillfall.Overlay.*` window remains near the taskbar and keeps moving (x 2051 → 2032 → 1983 within 2 s). This is the visitor's hit window following an invisible visitor, so it catches clicks with nothing drawn there. Seen on every sample over several minutes.
- **Evidence:** window enumeration (`1118014 Sillfall.Overlay… 2051,1352 44x40` while the 2560×1440 overlay was absent); `log-shell.txt` from 23:01:56 on (count alternates 1/2 instead of 0/1).
- **Also in Sandbox?** not applicable.
- **Suspected location:** `OverlayHost.SyncHitWindows` / visitor hit windows (ADR-0014) are not torn down on pause.

## F-20 – City search ranks the exact match last
- **Severity:** low
- **Area:** Settings UI
- **Steps to reproduce:** Weather sync on → City search, type `Berlin`.
- **Expected:** "Berlin (DE)" first.
- **Actual:** suggestions in this order: Marzahn (DE), Friedrichshain (DE), Charlottenburg (DE), Kreuzberg (DE), Neukölln (DE), Burlington (CA), North Canton (US), Berlin (DE). The exact match is the last item. Sync itself worked: "Berlin (DE) – Clear sky, 2 °C, wind 3 m/s from the south", effects matched `docs/weather-mapping.md` (clear night → no rain/sun, south wind → 0, no leaves in November below 4 m/s), Invert gave rain, a manual slider change showed "Weather sync paused", attribution shown.
- **Evidence:** screenshot during the run.
- **Also in Sandbox?** not applicable.
- **Suspected location:** `CityIndex` search ranking (alternate names probably score as high as the main name).

## F-21 – Transparent click-through overlay windows of other apps are tracked as normal windows
- **Severity:** medium
- **Area:** Tracking
- **Steps to reproduce:**
  1. Have another app's transparent, click-through, topmost window on screen (here the Claude desktop app's computer-use layer, `claude.exe`, class `Chrome_WidgetWin_1`, ex-style `0x8280028` = `WS_EX_LAYERED | WS_EX_TRANSPARENT | WS_EX_TOPMOST | WS_EX_NOACTIVATE | WS_EX_NOREDIRECTIONBITMAP`).
  2. Change display scaling 100 % → 150 % → 100 %. That layer stayed at 0,0 1707 × 960 (the 150 % size) instead of covering the monitor.
  3. Rain on.
- **Expected:** windows that are invisible and click-through to the user do not get weather (ADR-0004: cloaked windows, tool windows and own overlays are filtered; the same reasoning applies to other apps' overlays: game/recording overlays, NVIDIA overlay, screen-border highlights).
- **Actual:** the user saw "a phantom window at the left top corner in the shape of the 150 % scaled monitor": weather treats the invisible 1707 × 960 rectangle as a window (rain falls through it as in F-05; edges drawn where nothing is visible). Before the scaling change the same layer covered the whole monitor, so it was probably also taken into account as a full-screen window all along.
- **Evidence:** window enumeration (`2558934 claude Chrome_WidgetWin_1 0,0 1707x960`, ex-style `0x8280028`); `%TEMP%\sillfall-desktop-run\tracking\phantom-edge.png`.
- **Also in Sandbox?** not applicable.
- **Suspected location (guess):** `WindowEnumerator` filters: add `WS_EX_TRANSPARENT` (+ `WS_EX_LAYERED`) windows of other processes, since they can't be clicked and usually are fully transparent overlays.

---

Not tested: second monitor, monitors with different scaling, window spanning monitors, plug/unplug, orientation and primary-monitor changes (one monitor only); battery behaviour (desktop PC); Remote Desktop; presentation mode; sleep/wake (only lock/unlock); a full-screen game; all eight visitors with their weather (only Tuck appeared) and dragging a visitor into the box with the mouse (only keyboard collect); visitor click-through by hand; Chromium/UWP/console/Task Manager windows, tooltips and menus individually; German UI at 150 %; budget-scenario CPU measurement without the settings window (only soak and all-at-max). Much of section 5–6 was confounded by F-01 (any other app in front makes the weather blink) and done with the Sillfall window in front or by script.
