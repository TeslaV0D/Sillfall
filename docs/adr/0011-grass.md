# ADR-0011: Grass

- Status: accepted (visuals subject to maintainer sign-off)
- Date: 2026-11-11
- Milestone: M8

## Context

PLAN.md, section 4.6:

- Blades per surface, density from width,
- growth to ~18 px × DPI, faster in rain,
- sway in wind, optional cursor avoidance,
- fade on close instead of shedding.

## Decisions

### 1. Blade as a sprite shape

The plan describes tapered quads of 3 segments bent in the vertex shader. Implemented: sprite shape `Blade`, an upright quad where the pixel shader draws a tapering blade. The centerline bends with the square of height to the side, like bending at the root. Strength is in `Sprite.Variant`; the quad is widened per `Sprite.BladeHalfHeight`.

One sprite per blade instead of three keeps sprite count low: ~250 blades per 1000 px of edge. Root is darker than tip.

### 2. Strips per surface

- **Creation:** Each window top edge and each ground gets a strip on first grass with ~one blade per 4 px × DPI. Memory allocated only then and when a window grows wider.
- **Placement:** Blades stored relative to the left end and move with the window.
- **Resize:** Narrower window drops blades beyond the new edge. Wider spawns new ones.
- **Stable look:** Each position always gets the same blade (RNG seeded per position). Regrown windows do not look re-rolled.
- **Tufts:** A slow wave over position creates high and low sections.

### 3. Growth

Grass grows at 0.8 px/s × DPI × intensity, times (1 + 2 × rain) per plan. Without rain full height in ~20 s; full rain three times faster. Estimate: growth should be visible but not endless.

### 4. Motion

- **Wind:** Wind tilts all blades. A slow wave across width; each blade has its own phase.
- **Cursor:** Blades within 36 px × DPI bend away from the cursor, weaker outward. Uses the cursor introduced in M6.

### 5. End

- **Window closed:** Its grass fades at the old spot in 0.6 s.
- **Grass off:** Shrinks and fades in 3 s, then the render loop stops.

## Consequences

While grass is on, the render loop runs continuously because blades sway. Same for any active effect. Performance is for sign-off.
