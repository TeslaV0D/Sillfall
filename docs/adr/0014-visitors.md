# ADR-0014: Visitors

- Status: accepted
- Date: 2026-11-12
- Milestone: M11

## Context

PLAN.md, section 6: Original characters visit the desktop, walk on windows, react to weather, and can be collected (opt-in). Style and roster were approved: flat-geometric, eight characters each with a favorite weather.

| Character | What | Favorite weather | Rarity |
|---|---|---|---|
| Pip | Snail with leaf hat | Rain | common |
| Tuck | Penguin with scarf | Snow | occasional |
| Bristle | Hedgehog with leaves | Leaves | common |
| Lumen | Firefly (hovers) | Fog | rare |
| Sol | Cat with sunglasses | Sun | occasional |
| Drift | Cloud on legs | Wind | common |
| Sprout | Potted plant on feet | Grass | occasional |
| Nim | Mouse with umbrella | any weather | common |

## Decisions

### 1. Behavior as an effect

`VisitorSystem` is an `IEffectSystem`. It runs like weather effects, with no allocations and at most three characters at once. States:

- Appear,
- Walk,
- Stand,
- Airborne,
- Held,
- Offended,
- Leave.

Behavior:

- **On windows:** A character is positioned relative to its window and moves when the window moves. Walking only on the visible part of the top edge.
- **End of edge:** Jumps in an arc to a window in range (150 px sideways, 90 px up) or turns around. Landing uses the same search rain and snow use.
- **Window closed or shaken:** Character falls and lands on the next window or the taskbar.
- **Weather:** A prop matches the strongest weather (umbrella, snowball, sunglasses, lantern, leaf). In favorite weather, no prop: happy hop.
- **Visit length:** After 40–90 seconds the character leaves on its own.

### 2. Scheduling with wall clock

The render loop sleeps when nothing is visible (ADR-0005). `VisitorScheduler` therefore uses the host clock.

- **Flow:** In idle, the host asks whether a visit is due and wakes the loop.
- **Intervals:** Rare 30 min, normal 10 (default), often 3, each ±50%. “Often” allows two characters at once.
- **Who appears:** Weighted by rarity. Favorite weather makes a character up to 4× more likely.

### 3. Data instead of draw commands

Simulation outputs one `VisitorPose` per character in `RenderFrame`:

- position,
- size,
- facing,
- tilt,
- squash,
- bob,
- step phase,
- blink,
- prop,
- expression,
- opacity.

D2D renderer and hit windows need no simulation code.

### 4. Collection

`VisitorAlbum` is stored locally in `collection.json` beside settings, never on the network. Code name “Album” because the analyzer reserves type names ending in `…Collection` for lists.

- **Points:** 10, 25, or 50 by rarity, double in favorite weather.
- **Combo:** A collect within one minute after the last raises the factor by 0.5, cap ×3.
- **Achievements:**
  - first guest,
  - ten visitors,
  - all eight,
  - the rare one,
  - in favorite weather,
  - five weather types,
  - three in a row,
  - 100 points.

### 5. Drawing with Direct2D’s SVG renderer

Characters are SVGs with named parts (`eyes`, `foot-left`, `foot-right`). `VisitorPainter` splits them on first use into separate `ID2D1SvgDocument`s and animates parts:

- Feet alternate when walking.
- Body bobs.
- Eyes blink.
- Squash and tilt on jump.
- Mirrored by walk direction.

Faces (arc eyes, brows) and sunglasses are small SVGs from eye positions (`VisitorArt`). No sprite sheets or custom geometry code; characters stay sharp at any DPI. Direct2D starts on first visitor.

### 6. Hit windows and basket

The overlay stays click-through.

- **Hit windows:** In interactive mode (opt-in), each visitor gets a small window. Layered with alpha 1/255: invisible but clickable (alpha 0 would pass clicks). Topmost, never activated.
- **Tracking:** Hit windows use the overlay class prefix, so window tracking ignores them.
- **Grab:** On grab the hit window captures the mouse (`SetCapture`). Basket appears bottom-right on the primary monitor work area and glows while the character is over it. Release over the basket collects; elsewhere the character falls, sulks, and leaves.
- **Cleanup:** If another window captures the mouse, the character is released. Leaving characters drop their hit window immediately.

### 7. Without mouse

- **Hotkey:** Ctrl+Alt+V (`RegisterHotKey`) collects the first character. If taken, settings explain that.
- **Tray menu:** Entry while a character is present.
- **Tooltip:** Tray tooltip names the character and how to collect. Narrator reads it.
- **Hints:** Optional notification on each arrival. Collected characters always report points and achievements quietly via notification; Windows respects Do Not Disturb.

### 8. Sounds, share card

- **Sounds:** Three procedural sounds per character (arrive, collect, sulk), sine tones with per-character pitch (`VisitorSynth`). No samples, no license issues. Audible only when sound is on (Visitors volume).
- **Share card:** XAML on the Visitors page. Saved to clipboard or file via `RenderTargetBitmap` as PNG. PLAN.md mentions Direct2D; XAML yields the same image without a second text renderer; figures use the same SVGs (`SvgImageSource`).
- **Share dialog:** Windows share UI (`DataTransferManager`) added in M12 (ADR-0015).

## Consequences

- Grab via hit windows is real Windows behavior: smoke test drags a character into the basket with real mouse input. Multi-monitor, touch, and pen are checklist M11.
- Whether pace, jump distance, frequency, and visit length feel right is sign-off. All values are constants in `VisitorSystem`, `VisitorCatalog`, and `VisitorScheduler`.
