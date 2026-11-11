# ADR-0001: Tech-Stack

- Status: akzeptiert
- Datum: 2026-11-11
- Meilenstein: M0

## Kontext

Weatherly rendert animiertes Wetter über den offenen Fenstern. Der teure Teil ist GPU-gebunden,
die CPU-Simulation liegt bei höchstens 20 000 Partikeln. Entwickelt wird überwiegend von einem
Coding-Agenten, der oft in einem Linux-Container ohne Windows-Desktop läuft.

## Entscheidung

Option A aus PLAN.md, Abschnitt 1: **C# auf .NET 10, Direct3D 11 und DirectComposition über
Vortice.Windows, Win32-Interop über CsWin32, UI mit WinUI 3.**

Verworfen:

- **C++20 mit C++/WinRT:** kleinster Footprint, aber langsamere Iteration, und die Kernlogik wäre
  unter Linux kaum testbar.
- **Rust mit windows-rs:** sicher und schnell, aber die WinUI-Integration ist mühsam, und
  DirectComposition-Transparenz mit wgpu ist fummelig.

## Konkretisierungen in M0

Diese Punkte präzisieren den Plan oder weichen leicht davon ab.

| Thema | Entscheidung | Grund |
|---|---|---|
| Target-Framework der Windows-Projekte | `net10.0-windows10.0.26100.0`, Mindestversion 10.0.19041 (statt `net10.0-windows`) | `net10.0-windows` bedeutet Windows 7. Der Analyzer CA1416 lehnt dann schon `GetThreadDpiAwarenessContext` ab. Mit Mindestversion 19041 (erste Version mit `WDA_EXCLUDEFROMCAPTURE`) meldet er genau die APIs, die einen Versions-Check brauchen. |
| Windows App SDK | 2.6.5 | Aktuelle stabile Linie. Projektdatei folgt dem offiziellen Template `Microsoft.WindowsAppSDK.WinUI.CSharp.Templates`. |
| Plattformen | x64 und ARM64, kein x86 | Wie im Plan (CI baut x64 und ARM64). |
| Solution-Format | `Weatherly.slnx` | Standardformat des .NET-10-SDK. `Weatherly.CrossPlatform.slnf` filtert die WinUI-App für Linux heraus. |
| Test-Runner | xUnit v3 auf Microsoft.Testing.Platform | xUnit v3 setzt unter .NET 10 darauf auf, der alte VSTest-Weg wird nicht mehr unterstützt. |
| Assertions | AwesomeAssertions statt FluentAssertions | Lizenz, siehe ADR-0002. |
| `Vortice.D3DCompiler` | noch nicht referenziert | Shader werden laut Plan zur Build-Zeit kompiliert. Das Paket kommt erst dazu, falls Laufzeit-Kompilierung gebraucht wird. |
| Tray-Icon (`H.NotifyIcon.WinUI`) | noch nicht referenziert | Wird mit dem Minimal-Tray entschieden (MVP-Cut). |
| App-Start im CI | `--smoke-test=<Datei>` | Die App startet auf dem Windows-Runner, prüft DPI-Awareness und Direct3D-Device und beendet sich. Ersetzt keinen manuellen Test, fängt aber Startfehler ab. |

## Konsequenzen

- Rund 60 % des Codes (Simulation, Geometrie, Wetter-Mapping, Settings) sind unter Linux bau- und testbar.
  Zusätzlich lassen sich Platform, Render und Audio unter Linux kompilieren (`EnableWindowsTargeting`),
  nur nicht ausführen.
- Die WinUI-App baut nur unter Windows. Prüfinstanz dafür ist der CI-Job `windows`.
- Etwas mehr RAM als bei C++ (ca. 80–120 MB) und GC-Disziplin im Frame-Loop nötig (Regel 3 in CLAUDE.md).
