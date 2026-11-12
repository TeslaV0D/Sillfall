# Assets

Every asset in the repository needs a row here with source, license, and author.
`AssetRegistryTests` (CI job `core`) fails when:

- a media file (image, audio, font) or any file under an `Assets/` folder is missing here, or
- an entry points at a file that no longer exists.

**Rules**

- All characters, names, graphics, sounds, and copy are original to Sillfall or properly licensed (CC0 / MIT).
- Allowed: self-made (SVG, procedural) or CC0. Other licenses only after maintainer approval.
- First column: path relative to the repo root in backticks. A trailing `/` covers a whole folder.
  Folder entries are for uniformly generated files (for example golden images from our own renderer).

## Effect icons

The six effect symbols in settings and the flyout (drop, snowflake, leaf, fog band, sun, grass blades)
are hand-drawn paths in code (`Ui.IconPath` in `src/Weatherly.App/Ui.cs`), MIT (project license),
Sillfall project. No files, so no table row.

## App icons

Procedurally drawn (cloud with three raindrops on a rounded gradient).
Regenerate with `python3 tools/icons/generate_app_icons.py` (requires Pillow).

| File | Source | License | Author |
|---|---|---|---|
| `src/Weatherly.App/Assets/AppIcon.ico` | `tools/icons/generate_app_icons.py` | MIT (project license) | Sillfall project |
| `src/Weatherly.App/Assets/SplashScreen.scale-200.png` | `tools/icons/generate_app_icons.py` | MIT (project license) | Sillfall project |
| `src/Weatherly.App/Assets/Square150x150Logo.scale-200.png` | `tools/icons/generate_app_icons.py` | MIT (project license) | Sillfall project |
| `src/Weatherly.App/Assets/Square44x44Logo.scale-200.png` | `tools/icons/generate_app_icons.py` | MIT (project license) | Sillfall project |
| `src/Weatherly.App/Assets/Square44x44Logo.targetsize-24_altform-unplated.png` | `tools/icons/generate_app_icons.py` | MIT (project license) | Sillfall project |
| `src/Weatherly.App/Assets/Square44x44Logo.targetsize-48_altform-lightunplated.png` | `tools/icons/generate_app_icons.py` | MIT (project license) | Sillfall project |
| `src/Weatherly.App/Assets/StoreLogo.png` | `tools/icons/generate_app_icons.py` | MIT (project license) | Sillfall project |
| `src/Weatherly.App/Assets/Wide310x150Logo.scale-200.png` | `tools/icons/generate_app_icons.py` | MIT (project license) | Sillfall project |

## README media

WARP preview frames copied from golden tests for the GitHub README gallery.

| File | Source | License | Author |
|---|---|---|---|
| `docs/media/` | `tests/Weatherly.Render.Tests/Goldens/` (own renderer, WARP) | MIT (project license) | Sillfall project |

## Golden images

Reference frames for render tests (`GoldenImageTests`), produced by our renderer with WARP from scenes in
`tools/Weatherly.FrameDump/Scenes/`. New or changed goldens are taken from the CI `previews` artifact
(`render/golden-actual-*.png`) after visual review.

| File | Source | License | Author |
|---|---|---|---|
| `tests/Weatherly.Render.Tests/Goldens/` | `GoldenImageTests` (own renderer, WARP) | MIT (project license) | Sillfall project |

## Visitors

Original flat-geometric characters and props (ADR-0014). Drawn with Direct2D’s SVG renderer (`VisitorPainter`).
Eyes, feet, and expressions are named parts or derived in code (`VisitorArt`).

| File | Source | License | Author |
|---|---|---|---|
| `src/Weatherly.Render/Visitors/pip.svg` | Visitor Pip (snail), hand-drawn SVG | MIT (project license) | Sillfall project |
| `src/Weatherly.Render/Visitors/tuck.svg` | Visitor Tuck (penguin), hand-drawn SVG | MIT (project license) | Sillfall project |
| `src/Weatherly.Render/Visitors/bristle.svg` | Visitor Bristle (hedgehog), hand-drawn SVG | MIT (project license) | Sillfall project |
| `src/Weatherly.Render/Visitors/lumen.svg` | Visitor Lumen (firefly), hand-drawn SVG | MIT (project license) | Sillfall project |
| `src/Weatherly.Render/Visitors/sol.svg` | Visitor Sol (cat), hand-drawn SVG | MIT (project license) | Sillfall project |
| `src/Weatherly.Render/Visitors/drift.svg` | Visitor Drift (cloud), hand-drawn SVG | MIT (project license) | Sillfall project |
| `src/Weatherly.Render/Visitors/sprout.svg` | Visitor Sprout (potted plant), hand-drawn SVG | MIT (project license) | Sillfall project |
| `src/Weatherly.Render/Visitors/nim.svg` | Visitor Nim (mouse), hand-drawn SVG | MIT (project license) | Sillfall project |
| `src/Weatherly.Render/Visitors/umbrella.svg` | Prop umbrella, hand-drawn SVG | MIT (project license) | Sillfall project |
| `src/Weatherly.Render/Visitors/snowball.svg` | Prop snowball, hand-drawn SVG | MIT (project license) | Sillfall project |
| `src/Weatherly.Render/Visitors/lantern.svg` | Prop lantern, hand-drawn SVG | MIT (project license) | Sillfall project |
| `src/Weatherly.Render/Visitors/leaf.svg` | Prop leaf, hand-drawn SVG | MIT (project license) | Sillfall project |
| `src/Weatherly.Render/Visitors/box.svg` | Collection box, hand-drawn SVG | MIT (project license) | Sillfall project |

## Data

| File | Source | License | Author |
|---|---|---|---|
| `src/Weatherly.Core/Weather/Cities/cities15000.tsv.gz` | GeoNames `cities15000`, packed with `tools/cities/build_cities.py` | CC BY 4.0 (attribution in app) | GeoNames (geonames.org) |
