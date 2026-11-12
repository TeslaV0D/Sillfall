# CLAUDE.md – conventions for agents

Sillfall is a native Windows app that renders animated weather on the edges of open windows.
The full plan is in [PLAN.md](PLAN.md): feature inventory, technical requirements, effect specs,
and milestones M0–M14 (order is binding).

Status: **M13 (hardening)** is implemented. The MVP tray cut (ADR-0008) and all six effects are in tree.
Progress per milestone lives in `docs/manual-tests/`.

## Working rules (from PLAN.md §13)

1. **One milestone = one branch = one PR.** Before the PR: Core tests green locally, CI green, previews checked.
2. **Core stays Windows-free.** No `System.Windows`, Win32, or Vortice references in `Weatherly.Core`. Enforced by `ArchitectureTests`.
3. **No allocations in the frame loop.** Hot paths use `[SkipLocalsInit]`, `Span`, and struct-of-arrays.
4. **Privacy invariants** are tested (`PrivacyInvariantTests`):
   - no `GetWindowText` / `WM_GETTEXT` in product code (geometry only),
   - network only under `src/Weatherly.Core/Weather/Providers/` (`IWeatherProvider` implementations),
   - no screen capture (`BitBlt`, `PrintWindow`, Graphics Capture, Desktop Duplication),
   - coordinates rounded to one decimal before send (covered with weather sync).
5. **Asset invariant:** every asset has a row in [ASSETS.md](ASSETS.md) (source, license, author). Missing rows fail `AssetRegistryTests`. All characters, graphics, sounds, and copy are original or properly licensed CC0/MIT — never taken from third-party weather apps.
6. **Escalate uncertainty** instead of guessing — especially visuals and real Windows behavior. Add a checklist under `docs/manual-tests/`.
7. Non-trivial decisions get a short ADR in `docs/adr/`.

## Project layout

| Path | Framework | Contents |
|---|---|---|
| `src/Weatherly.Core` | `net10.0` | Simulation, geometry, weather mapping, settings. Platform-neutral. |
| `src/Weatherly.Platform` | Windows | Win32 interop via CsWin32 (`NativeMethods.txt`) |
| `src/Weatherly.Render` | Windows | Direct3D 11, DirectComposition, Direct2D via Vortice |
| `src/Weatherly.Audio` | Windows | XAudio2 via Vortice |
| `src/Weatherly.App` | Windows, WinUI 3 | Tray, settings, composition root. MSIX. |
| `tools/Weatherly.Sandbox` | Windows | Debug app: effects over a fake desktop with draggable windows |
| `tools/Weatherly.FrameDump` | Windows | Renders JSON scenes (`Scenes/`) headless with WARP to PNGs |
| `tools/icons` | Python | App icon generator |
| `tools/ci` | PowerShell | CI scripts, runnable locally the same way |
| `tests/Weatherly.Core.Tests` | `net10.0` | Runs on Linux and Windows |
| `tests/Weatherly.Render.Tests` | Windows | WARP tests, Windows CI only |

Product name: always `AppInfo.Name` (**Sillfall**). URI scheme: `AppInfo.UriScheme` (`sillfall://`).
Project/assembly names may still say `Weatherly.*` until packaging rename work.

Overlay (ADR-0003): `OverlayHost` (Platform) owns a thread with a message loop and one window per monitor.
Drawing goes through `IOverlayRenderer` (Core), implemented by `CompositionOverlayRenderer` (Render).

Window tracking (ADR-0004): `WindowEnumerator` → `SurfaceExtractor` → `WindowMotionTracker`.

Engine (ADR-0005): `SimulationEngine` with fixed timestep and `RenderFrame` of sprites. Effects implement
`IEffectSystem` and must not allocate in `Simulate`/`Emit`.

Effects live in `src/Weatherly.Core/Effects/`. Fog/sun use `FogLayer`/`SunLayer` plus `AtmospherePass`.

UI (ADR-0008, ADR-0012): `AppSettings` is the only settings model; `AppController` is the only writer.
Windows: `SettingsWindow`, `FlyoutWindow`, `MainWindow`. Scenes: `Scene`, `ScenePresets`, `SceneCodec`
(`.sillscene`, `sillfall://`).

Weather sync (ADR-0013): `MetNorwayProvider`, `WeatherSync`, `WeatherMapper`, `WeatherBlend`, offline `CityIndex`.

Visitors (ADR-0014): `VisitorSystem`, SVG art under `src/Weatherly.Render/Visitors/`, hit windows in `OverlayHost`.

Hardening (ADR-0016): `SystemState` / `RuntimePolicy`, `CrashLog`, soak via `tools/ci/soak-test.ps1`.

Allowed project references: Platform, Render, and Audio depend only on Core. App depends on all.
See `ArchitectureTests`.

Windows projects share TFM and minimum OS (Windows 10 2004 / build 19041) from `Directory.Build.props`.

## Build and test

```bash
# Linux
dotnet build Weatherly.CrossPlatform.slnf
dotnet test --project tests/Weatherly.Core.Tests
dotnet format Weatherly.CrossPlatform.slnf --verify-no-changes

# Windows
dotnet build Weatherly.slnx -p:Platform=x64
dotnet test --project tests/Weatherly.Core.Tests
dotnet test --project tests/Weatherly.Render.Tests
pwsh tools/ci/smoke-test.ps1
```

Use `dotnet test --project <path>` (Microsoft.Testing.Platform via `global.json`).

## CI (`.github/workflows/ci.yml`)

| Job | Runner | Checks |
|---|---|---|
| `core` | ubuntu-latest | Format, cross-platform build, Core tests |
| `windows` | windows-latest, x64 + ARM64 | Full solution build; x64 also format, Core + Render tests, smoke test |
| `soak` | windows-latest, x64 | 5-minute app soak (`tools/ci/soak-test.ps1`) |
| `package` | windows-latest, x64 and ARM64 | Unsigned MSIX (`tools/ci/package.ps1`) |

`package` publishes an unsigned MSIX per architecture (`tools/ci/package.ps1`, artifact `msix-*`). Store signing and winget submission are documented in `docs/release/`.

## Code conventions

- C# with nullable, `TreatWarningsAsErrors`, analyzers `latest-recommended`. Style in `.editorconfig`.
- Code, identifiers, and comments in **English**. Product docs (this file, ADRs, checklists) in **English**.
- Package versions in `Directory.Packages.props`.
- Visible strings via `Texts.Get` / `Texts.Format` with both `.resw` locales (ADR-0015).
- Premultiplied alpha in all shaders. Coordinates are physical pixels.
- Tests: xUnit v3 + AwesomeAssertions (ADR-0002). Names: `Method_State_Expectation`.
- Win32 APIs listed alphabetically in `src/Weatherly.Platform/NativeMethods.txt`.
