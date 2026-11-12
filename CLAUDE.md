# CLAUDE.md – conventions for agents

Sillfall is a native Windows app that renders animated weather on the edges of open windows.
The full plan is in [PLAN.md](PLAN.md): feature inventory, technical requirements, effect specs,
and milestones M0–M14 (order is binding).

Status: **M14 (release)**. MSIX for x64 and ARM64, privacy policy, store listing, and winget manifest are in tree
(ADR-0017). Store certification and the name reservation are in checklist M14. Progress per milestone lives in
`docs/manual-tests/`.

## Working rules (from PLAN.md §13)

1. **One milestone = one branch = one PR.** Before the PR: Core tests green locally, CI green, previews checked.
2. **Core stays Windows-free.** No `System.Windows`, Win32, or Vortice references in `Sillfall.Core`. Enforced by `ArchitectureTests`.
3. **No allocations in the frame loop.** Hot paths use `[SkipLocalsInit]`, `Span`, and struct-of-arrays.
4. **Privacy invariants** are tested (`PrivacyInvariantTests`):
   - no `GetWindowText` / `WM_GETTEXT` in product code (geometry only),
   - network only under `src/Sillfall.Core/Weather/Providers/` (`IWeatherProvider` implementations),
   - no screen capture (`BitBlt`, `PrintWindow`, Graphics Capture, Desktop Duplication),
   - coordinates rounded to one decimal before send (covered with weather sync).
5. **Asset invariant:** every asset has a row in [ASSETS.md](ASSETS.md) (source, license, author). Missing rows fail `AssetRegistryTests`. All characters, graphics, sounds, and copy are original or properly licensed CC0/MIT — never taken from third-party weather apps.
6. **Escalate uncertainty** instead of guessing — especially visuals and real Windows behavior. Add a checklist under `docs/manual-tests/`.
7. Non-trivial decisions get a short ADR in `docs/adr/`.

## Project layout

| Path | Framework | Contents |
|---|---|---|
| `src/Sillfall.Core` | `net10.0` | Simulation, geometry, weather mapping, settings. Platform-neutral. |
| `src/Sillfall.Platform` | Windows | Win32 interop via CsWin32 (`NativeMethods.txt`) |
| `src/Sillfall.Render` | Windows | Direct3D 11, DirectComposition, Direct2D via Vortice |
| `src/Sillfall.Audio` | Windows | XAudio2 via Vortice |
| `src/Sillfall.App` | Windows, WinUI 3 | Tray, settings, composition root. MSIX. |
| `tools/Sillfall.Sandbox` | Windows | Debug app: effects over a fake desktop with draggable windows |
| `tools/Sillfall.FrameDump` | Windows | Renders JSON scenes (`Scenes/`) headless with WARP to PNGs |
| `tools/icons` | Python | App icon generator |
| `tools/ci` | PowerShell | CI scripts, runnable locally the same way |
| `tests/Sillfall.Core.Tests` | `net10.0` | Runs on Linux and Windows |
| `tests/Sillfall.Render.Tests` | Windows | WARP tests, Windows CI only |

Product name: always `AppInfo.Name` (**Sillfall**). URI scheme: `AppInfo.UriScheme` (`sillfall://`).
Project/assembly names may still say `Sillfall.*` until packaging rename work.

Overlay (ADR-0003): `OverlayHost` (Platform) owns a thread with a message loop and one window per monitor.
Drawing goes through `IOverlayRenderer` (Core), implemented by `CompositionOverlayRenderer` (Render).

Window tracking (ADR-0004): `WindowEnumerator` → `SurfaceExtractor` → `WindowMotionTracker`.

Engine (ADR-0005): `SimulationEngine` with fixed timestep and `RenderFrame` of sprites. Effects implement
`IEffectSystem` and must not allocate in `Simulate`/`Emit`.

Effects live in `src/Sillfall.Core/Effects/`. Fog/sun use `FogLayer`/`SunLayer` plus `AtmospherePass`.

UI (ADR-0008, ADR-0012): `AppSettings` is the only settings model; `AppController` is the only writer.
Windows: `SettingsWindow`, `FlyoutWindow`, `MainWindow`. Scenes: `Scene`, `ScenePresets`, `SceneCodec`
(`.sillscene`, `sillfall://`).

Weather sync (ADR-0013): `MetNorwayProvider`, `WeatherSync`, `WeatherMapper`, `WeatherBlend`, offline `CityIndex`.

Visitors (ADR-0014): `VisitorSystem`, SVG art under `src/Sillfall.Render/Visitors/`, hit windows in `OverlayHost`.

Release (ADR-0017): `tools/ci/package.ps1` builds the MSIX (x64/ARM64), unsigned for the Store and signed with a
development certificate for sideloading. The `package` job runs on pull requests and tags `v*`. A tag drafts a
release; Azure Artifact Signing signs only when the reserved identity and the secrets are set. Privacy policy:
`docs/privacy.md` (German: `docs/privacy.de.md`). Store text: `docs/store/listing.md`. winget: `winget/`.

Hardening (ADR-0016): `SystemState` / `RuntimePolicy`, `CrashLog`, soak via `tools/ci/soak-test.ps1`.

Allowed project references: Platform, Render, and Audio depend only on Core. App depends on all.
See `ArchitectureTests`.

Windows projects share TFM and minimum OS (Windows 10 2004 / build 19041) from `Directory.Build.props`.

## Build and test

```bash
# Linux
dotnet build Sillfall.CrossPlatform.slnf
dotnet test --project tests/Sillfall.Core.Tests
dotnet format Sillfall.CrossPlatform.slnf --verify-no-changes

# Windows
dotnet build Sillfall.slnx -p:Platform=x64
dotnet test --project tests/Sillfall.Core.Tests
dotnet test --project tests/Sillfall.Render.Tests
pwsh tools/ci/smoke-test.ps1
```

Use `dotnet test --project <path>` (Microsoft.Testing.Platform via `global.json`).

## CI (`.github/workflows/ci.yml`)

| Job | Runner | Checks |
|---|---|---|
| `core` | ubuntu-latest | Format, cross-platform build, Core tests |
| `windows` | windows-latest, x64 + ARM64 | Full solution build; x64 also format, Core + Render tests, smoke test |
| `soak` | windows-latest, x64 | 5-minute app soak (`tools/ci/soak-test.ps1`) |
| `package` | windows-latest, x64 and ARM64 | MSIX (`tools/ci/package.ps1`), artifacts `msix-x64` and `msix-ARM64`. Pull requests, “Run workflow”, and tags `v*` |
| `release` | windows-latest | Tags `v*` only: draft GitHub release, Artifact Signing when the secrets are set |

`package` signs with a development certificate. Public signing happens on a tag once the Artifact Signing secrets
are set (ADR-0017). Store submission and winget are in checklist M14.

## Code conventions

- C# with nullable, `TreatWarningsAsErrors`, analyzers `latest-recommended`. Style in `.editorconfig`.
- Code, identifiers, and comments in **English**. Product docs (this file, ADRs, checklists) in **English**.
- Package versions in `Directory.Packages.props`.
- Visible strings via `Texts.Get` / `Texts.Format` with both `.resw` locales (ADR-0015).
- Every new control needs a Narrator name (`AutomationProperties.Name`; the `Ui` helpers set it). The smoke test
  (`AccessibilityCheck`) checks every settings page.
- Premultiplied alpha in all shaders. Coordinates are physical pixels.
- Tests: xUnit v3 + AwesomeAssertions (ADR-0002). Names: `Method_State_Expectation`.
- Win32 APIs listed alphabetically in `src/Sillfall.Platform/NativeMethods.txt`.
