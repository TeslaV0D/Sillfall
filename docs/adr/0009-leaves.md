# ADR-0009: Leaves

- Status: accepted (visuals subject to maintainer sign-off)
- Date: 2026-11-11
- Milestone: M6

## Context

PLAN.md, section 4.3:

- Leaves with flutter physics and pseudo-3D tumbling,
- resting on surfaces with a cap per 100 px,
- cursor gust and shake.

## Decisions

### 1. Leaf shape as a sprite form

Like snowflakes, the sprite shader draws leaves directly (`SpriteShape.Leaf`). Four variants: pointed oval, maple, oak, and narrow leaf. All have a darker midrib and stem. No texture or asset.

Colors come from a dedicated autumn palette: red, orange, golden yellow, ochre, brown, and late green. A custom color in settings replaces the palette.

### 2. Flutter physics

- **Tumble:** Each leaf has a tumble phase. Width follows |cos(phase)| with a floor so it never vanishes. The back is slightly darker.
- **Lateral force:** Lift follows sin(phase) and pushes sideways for the sway.
- **Falling:** When the leaf is flat to the air (large cos²), it falls slower.
- **Drag:** Air resistance pulls toward wind speed. Fall speed ends around 80–100 px/s × DPI.

### 3. Depth

70% of leaves are near. The rest are smaller, darker, and fall behind windows. Same rule as rain and snow.

### 4. Resting

- **Pose:** A leaf lies flat along the edge, slightly tilted, and moves with its window.
- **Occlusion:** If a higher window covers the spot, the leaf is not visible there.
- **Cap:** An edge is split into fixed 100 px segments. Four leaves per segment max; the oldest is pushed off when a fifth lands. A per-window cap around each landing allowed up to seven leaves per 100 px; a test found that.
- **After turn-off:** Resting leaves fade over 12 s. Then the render loop stops.

### 5. Cursor gust

- **Sampling:** `OverlayHost` reads `GetCursorPos` once per frame, no hook. Only while the render loop runs.
- **Smoothing:** `CursorMotion` smooths velocity over ~50 ms. A jump faster than 20,000 px/s is teleport, not a gust.
- **Effect:** From 600 px/s × DPI, leaves within 90 px × DPI get a push, weaker outward. Resting leaves lift off.

Threshold and radius are estimates. Sign-off must confirm.

### 6. Shake and close

Shake throws all leaves on the window up and sideways. Close lets them fall. Like snow, they do not land again on the window they came from.

## Consequences

- Cursor is available to all effects (`SimulationContext.Cursor`, `CursorVelocity`). Grass (M8) can use it for pushing aside.
- **Sandbox:** `--effect=leaves --scatter-leaves` or keys L and K to test. Drag the mouse over an edge or shake a window.
