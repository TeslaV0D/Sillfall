# Sillfall

Animated weather for Windows — rain, snow, fog, leaves, sunlight, and grass that settle on the edges of your open windows. Effects follow window geometry. Clicks pass through.

<p align="center">
  <img src="docs/media/rain.png" alt="Heavy rain settling on window tops" width="48%" />
  <img src="docs/media/snow.png" alt="Snow gathering on window edges" width="48%" />
</p>

<p align="center">
  <img src="docs/media/leaves.png" alt="Autumn leaves drifting and landing" width="32%" />
  <img src="docs/media/fog.png" alt="Soft fog across the desktop" width="32%" />
  <img src="docs/media/sun.png" alt="Warm sunlight between windows" width="32%" />
</p>

**Status:** Milestone **M14** (release): setup .exe and MSIX for x64 and ARM64, privacy policy, store listing, and winget manifest. Store certification and the name reservation are tracked in [checklist M14](docs/manual-tests/M14.md). See [PLAN.md](PLAN.md).

## Features

- **Six effects** — rain (splashes, puddles, rivulets), snow (accumulation & melt), fog, autumn leaves, sunlight / god-rays, grass on window tops
- **Window-aware** — weather lands on visible edges; closing or shaking a window sheds snow and leaves
- **Click-through** — overlays stay out of the way; optional interactive visitors use separate hit windows
- **Weather sync** — optional live conditions via MET Norway (offline city search, invert, per-effect blend)
- **Scenes** — save and share looks as `.sillscene` files or `sillfall://` links
- **Visitors** — eight original characters that visit window edges; collect them locally
- **Privacy-first** — reads window geometry only; no titles, no screen contents, no analytics
- **English & German** UI, Narrator labels, high contrast, reduced-motion options

## Download

Get the latest **Sillfall-Setup-…-x64.exe** (or **…-arm64.exe** for ARM PCs) from [Releases](https://github.com/TeslaV0D/Sillfall/releases/latest) and run it. It installs for the current user without administrator rights and adds a Start menu entry; uninstall from *Settings > Apps*. Requires **Windows 10 2004** or newer.

Until the first signed release, SmartScreen may show *Windows protected your PC*. Choose *More info* > *Run anyway*. Checksums are in `SHA256SUMS-setup-*.txt` next to the download. See the [code signing policy](#code-signing-policy).

## Build from source

Requires the [.NET 10 SDK](https://dotnet.microsoft.com/download). The WinUI app needs **Windows 10 2004** or newer.

```bash
# Full solution (Windows)
dotnet build Sillfall.slnx -p:Platform=x64
dotnet run --project src/Sillfall.App -p:WindowsPackageType=None

# Effect sandbox with fake windows (Windows)
dotnet run --project tools/Sillfall.Sandbox

# Core on Linux (simulation & tests)
dotnet build Sillfall.CrossPlatform.slnf
dotnet test --project tests/Sillfall.Core.Tests
```

Build the setup yourself with [Inno Setup 6](https://jrsoftware.org/isinfo.php) installed:

```bash
pwsh tools/ci/installer.ps1 -Platform x64 -OutputDirectory artifacts/installer
```

## Gallery

| Rain on sills | Snow banks | Grass + rain |
|---|---|---|
| ![Rain](docs/media/rain.png) | ![Snow](docs/media/snow.png) | ![Grass](docs/media/grass.png) |

| Leaves | Fog | Sun & fog |
|---|---|---|
| ![Leaves](docs/media/leaves.png) | ![Fog](docs/media/fog.png) | ![Sun and fog](docs/media/sun-and-fog.png) |

These frames are deterministic WARP renders from `tools/Sillfall.FrameDump` (overlay only, transparent background shown as black).

## Architecture (short)

| Project | Role |
|---|---|
| `Sillfall.Core` | Simulation, surfaces, weather mapping, settings (platform-neutral) |
| `Sillfall.Platform` | Win32 overlays, window tracking, tray, shell integration |
| `Sillfall.Render` | Direct3D 11 / DirectComposition / Direct2D |
| `Sillfall.Audio` | XAudio2 |
| `Sillfall.App` | WinUI 3 tray, settings, MSIX packaging |

Product display name and URI scheme come from `AppInfo` (`Sillfall`, `sillfall://`). Assembly / project names still use the historical `Sillfall.*` identifiers until a bulk rename in packaging work.

## Privacy

Sillfall reads **window geometry** only. It does not read titles or contents, does not capture the screen, and ships without telemetry. Crash reports stay on disk under `%LOCALAPPDATA%\Sillfall\logs`. These rules are enforced by automated invariant tests.

## Code signing policy

Windows setups are signed through the open-source program of SignPath, once the project is accepted there:
free code signing provided by [SignPath.io](https://signpath.io), certificate by [SignPath Foundation](https://signpath.org).
Releases published before that are unsigned and say so in their release notes.

- Only binaries built by this repository's GitHub Actions workflow (`.github/workflows/ci.yml`, GitHub-hosted runners)
  from the tagged source are signed. Nothing built on a local machine is submitted.
- Every signing request is approved by hand before it is signed.

Team roles:

| Role | Members |
|---|---|
| Authors (committers) | [@TeslaV0D](https://github.com/TeslaV0D) |
| Reviewers | [@TeslaV0D](https://github.com/TeslaV0D) — changes from contributors and coding agents are reviewed in pull requests before merge |
| Approvers | [@TeslaV0D](https://github.com/TeslaV0D) |

Privacy: this program will not transfer any information to other networked systems unless specifically requested
by the user or the person installing or operating it. The only such request is the optional weather sync, which sends
a location rounded to about 11 km to MET Norway. Details in the [privacy policy](docs/privacy.md).

## Documentation

- [PLAN.md](PLAN.md) — product plan and milestones
- [CLAUDE.md](CLAUDE.md) — contributor / agent conventions
- [ASSETS.md](ASSETS.md) — source and license for every asset
- [docs/adr](docs/adr) — architecture decisions
- [docs/manual-tests](docs/manual-tests) — Windows checklists
- [docs/weather-mapping.md](docs/weather-mapping.md) — forecast → effect mapping
- [docs/privacy.md](docs/privacy.md) — privacy policy ([Deutsch](docs/privacy.de.md))
- [docs/store/listing.md](docs/store/listing.md) — Microsoft Store listing text
- [winget/](winget) — winget manifest (submitted after the first signed release)

## License

[MIT](LICENSE)
