# ADR-0016: Härtung

- Status: akzeptiert
- Datum: 2026-11-12
- Meilenstein: M13

## Kontext

PLAN.md, M13 verlangt:

- Vollbild- und Spiel-Erkennung,
- Pause bei Sperre, Standby und Remotedesktop,
- Grafikkarten-Ausfall,
- Umgang mit dem Startmenü,
- lokale Absturzprotokolle ohne Telemetrie,
- 8 Stunden ohne Leck.

Abnahme: Soak-Test bestanden, Budgets auf Referenz-Hardware.

## Entscheidungen

### 1. Systemzustände pausieren das Wetter

`SystemState` (Core) kennt neu:

- Sperre (inklusive getrennter Sitzung und Standby),
- Vollbild,
- Präsentationsmodus,
- Remotedesktop.

Wer im Vordergrund ist, verrät, ob Start, Suche oder Benachrichtigungen offen sind. `RuntimePolicy` entscheidet in
dieser Reihenfolge:

1. aus
2. gesperrt
3. Vollbild
4. Präsentation
5. Remotedesktop (abschaltbar)
6. Reduced Motion
7. hoher Kontrast
8. Akku
9. Start und Suche (abschaltbar)
10. App-Regeln

Eine Pause entfernt die Overlay-Fenster ganz. Über einem Spiel liegt also nichts, auch kein leeres Fenster. Es gibt
keine Hooks in fremde Prozesse (Anti-Cheat).

Die Erkennung im `OverlayHost`:

- **Vollbild und Präsentation:** `SHQueryUserNotificationState`, bei jedem Fokuswechsel und alle 2 Sekunden. Die Shell
  meldet das nicht von sich aus. Der Timer läuft am Steuerfenster, also auch, solange keine Overlays da sind, sonst
  bemerkte die App das Ende eines Spiels nie. Ein Vollbildfenster des eigenen Prozesses zählt nicht, damit die App
  sich nicht selbst pausiert.
- **Sperre und Remotedesktop:** `WTSRegisterSessionNotification` (Sperren, Entsperren, Verbinden, Trennen) und
  `SM_REMOTESESSION`.
- **Standby:** `WM_POWERBROADCAST` (`PBT_APMSUSPEND`, `PBT_APMRESUME*`). Nach dem Aufwachen wird die verstrichene Zeit
  nicht nachsimuliert, und Monitore und Grafik werden neu geprüft.
- **Startmenü:** Die Prozessnamen der Shell (`StartMenuExperienceHost`, `SearchHost`, `ShellExperienceHost` …) im
  Vordergrund. Gelesen wird wie bei den App-Regeln nur der Dateiname.

### 2. Grafikkarten-Ausfall

Seit M1 vorhanden (ADR-0003): Das Device wird bei `DEVICE_REMOVED/RESET` neu aufgebaut, bis zu dreimal pro Frame. Die
Simulation in Core bleibt erhalten. Neu ist, dass Entsperren, Aufwachen und Remote-Verbindung ebenfalls eine Prüfung
von Monitoren und Device auslösen.

### 3. Lokale Fehlerberichte

`CrashLog` (Core) schreibt einen Textbericht nach `%LOCALAPPDATA%\Weatherly\logs`, die letzten zehn bleiben.

- **Inhalt:** Version, Windows-Version, Architektur, Ort des Fehlers, voller Stacktrace.
- **Datenschutz:** Profilpfad und Benutzername werden ersetzt. Gesendet wird nichts.
- **Quellen:** unbehandelte Ausnahmen (App, Tasks, WinUI) sowie Fehler, die der Overlay-Thread abfängt und überlebt.
  Letztere landen bisher nur im Status und werden nur einmal pro Sitzung geschrieben.
- **Zugang:** „Erweitert → Fehlerberichte öffnen“.

### 4. Langzeittest

- **Core:** `LongRunTests` simuliert zehn Minuten mit allen Effekten, Besuchern und ständig wechselnden Fenstern. Der
  verwaltete Heap darf nach der Aufwärmphase nicht wachsen.
- **App:** `--soak=<Minuten>` (`SoakRun`) lässt alle Effekte laufen, lädt Besucher ein und schiebt und skaliert das
  Fenster. Alle 30 Sekunden misst er privaten Speicher, verwalteten Heap, Handles und GDI- und USER-Objekte.
  `SoakAnalysis` vergleicht nach der Aufwärmphase den Median des ersten und des letzten Drittels mit einer
  Grundtoleranz plus einer Rate pro Stunde. Der Bericht nennt dazu CPU-Last und Frame-Zeiten.
- **CI:** Ein eigener Job läuft bei jedem Push 5 Minuten. Das fängt nur schnelle Lecks. Längere Läufe gehen über „Run
  workflow“ (höchstens etwa 5,5 Stunden).
- **8 Stunden:** Der volle Lauf geht auf einem echten PC: `pwsh tools/ci/soak-test.ps1 -Minutes 480`.

## Konsequenzen

- Was Windows hier wirklich tut, lässt sich im CI nicht nachstellen: Sperre, Standby, Spiele im exklusiven Vollbild,
  Remotedesktop, Startmenü. Das prüft die Checkliste M13.
- Die Budgets aus PLAN.md, Abschnitt 9 (CPU unter 3 %, Frame unter 2,5 ms auf einer iGPU) gelten für echte Hardware.
  Der CI-Runner rendert mit WARP auf der CPU und liefert nur Vergleichswerte.
