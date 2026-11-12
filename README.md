# Weatherly for Windows

Animiertes Wetter über deinen offenen Fenstern: Regen, Schnee, Nebel, Laub, Sonnenlicht und Gras,
die mit der Fenstergeometrie interagieren. Klicks gehen durch. „Weatherly“ ist ein Arbeitstitel.

**Status:** Meilenstein M14 (Release): MSIX für x64 und ARM64, Datenschutzerklärung, Store-Text und winget-Manifest.
„Weatherly“ ist weiter der Arbeitstitel, bis der Name für den Store reserviert ist. Die Store-Zertifizierung steht
in der Checkliste M14. Davor: Härtung, Sprachen, Besucher, Wetter-Sync, Einstellungen und alle sechs Wettereffekte.

```bash
dotnet run --project tools/Weatherly.Sandbox
``` Der Fahrplan steht in
[PLAN.md](PLAN.md).

**Privacy:** Weatherly liest nur Fenstergeometrie. Keine Fenstertitel, keine Inhalte, keine Analytics.
Diese Zusagen sind als Tests abgesichert.

## Bauen

Voraussetzung: [.NET-10-SDK](https://dotnet.microsoft.com/download), für die App Windows 10 2004 oder neuer.

Unter Windows:

```bash
dotnet build Weatherly.slnx -p:Platform=x64
```

```bash
dotnet run --project src/Weatherly.App -p:WindowsPackageType=None
```

Unter Linux lässt sich alles außer der WinUI-App bauen und der Kern testen:

```bash
dotnet build Weatherly.CrossPlatform.slnf
```

```bash
dotnet test --project tests/Weatherly.Core.Tests
```

## Dokumentation

- [PLAN.md](PLAN.md): Implementierungsplan und Meilensteine
- [CLAUDE.md](CLAUDE.md): Arbeitsregeln, Struktur und Konventionen
- [ASSETS.md](ASSETS.md): Herkunft und Lizenz jedes Assets
- [docs/adr](docs/adr): Architekturentscheidungen
- [docs/manual-tests](docs/manual-tests): Checklisten für Tests auf echtem Windows

## Lizenz

[MIT](LICENSE)
