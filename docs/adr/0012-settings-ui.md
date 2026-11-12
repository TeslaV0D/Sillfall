# ADR-0012: Einstellungen, Laufzeitregeln und Oberfläche

- Status: akzeptiert (Optik und Bedienung vorbehaltlich Boss-Abnahme)
- Datum: 2026-11-12
- Meilenstein: M9

## Kontext

PLAN.md, Abschnitt 7 sieht die vollständige Oberfläche vor:

- Tray-Flyout,
- Einstellungsfenster mit zehn Seiten,
- pro Effekt Farbe, Helligkeit und fest/zufällig,
- Vorlagen mit Teilen,
- Einstellungen pro Monitor,
- App-Regeln,
- Leistung und Akku,
- Autostart,
- eine einzige laufende Instanz,
- das Protokoll `weatherly://`,
- versionierte Einstellungen mit Migration.

## Entscheidungen

### 1. Ein Modell, ein Controller

- `AppSettings` (Core) ist das einzige gespeicherte Modell, versioniert. Version 1 aus dem MVP wird beim
  Laden migriert. Werte werden beim Laden bereinigt.
- Der `AppController` (App) ist der einzige Ort, der Einstellungen ändert. Er schreibt sie 0,5 s nach der
  letzten Änderung und wendet sie höchstens alle 60 ms an.
- Aus Einstellungen, Systemzustand und Zufallsläufen entstehen die `OverlayOptions`. Dafür sind
  `RuntimePolicy`, `IntensityDirector` und `RuntimeOptions` in Core zuständig, alle getestet.
- Was der Controller selbst setzt, schreibt er nie in die Einstellungen zurück: Pausen, Zufallswerte und
  den Akku-Faktor.

### 2. Laufzeitregeln

**Pause.** Gründe in dieser Reihenfolge:

1. Hauptschalter,
2. Reduced Motion (immer, oder nach der Windows-Einstellung „Animationen anzeigen“),
3. Akku mit „Pause“,
4. App-Regel für das Programm im Vordergrund.

**Smoothness.**

| Einstellung | Bildrate |
|---|---|
| Auto | 60 fps, im Akkubetrieb 30 |
| Geringster Verbrauch | 30 fps |
| Ausgewogen | 60 fps |
| Am flüssigsten | Monitorrate |

„Reduzieren“ im Akkubetrieb halbiert die Intensität und begrenzt auf 30 fps.

**Systemzustand.** Der Overlay-Host liest Stromquelle, Animationseinstellung und den Dateinamen des
Programms im Vordergrund und meldet Änderungen. Von einem Programm wird nur der Dateiname gelesen, nie ein
Titel.

**Bekannt:** Auf Windows Server, also auch auf dem CI-Runner, sind Animationen standardmäßig aus. Mit
„Windows folgen“ pausiert die App dort. Für Nutzer ist das gewollt. Der Smoke-Test schaltet die
Laufzeitregeln ab.

### 3. Pro Monitor und App-Regeln

- **Monitore:** Ausgeschaltete Monitore bekommen kein Overlay. Die anderen zeichnen mit eigener Deckkraft,
  als Faktor in den Sprite- und Atmosphären-Shadern.
- **„Über diesen Fenstern ausblenden“:** Die Fenster des Programms werden nach dem Zeichnen wieder
  transparent gemacht (ohne Blending). Ausgeschnitten wird das ganze Fensterrechteck, auch wo ein anderes
  Fenster darüber liegt. Ein exakter Zuschnitt lohnt die Komplexität derzeit nicht.

### 4. Oberfläche im Code statt in XAML

Das Einstellungsfenster ist ein `NavigationView` mit zehn Seiten. Das Flyout ist ein kleines, rahmenloses
Fenster mit Acrylic über dem Tray. Beide bestehen aus wenig XAML; die Seiten baut C#-Code aus gemeinsamen
Bausteinen (`Ui`).

- **Warum:** Die WinUI-App baut nur unter Windows. Code-Fehler zeigt das CI zuverlässig, XAML-Fehler oft
  erst zur Laufzeit. Gleiche Bausteine sehen außerdem überall gleich aus.
- **Seiten:** Effekte, Vorlagen, Wetter-Sync (Platzhalter bis M10), Visitors (Platzhalter bis M11), Sound,
  Displays, Apps, Leistung (mit Live-Zahlen), Erweitert, Info.
- **Prüfung:** Der Smoke-Lauf `--settings-tour` zeigt jede Seite und das Flyout und speichert sie als PNG
  in die Previews. Die Seiten lassen sich so ohne Windows-Desktop ansehen.

### 5. Fenster und Lebensdauer

- **Hintergrund-App:** Die App läuft im Infobereich (`DispatcherShutdownMode.OnExplicitShutdown`). Nur
  „Quit“ beendet sie.
- **Klicks auf das Tray-Symbol:** Ein Linksklick öffnet das Flyout, der Menüpunkt „Open“ die
  Einstellungen.
- **Diagnosefenster:** Das bisherige Testfenster (`MainWindow`) bleibt als Diagnosefenster erreichbar
  (Erweitert). Es gehören ihm weiter Overlay-Host, Tray-Icon und die Single-Instance-Sperre. Der
  Smoke-Test steuert es.
- **Ohne Tray-Icon:** Lehnt die Shell das Icon ab, beendet das Schließen der Einstellungen die App. Sonst
  bliebe sie unerreichbar im Hintergrund.

### 6. Eine Instanz, Links, Autostart

- **Eine Instanz:** Ein benannter Mutex pro Sitzung und ein verstecktes Nachrichtenfenster. Ein zweiter
  Start schickt seine Kommandozeile per `WM_COPYDATA` an die laufende Instanz und beendet sich.
- **`weatherly://scene?d=…`:** Bei MSIX steht das Protokoll im Manifest. Unverpackt registriert die App es
  bei jedem Start für den Benutzer. Ein Link wird bereinigt, angewendet und als Vorlage behalten.
- **Autostart:** Bei MSIX über den `StartupTask` (standardmäßig aus), unverpackt über den Run-Schlüssel.
  Der Autostart-Weg startet mit `--background` nur im Infobereich.

## Konsequenzen

- Was Windows wirklich tut, prüft nur die Abnahme auf echter Hardware (Checkliste M9):
  - Flyout-Position und Fokus-Verhalten,
  - Autostart über MSIX,
  - Protokoll-Aktivierung,
  - Akku-Wechsel,
  - eine App im Vordergrund.
- Die Sprache der Oberfläche ist vorerst Englisch. Deutsch folgt mit M12 über `.resw`.
