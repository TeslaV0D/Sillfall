# ADR-0019: Shader bytecode under IntermediateOutputPath

- Status: accepted
- Date: 2026-11-20
- Milestone: M14 (hotfix)

## Context

Rain v2 added `SpriteShape.Raindrop` and `WaterFilm` in `Sprite.ps.hlsl`. The PR CI on that branch was green,
but the merge commit on `main` failed Render tests: those shapes drew as solid rectangles, and rain goldens
mismatched by thousands of pixels. Disc/Streak/Flake still matched — only shapes 7 and 8 were missing from
the pixel shader that ran.

`Shaders.targets` had snapshotted `$(IntermediateOutputPath)` into `ShaderOutputDirectory` at import time.
At that moment the property was still empty, so the directory became `Shaders\` next to the `.hlsl` sources.
Checked-in `.cso` files lived there. On a fresh checkout the `.cso` and `.hlsl` share the same mtime, so
MSBuild's incremental check treated the outputs as up to date and **skipped** `D3DCompile`. The assembly
embedded the pre-raindrop bytecode. Whether a given CI run recompiled depended on filesystem mtime ordering —
hence green on the PR and red on `main` for the same tree.

## Decision

1. Evaluate `$(IntermediateOutputPath)` only inside the compile/embed targets (never as an early property).
2. Write compiled `.cso` files only under `$(IntermediateOutputPath)Shaders\`.
3. Do not commit `.cso` next to sources; ignore `src/Sillfall.Render/Shaders/*.cso`.

## Consequences

Windows CI always recompiles from `.hlsl` into `obj/`. Linux still skips shader compile (no `d3dcompiler_47.dll`).
A stale checked-in bytecode can no longer silently win over the sources.
