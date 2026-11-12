# ADR-0010: Fog and sunlight

- Status: accepted (visuals subject to maintainer sign-off)
- Date: 2026-11-11
- Milestone: M7

## Context

PLAN.md, sections 4.4 and 4.5:

- Fog as fBm noise with domain warping at quarter resolution,
- god rays with a mask of windows,
- glow and dust.

Until M6 every frame was sprites only. Full-screen effects needed a second path from Core to the renderer.

## Decisions

### 1. Parameters instead of sprites

`RenderFrame` carries two layers: `FogLayer` (density, drift, phase, color, max opacity) and `SunLayer` (strength, position, color). Plus window rectangles as occluders, at most 64, topmost first.

- Core simulates only these numbers: fog eases in and out, drifts with wind and breathes; the sun moves slowly.
- Drawing is in the renderer. Simulation stays testable and Windows-free.
- Occluders are filled only while fog or sun is visible.

### 2. One pass at quarter resolution

`AtmospherePass` (Render) draws in three steps:

1. Window mask at quarter res: sky white, windows black.
2. Fog and rays in one pixel shader at quarter res into a half-float texture. 8-bit would band in thin fog.
3. Bilinear upscale over the target, premultiplied.

Fog and sun sit behind all sprites (PLAN.md, section 4.7). Textures are cached per monitor size, up to 4 sizes.

### 3. Fog

- **Shape:** 4 octaves value noise with domain warping. Feature size ~380 px × DPI. Fog moves at 60% of wind plus a base drift of 6 px/s.
- **Height:** Denser toward the bottom.
- **Opacity:** at most 35%, over windows only 45% of that. A bright rim on top edges lets windows emerge from fog.

### 4. Sunlight

- **Position:** Sun sits just above the desktop’s upper right or left corner and wanders a few percent over minutes.
- **Rays:** 48 steps through the mask toward the sun with decay 0.965. Free-sky fraction, squared, yields rays. Windows cast long shadows.
- **Glow:** around the sun. Over windows only 35% of light remains so content stays readable.
- **Dust:** 100 particles drift in the cone. They sparkle and brighten toward the sun.

Additive light on a transparent overlay is limited. Light is emitted as warm color with low opacity (alpha = 30% of brightness). Whether that reads warm rather than milky on real desktops is for sign-off.

That makes the light “overbright” in premultiplied terms (color greater than alpha), i.e. additive. PNGs store straight alpha and clip such values. Previews look slightly weaker than on screen. Golden comparison runs the render through the same PNG path before compare.

### 5. Without weather sync

Day/night behavior (`is_day`) and strength from cloud cover arrive with M10. Sun side is configurable in code (`SunEffect.Side`). It appears in settings with M9.

## Consequences

- GPU cost grows: fog ~60 ALU ops per quarter pixel, sun 48 texture samples. Measured on reference hardware; see checklist M7.
- Golden images `fog`, `sun`, and `sun-and-fog`, plus WARP tests for occlusion, height falloff, and shadows.
