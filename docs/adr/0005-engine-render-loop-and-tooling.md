# ADR-0005: Engine, render loop, and tooling

- Status: accepted
- Date: 2026-11-11
- Milestone: M3

## Context

Effects start at M4. Before that they need a foundation (PLAN.md, M3): fixed timestep, render loop with idle stop, a data format between simulation and GPU, instancing, Direct2D, a sandbox, and golden images.

## Decisions

### 1. Engine in Core, loop in the host

`SimulationEngine` (Core) knows monitors, windows, surfaces, wind, and effects. It runs with a fixed step (`FixedStepClock`, 1/120 s, at most 8 steps per frame) and builds a `RenderFrame` of `Sprite`s per frame. It is platform-neutral and runs the same in the app, Sandbox, FrameDump, and tests.

The render loop lives in `OverlayHost` on the overlay thread:

- **Idle:** blocks in `GetMessage`. No frames, no GPU work.
- **Active** (at least one effect has work): drains the message queue without blocking and renders one frame per pass. Paced by the swap chain’s frame-latency waitable (MaxFrameLatency 1). A 240 fps cap guards against busy-looping.
- When no effect has work, the loop stops and presents one empty frame.

### 2. Sprites as the only format between simulation and GPU

A `Sprite` is 48 bytes: center, half size, rotation, shape (rectangle, soft disc, stripe), premultiplied color. All particle effects output sprites. The GPU draws them with one instanced draw from a structured buffer (`SpriteBatch`). Per monitor, culling happens on the CPU first. Full-screen effects (fog, sun) get their own passes from M7.

Effects write struct-of-arrays with fixed capacity so the frame loop allocates nothing. Verified twice: Core test (engine, 600 frames) and live smoke test on Windows (host measures `GC.GetAllocatedBytesForCurrentThread` after 60 warm-up frames: 0 bytes).

### 3. Calibration drops instead of rain immediately

`TestDropsEffect` is deliberately simple: drops fall, land on edges, move with the window, fall on shake or close, and fade out. It exercises every engine feature before M4 builds real rain visuals and remains a diagnostic effect afterward.

### 4. Direct2D

`Direct2DCanvas` layers Direct2D over DXGI surface interop on the same device. Drawing goes into any BGRA texture, including the swap-chain back buffer. Used from M11 (visitors, share card). DirectWrite arrives with the first text.

### 5. Tooling

- **Sandbox** (`tools/Sillfall.Sandbox`): normal window with fake desktop and draggable fake windows, same engine and renderer as the app.
- **FrameDump** (`tools/Sillfall.FrameDump`): plays JSON scenes (`Scenes/*.json`) deterministically and renders capture times with WARP to PNG plus contact sheet.
- **Golden images:** `GoldenImageTests` render the same scenes and compare to `tests/Sillfall.Render.Tests/Goldens/` (tolerance 2 per channel, at most 0.1% of pixels). Missing a golden fails the test and drops the image in previews. Adoption only after visual review.

### 6. Deviation: no `ci-previews/<branch>` branch

PLAN.md had CI push previews to a dedicated branch. A session may write only to its assigned branch. A CI job with write access to other branches would bypass that. Instead all previews (render tests, FrameDump, desktop screenshots) go to CI artifact `previews`. The agent downloads it and inspects PNGs directly. Same purpose.

## Consequences

- Effects implement only `IEffectSystem` and output sprites. No render code required in effects.
- New scenes for previews and goldens are JSON files, not code.
- A golden changes only deliberately: review image, copy from artifact, commit.
