# Manual checklist – rain v2

Shapes and physics from ADR-0006 amendment 2026-11 (rain v2). Automated: Core rain/water-field tests (sizes, Atlas
fall speed, gust drag, splash energy, stick-slip drips, puddle sprite count, sprite budget, 0 allocations, sim
budget). Render SpriteTests for Raindrop/WaterFilm (Windows CI). Goldens for `rain-*`, `grass-rain-*`, and `rain-gusty` were regenerated after the splash-lobe fix
(short crowns, no needle comb) from CI `previews` on commit `5f90433`. After merge to `main`, Render CI
failed when a stale checked-in `Sprite.ps.cso` was embedded instead of recompiling (ADR-0019); fixed in 0.1.2.

## Start

```bash
dotnet run --project src/Sillfall.App -p:WindowsPackageType=None
```

Test pattern off. Compare against a build from before rain v2 when noting Task Manager numbers.

## 1. Readability

- [ ] Dark wallpaper: drops readable, not harsh white streaks.
- [ ] Light wallpaper: drops still visible (dark rim helps); not washed out.
- [ ] Custom rain colour still applies; rim/alpha still vary with depth.

## 2. Depth and scale

- [ ] 100 % display scaling: distant drops do not shimmer as sub-pixel lines.
- [ ] 200 % scaling: sizes and speeds feel the same; no flicker on far drops.
- [ ] Near drops are thicker, faster, and more contrasted than far ones.

## 3. Wind and gusts

- [ ] Steady wind: rain leans; not a regular grid of identical streaks.
- [ ] High gustiness: the curtain fans and breathes; small drops lead the gusts.
- [ ] Sandbox / FrameDump scene `rain-gusty` (wind 120, gustiness high, intensity 0.8) looks intentional.

## 4. Splashes and edges

- [ ] Heavy rain: no noisy band of needles/dots along every top edge (sparse short crown lobes, not a comb).
- [ ] Splashes leave the edge and fall back into it; nothing rains into window content.
- [ ] Ripples start thicker and thin out.

## 5. Puddles

- [ ] Water film looks smooth (trapezoid bins), no stair-steps.
- [ ] No bright seams between neighbouring bins.
- [ ] Surface has a subtle glint; waves from impacts are gentle, not volume-changing.

## 6. Gutters (drips)

- [ ] Fresh rivulets pause, gather, then slip (stick-slip), not a constant slide on rails.
- [ ] Feeding the same corner merges into the hanging drip instead of stacking many.
- [ ] Trails fade by time and taper; after rain ends the effect goes idle (0 overlay frames).

## 7. Performance (maintainer GPU)

- [ ] Intensity 50 %: note GPU time and CPU in Task Manager vs pre-v2.
- [ ] Intensity 100 %: same; sprite count and sim budget tests stay green in CI.
- [ ] Smoke / soak: no managed allocations in the warm frame loop.

## 8. Goldens (after visual review)

- [x] Accept or reject each `golden-actual-rain-*.png` / `grass-rain-*.png` / `rain-gusty-*.png` from the CI
      `previews` artifact.
- [x] Only then copy approved images into `tests/Sillfall.Render.Tests/Goldens/` and push.
