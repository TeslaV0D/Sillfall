# Sillfall – implementation plan

> Goal: a native Windows utility that renders animated weather (rain, snow, fog, leaves, sunlight, grass)
> **on** open window edges. Weather interacts with window geometry. Clicks pass through. Optional sync
> follows real-world conditions.

This document is the working plan for contributors and coding agents. Each milestone has scope, technical
notes, tests, and a definition of done (DoD). Milestones run in order. One milestone → one branch → one PR
with green CI.

---

## 0. Feature inventory

| Area | Behavior |
|---|---|
| Rain | Drops, splashes, puddles on tops, rivulets on sides, corner sheets in heavy rain |
| Snow | Settles on tops, slides off on close/move/shake, melts when warm |
| Fog | Wind-driven fBm noise with domain warping, low-res fullscreen pass |
| Leaves | Flutter, land on windows, shake scatters, cursor gusts |
| Sunlight | Occluder mask from window rects + radial-blur god-rays |
| Grass | Grows on tops, faster in rain, vertex sway in wind |
| Per effect | Intensity, color, brightness, Fixed / Random with rate |
| Scenes | Named templates with emoji; `.sillscene` files and `sillfall://scene?d=…` links |
| Weather sync | MET Norway + bundled city list + mapping table; invert; per-effect blend; manual override pauses sync |
| Visitors | Original characters on edges; drag into a box for points; local album; PNG share card |
| Sound | Procedural rain + short samples; per-category volume; default off |
| System | Per display, per-app pause/hide, battery options, smoothness, exclude from capture |
| Accessibility | Narrator/UIA, reduced motion, high contrast, keyboard collect |
| Privacy | Geometry only — no titles, contents, analytics, or screen recording |

**Windows extras (bonus):** taskbar as a ground surface, snap-layout awareness, Mica UI.

### IP rules (hard)

- Product name is **Sillfall**. Keep display strings on `AppInfo.Name`.
- No third-party weather-app characters, names, art, sounds, copy, or screenshots.
- Assets are original (SVG/procedural) or CC0; every source is listed in `ASSETS.md`.

---

## 1. Tech stack

### Choice: C# / .NET 10 + Direct3D 11 + DirectComposition + WinUI 3

| Option | Pros | Cons |
|---|---|---|
| **A: C# .NET 10, Vortice, CsWin32, WinUI 3** ✅ | Fast iteration; core logic platform-neutral → **tests on Linux** | Higher RAM (~80–120 MB); GC discipline in the frame loop |
| B: C++20 + WinUI 3 | Max performance | Slower iteration; hard to test core on Linux |
| C: Rust + windows-rs | Safe and fast | Awkward WinUI / DComp path |

GPU-bound work dominates. CPU simulation (≤ 20k particles) stays under ~1 ms with `System.Numerics` SIMD.
~60% of the code (simulation, geometry, weather mapping, settings) builds and tests off Windows.

### Core libraries

- Vortice: D3D11, DXGI, DirectComposition, Direct2D, XAudio2, D3DCompiler
- Microsoft.Windows.CsWin32
- Microsoft.WindowsAppSDK (WinUI 3)
- Tray: `H.NotifyIcon.WinUI` or `Shell_NotifyIcon` wrapper
- Tests: xUnit, AwesomeAssertions, Verify

---

## 2. Architecture

```
┌──────────────────────── Weatherly.App (WinUI 3) ────────────────────────┐
│ Tray flyout · Settings · Scenes · Onboarding · Composition root         │
└───────────────┬───────────────────────────────┬───────────────────────────┘
                │                               │
┌───────────────▼──────────────┐  ┌─────────────▼────────────┐  ┌───────────▼─────────────┐
│ Weatherly.Platform (Win32)   │  │ Weatherly.Render (D3D11) │  │ Weatherly.Audio          │
│ Overlay per monitor          │  │ Device / swapchain / DComp│  │ XAudio2, rain synth     │
│ WindowTracker, monitors, DPI │  │ Pipelines per effect     │  └──────────────────────────┘
│ Power / fullscreen / cursor  │  │ D2D for visitors / text  │
└───────────────┬──────────────┘  └─────────────▲────────────┘
                │ WindowSnapshot                │ RenderFrame
                └────────────────► Weatherly.Core (net10.0)
                                  SurfaceExtractor · Simulation · Effects · Weather sync · Scenes · Visitors
```

### Data flow per frame

1. `WindowTracker` → `WindowSnapshot` (event-driven, 10 Hz poll fallback).
2. `SurfaceExtractor` → visible horizontal/vertical edge segments (+ taskbar ground).
3. Simulation at fixed `dt = 1/120 s`, deterministic with seed.
4. Immutable `RenderFrame` → per-monitor overlay draw.
5. Idle (effects off / paused / fullscreen) **stops the render loop** (0% GPU).

---

## 3. Critical Windows technique

### Overlay (one window per monitor)

- Styles: `WS_POPUP`; ex: `WS_EX_LAYERED | WS_EX_TRANSPARENT | WS_EX_TOPMOST | WS_EX_TOOLWINDOW | WS_EX_NOACTIVATE | WS_EX_NOREDIRECTIONBITMAP`.
- D3D11 → composition swapchain (`DXGI_ALPHA_MODE_PREMULTIPLIED`, flip sequential, latency waitable).
- Premultiplied alpha; clear `(0,0,0,0)`.
- DPI: PerMonitorAwareV2; physical pixels internally.
- Optional `WDA_EXCLUDEFROMCAPTURE`. Prefer iGPU via `MINIMUM_POWER`. Device-lost rebuilds GPU only.
- **Never** sample the desktop framebuffer.

### Window tracking

- `EnumWindows` + DWM extended frame bounds; cloak / toolwindow / own overlays filtered.
- `SetWinEventHook` (out-of-context) with coalescing.
- **Never** `GetWindowText`. Per-app rules use process image name only.
- Shake: ≥ 3 direction changes in 600 ms with amplitude > 20 px.

### System states

Pause for fullscreen / presentation (`SHQueryUserNotificationState`), lock / sleep / RDP, Start/Search when configured.
Smoothness: Least Energy / Balanced / Smoothest / Automatic.

---

## 4. Effects (summary)

All effects implement `IEffectSystem` (`Configure` / `Step` / `OnSurfaceEvent` / `Emit`). Shared wind field.

Budgets (iGPU, 1440p, 60 fps, all effects ~50%): CPU < 3% of one core, sim < 1.0 ms p95, GPU frame < 2.5 ms p95,
RAM < 150 MB, **0 B** allocations in the frame loop, idle ≈ 0 frames.

Details: ADRs 0006–0011 and Core effect classes.

---

## 5. Weather sync

- Provider: MET Norway `locationforecast/2.0/compact` (CC BY 4.0, required User-Agent, honor cache headers).
- Behind `IWeatherProvider`. Location: optional geolocation (rounded to 1 decimal) or offline GeoNames cities.
- Mapping: `docs/weather-mapping.md`, unit-tested.
- Invert, custom blend weights, manual override pauses per element. Attribution in About / Sync settings.

---

## 6. Visitors

Eight original characters (SVG → Direct2D). State machine in Core. Optional interactive hit windows + collection box.
Keyboard / Narrator collect path. Local album under `%LOCALAPPDATA%\Sillfall\collection.json`. Share cards via D2D.
Leaderboards: not in v1.

---

## 7. UI

Tray flyout + Settings (NavigationView): Effects, Templates, Weather sync, Visitors, Sound, Displays, Apps,
Performance, Advanced, About. Settings in `%LOCALAPPDATA%\Sillfall\settings.json` with migrations.
Single-instance + `sillfall://` protocol.

---

## 8. Audio

XAudio2 mastering + category submixes. Procedural rain. Default: muted.

---

## 9. Testing strategy

1. Core unit tests (Linux + Windows).
2. Deterministic simulation snapshots.
3. WARP golden images on Windows CI + FrameDump previews artifact.
4. Sandbox app for manual iteration.
5. Manual checklists in `docs/manual-tests/`.

---

## 10. CI/CD

- `core` (ubuntu): format, cross-platform build, Core tests
- `windows` (windows-latest): full solution, WARP render tests, smoke test, preview artifact
- `soak`: short soak each push; longer via workflow_dispatch
- Future `package` (tags): MSIX build + signing (M14)

---

## 11. Milestones

| # | Milestone | Size | Contents | DoD |
|---|---|---|---|---|
| **M0** | Foundation | S | Solution, CsWin32, Vortice, CI, docs | CI green, empty WinUI app builds |
| **M1** | Overlay spike | M | Per-monitor DComp overlay, click-through, DPI, capture exclusion | Manual M1 checklist |
| **M2** | Tracking & surfaces | M | WindowTracker, SurfaceExtractor, shake/velocity | ≥ 30 occlusion tests |
| **M3** | Engine & tooling | M | Fixed timestep, idle stop, Sandbox, FrameDump, goldens | Previews + 0 allocs in loop |
| **M4** | Rain | L | Full rain system + procedural sound | Goldens + budgets |
| **M5** | Snow | M | Heightfield, repose, shed, melt | Mass conservation tests |
| — | **MVP cut** | | M0–M5 + minimal tray | Daily usable |
| **M6** | Leaves | M | Flutter, land, cursor gust, shake | Tests + previews |
| **M7** | Fog + sun | M | fBm fog, god-rays | Goldens + GPU budget |
| **M8** | Grass | S | Growth, rain boost, sway | Tests + previews |
| **M9** | Full UI | L | All settings pages, scenes, per-display/app, smoothness | Live + persistent settings |
| **M10** | Weather sync | M | MET, location, cities, mapping, invert, blend | Mapping tests + attribution |
| **M11** | Visitors | L | 8 characters, collect, album, share, sounds | Drag/collect without click-through regression |
| **M12** | A11y & localization | M | Narrator, HC, reduced motion, EN/DE `.resw` | No critical a11y findings |
| **M13** | Hardening | M | Fullscreen/lock/RDP/Start, crash logs, soak | Soak + budgets on reference hardware |
| **M14** | Release | M | MSIX x64/ARM64, Store listing, privacy policy, winget, signing, trademark check | Store certification |

**Current:** M13 complete in code; manual soak/checklist on real Windows still open. **Next:** M14.

---

## 12. Working rules

1. One milestone = one branch = one PR.
2. Core stays Windows-free (architecture test).
3. No allocations in the frame loop.
4. Privacy invariants tested.
5. Every asset listed in `ASSETS.md`.
6. Escalate uncertainty; add manual checklists for Windows-only behavior.
7. Non-trivial decisions → ADR in `docs/adr/`.

---

## 13. Risks

| Risk | Mitigation |
|---|---|
| Overlay above Start / notifications / UAC | Detect foreground class → hide; UAC is a separate desktop |
| Fullscreen games / anti-cheat | Pause and hide on exclusive fullscreen; out-of-context hooks only |
| Click-through vs visitors | Separate mini hit windows |
| Mixed DPI / negative monitor coords | Physical pixels + multi-monitor unit tests |
| Battery use | iGPU preference, idle stop, auto smoothness, low-res passes |
| Weather API license | MET Norway; swappable provider |
| Name conflict | Product name **Sillfall**; verify Store / trademark before M14 ship |
