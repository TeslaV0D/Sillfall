# CLAUDE.md – Konventionen für Agenten

Weatherly ist ein natives Windows-Tool, das animiertes Wetter über den offenen Fenstern rendert.
Der vollständige Plan steht in [PLAN.md](PLAN.md). Dort stehen Feature-Inventar, Technik-Vorgaben,
Effekt-Spezifikationen und die Meilensteine M0–M14 (Reihenfolge verbindlich).

Stand: **M1 (Overlay-Spike)**. Den Fortschritt pro Meilenstein dokumentieren die Checklisten in
`docs/manual-tests/`.

## Arbeitsregeln (aus PLAN.md, Abschnitt 13)

1. **Ein Meilenstein = ein Branch = ein PR.** Vor dem PR: Core-Tests lokal grün, CI grün, Previews geprüft.
2. **Core bleibt Windows-frei.** Keine `System.Windows`-, Win32- oder Vortice-Referenzen in
   `Weatherly.Core`. Erzwungen durch `ArchitectureTests`.
3. **Keine Allocations im Frame-Loop.** Hot-Paths mit `[SkipLocalsInit]`, `Span`, Struct-of-Arrays.
4. **Privacy-Invarianten** sind als Tests abgesichert (`PrivacyInvariantTests`):
   - kein `GetWindowText`/`WM_GETTEXT` im ganzen Produktcode, es wird nur Fenstergeometrie gelesen,
   - Netzwerkzugriffe ausschließlich in `src/Weatherly.Core/Weather/Providers/` (Implementierungen
     von `IWeatherProvider`),
   - kein Lesen des Bildschirms (`BitBlt`, `PrintWindow`, Graphics Capture, Desktop Duplication),
   - Koordinaten werden vor dem Senden auf eine Dezimale gerundet (Test kommt mit M10).
5. **IP-Invariante:** Jedes Asset hat einen Eintrag in [ASSETS.md](ASSETS.md) (Quelle, Lizenz, Autor).
   Fehlt er, schlägt `AssetRegistryTests` fehl. Keine Charaktere, Namen, Grafiken, Sounds oder Texte
   der Referenz-App.
6. **Unsicherheit an Boss eskalieren**, statt zu raten. Das gilt für alles Optische und für alles,
   was echtes Windows-Verhalten betrifft. Dafür eine manuelle Checkliste in `docs/manual-tests/` anlegen.
7. Jede nicht-triviale Entscheidung bekommt eine kurze ADR in `docs/adr/`.

## Projektstruktur

| Pfad | Framework | Inhalt |
|---|---|---|
| `src/Weatherly.Core` | `net10.0` | Simulation, Geometrie, Wetter-Mapping, Settings. Plattformneutral. |
| `src/Weatherly.Platform` | Windows | Win32-Interop über CsWin32 (`NativeMethods.txt`) |
| `src/Weatherly.Render` | Windows | Direct3D 11, DirectComposition, Direct2D über Vortice |
| `src/Weatherly.Audio` | Windows | XAudio2 über Vortice |
| `src/Weatherly.App` | Windows, WinUI 3 | Tray, Settings, Composition Root. MSIX. |
| `tools/Weatherly.Sandbox` | Windows | Debug-App mit Fake-Fenstern (ab M3) |
| `tools/Weatherly.FrameDump` | Windows | Headless-Renderer für Golden-Images über WARP (ab M3) |
| `tools/icons` | Python | Generator für die App-Icons |
| `tools/ci` | PowerShell | Skripte für CI, lokal genauso ausführbar |
| `tests/Weatherly.Core.Tests` | `net10.0` | Läuft auf Linux und Windows |
| `tests/Weatherly.Render.Tests` | Windows | WARP-Tests, laufen nur im Windows-CI-Job |

Overlay-Aufbau (Details in ADR-0003): `OverlayHost` (Platform) besitzt einen eigenen Thread mit
Message-Loop und ein Fenster pro Monitor. Gezeichnet wird über den Vertrag `IOverlayRenderer`
(Core), den `CompositionOverlayRenderer` (Render) mit D3D11 und DirectComposition erfüllt.

Erlaubte Projektreferenzen: Platform, Render und Audio kennen nur Core. App kennt alle.
Die Tabelle dazu steht in `ArchitectureTests`. Ein neues Projekt muss dort eingetragen werden.

Die Windows-Projekte nutzen ein gemeinsames Target-Framework und eine gemeinsame Mindestversion
(Windows 10 2004, Build 19041). Beides steht in `Directory.Build.props`. Für APIs, die neuer als die
Mindestversion sind, verlangt der Analyzer CA1416 einen Versions-Check.

## Bauen und Testen

Voraussetzung: .NET-SDK gemäß `global.json` (10.0.x).

Unter Linux (alles außer der WinUI-App, die Windows-Bibliotheken werden cross-kompiliert):

```bash
dotnet build Weatherly.CrossPlatform.slnf
dotnet test --project tests/Weatherly.Core.Tests
dotnet format Weatherly.CrossPlatform.slnf --verify-no-changes
```

Unter Windows (ganze Solution):

```bash
dotnet build Weatherly.slnx -p:Platform=x64
dotnet test --project tests/Weatherly.Core.Tests
dotnet test --project tests/Weatherly.Render.Tests
pwsh tools/ci/smoke-test.ps1
```

Hinweise:

- Tests laufen über Microsoft.Testing.Platform (`global.json`). Deshalb `dotnet test --project <Pfad>`
  statt des Pfads als Positionsargument.
- Die WinUI-App baut nur unter Windows, weil der XAML-Compiler eine Windows-Exe ist. Unter Linux
  ist der Windows-CI-Job die Prüfung dafür.
- `tools/ci/smoke-test.ps1` startet die App zweimal mit `--smoke-test=<Datei>`. Die App zeigt ihr
  Fenster und die Overlays, prüft sich selbst (`OverlayDiagnostics`) und schreibt das Ergebnis in die
  Datei. Das Skript fotografiert währenddessen den Desktop und prüft, ob das Testmuster wirklich zu
  sehen ist (bzw. mit `--exclude-from-capture` nicht). Neue Schichten tragen sich in
  `StartupDiagnostics` ein.
- Shader liegen in `src/Weatherly.Render/Shaders/` (`Name.vs.hlsl`, `Name.ps.hlsl`) und werden beim
  Build unter Windows mit `fxc` kompiliert und eingebettet (`Shaders.targets`). Unter Linux wird das
  übersprungen: Render kompiliert, kann aber nicht zeichnen.

## CI (`.github/workflows/ci.yml`)

| Job | Runner | Prüft |
|---|---|---|
| `core` | ubuntu-latest | Format, Build der Cross-Platform-Projekte, Core-Tests (inkl. Architektur-, Privacy- und Asset-Invarianten) |
| `windows` | windows-latest, x64 und ARM64 | Build der ganzen Solution. Nur x64: Format, Core-Tests, Render-Tests (WARP), Smoke-Test der App |

Jeder Render-Test speichert sein Bild als PNG, das Smoke-Skript seine Desktop-Screenshots. Beides
lädt der Job als Artefakt `previews` hoch. So lässt sich die Optik ohne Windows-Desktop prüfen
(Artefakt herunterladen, PNGs mit dem Read-Tool ansehen).

Noch nicht eingerichtet, weil der Inhalt dafür erst später entsteht:

- FrameDump-Previews und der Branch `ci-previews/<branch>` (M3),
- Job `package` für MSIX-Build und Signierung bei Tags (M14).

## Code-Konventionen

- C# mit Nullable, `TreatWarningsAsErrors`, Analyzer auf `latest-recommended`. Stil steht in `.editorconfig`.
- Code, Bezeichner und Kommentare auf Englisch. Doku für Boss (dieses Dokument, ADRs, Checklisten) auf Deutsch.
- Paketversionen stehen zentral in `Directory.Packages.props`.
- Der Produktname kommt immer aus `AppInfo.Name`. „Weatherly“ ist ein Arbeitstitel.
- Alle Shader geben premultiplied Alpha aus. Alle Koordinaten sind intern physische Pixel.
- Tests: xUnit v3 und AwesomeAssertions (siehe ADR-0002). Testnamen im Muster `Methode_Zustand_Erwartung`.
- Win32-APIs kommen in `src/Weatherly.Platform/NativeMethods.txt`, alphabetisch sortiert.
