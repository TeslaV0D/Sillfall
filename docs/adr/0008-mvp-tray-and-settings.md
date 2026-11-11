# ADR-0008: MVP-Cut – Tray-Icon und gespeicherte Einstellungen

- Status: akzeptiert
- Datum: 2026-11-11
- Meilenstein: MVP-Cut (nach M5, PLAN.md, Abschnitt 12)

## Kontext

Nach M5 soll laut Plan eine erste Version für den Alltag stehen: M0–M5 plus ein minimales Tray-Menü.
Bisher war die App ein Testfenster. Schließen beendete das Wetter, und nichts blieb gespeichert.

## Entscheidungen

### 1. Tray-Icon über Win32

`TrayIcon` (Platform) benutzt `Shell_NotifyIcon` mit einem versteckten Fenster auf dem UI-Thread der App.
WinUI 3 bringt kein Tray-Icon mit, und ein Fremdpaket lohnt sich für ein Icon mit Menü nicht.

- Es ist ein unsichtbares Top-Level-Fenster und kein Message-Only-Fenster. Nur Top-Level-Fenster bekommen
  `TaskbarCreated`, und damit kommt das Icon nach einem Explorer-Neustart wieder. Unsichtbare Fenster
  ignoriert das Fenster-Tracking.
- `Shell_NotifyIcon` und `NOTIFYICONDATAW` sind selbst deklariert. CsWin32 erzeugt sie nur für eine feste
  CPU-Architektur, weil das Struct auf x86 anders gepackt ist. Die App erscheint nur für x64 und ARM64,
  dort gilt dasselbe Layout.
- Das Menü (`TrayMenu`, Core) ist ein Datenmodell und getestet. Es wird bei jedem Öffnen neu gebaut und
  zeigt deshalb immer den aktuellen Stand.

Menü:

- „Open Weatherly“ (auch Linksklick),
- Regen und Schnee jeweils Aus, Leicht, Mittel, Stark,
- Sound,
- „Pause all effects“,
- „Quit“.

Der Tooltip sagt, was gerade läuft.

### 2. Schließen versteckt das Fenster

Mit Tray-Icon versteckt Schließen das Fenster nur, das Wetter läuft weiter. Erst „Quit“ im Tray beendet
die App. Lehnt die Shell das Icon ab, verhält sich das Fenster wie bisher. Sonst gäbe es keinen Weg mehr
zurück.

### 3. Einstellungen als JSON

`SettingsStore` (Core) speichert `UserSettings` in `%LOCALAPPDATA%\<AppInfo.Name>\settings.json`:

- Pause,
- Regen,
- Regen-Tiefe,
- Schnee,
- Sound.

Geschrieben wird 0,5 s nach der letzten Änderung, damit ein gezogener Regler nicht bei jedem Schritt
eine Datei schreibt. Die Datei wird erst als Temp-Datei geschrieben und dann ausgetauscht. Eine
kaputte oder handbearbeitete Datei ergibt Standardwerte bzw. geklemmte Werte und nie einen Absturz.
`Version` ist für Migrationen vorgesehen, sobald die Einstellungen mit M9 wachsen.

Der Smoke-Test liest und schreibt keine Einstellungen. Er startet immer im selben Zustand.

### 4. Kein Testmuster mehr beim normalen Start

Bisher zeigte die App beim Start das Kalibriermuster. Beim normalen Start bleibt es jetzt aus, im Testfenster
lässt es sich weiter einschalten. Der Smoke-Test verlangt es weiterhin.

## Nicht im MVP

Diese Punkte folgen mit M9:

- Autostart,
- Tray-Flyout,
- Einstellungsfenster,
- Farben,
- Random-Modus.
