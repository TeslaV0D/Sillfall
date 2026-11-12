# ADR-0015: Sprachen und Barrierefreiheit

- Status: akzeptiert
- Datum: 2026-11-12
- Meilenstein: M12

## Kontext

PLAN.md, M12 verlangt:

- Narrator-Beschriftungen,
- hohen Kontrast,
- Reduced Motion,
- Zustände, die nicht nur an der Farbe hängen,
- Texte als `.resw` auf Englisch und Deutsch.

Abnahme: Accessibility Insights ohne kritische Befunde.

## Entscheidungen

### 1. Texte: `.resw`, aber ohne das Ressourcensystem von Windows

Alle Texte der Oberfläche stehen in `src/Weatherly.Core/Localization/Strings/{en-US,de-DE}/Resources.resw`. `Texts`
(Core) liest sie mit einem kleinen ResX-Leser aus den eingebetteten Dateien. WinUIs MRT nutzen wir nicht. Gründe:

- verpackt, unverpackt und in Tests gleich,
- auch Core-Texte sind übersetzbar (Tray-Menü, Pausengründe, Vorlagennamen, Erfolge),
- keine `resources.pri`-Erzeugung im Build.

Das Format bleibt `.resw`, also können Übersetzungswerkzeuge es bearbeiten.

### 2. Sprache

- **Wahl:** Die Sprache folgt Windows (Anzeigesprache) oder der Einstellung „Erweitert → Sprache“.
- **Wechsel:** Er wirkt sofort. Die offene Seite wird neu gebaut, Menüs und Flyout zeigen die Sprache beim nächsten
  Öffnen.
- **Fehlende Texte:** Englisch ist die Basis. Fehlt ein deutscher Text, erscheint der englische. Ein unbekannter
  Schlüssel erscheint als er selbst, damit er auffällt.
- **Bleiben englisch:** Diagnosefenster und Smoke-Bericht. Das sind Werkzeuge für Entwickler.

### 3. Tests halten die Sprachen zusammen

`LocalizationTests` prüft:

- gleiche Schlüssel in beiden Dateien,
- keine leeren Texte,
- gleiche Platzhalter,
- Deutsch wirklich übersetzt,
- jeder Schlüssel, den der Code per `Texts.Get`/`Texts.Format` verlangt, existiert (aus den Quellen gelesen),
- jeder aus Enums gebaute Schlüssel existiert.

Ein neuer Text ohne Übersetzung bricht also den Build.

### 4. Narrator

- **Namen:** Jedes Bedienelement bekommt einen Namen (`AutomationProperties.Name`). Regler, Schalter und Auswahlen
  tragen ihre Beschriftung. Die Regler im Flyout heißen „Regen, Stärke“. Die Effekt-Ausklapper tragen den Effektnamen.
- **Bilder:** Die Besucherbilder tragen den Besuchernamen. Reine Deko-Icons sind für Screenreader ausgeblendet.
- **Prüfung:** Der Smoke-Test läuft jede Einstellungsseite und das Flyout ab. Hat ein per Tab erreichbares
  Bedienelement laut UI Automation keinen Namen, schlägt er fehl (`AccessibilityCheck`).
- **Besucher:** Den Weg für Narrator haben M11 und ADR-0014 gebaut: Tooltip, Tray-Menü, Strg+Alt+V, Hinweis.

### 5. Hoher Kontrast

Die Oberfläche nutzt nur Theme-Ressourcen. Im hohen Kontrast übernimmt WinUI deshalb die Systemfarben.

Halbtransparentes Wetter über dem Bildschirm senkt aber genau den Kontrast, den diese Nutzer eingeschaltet haben.
Darum pausiert das Wetter im hohen Kontrast. Das lässt sich unter „Erweitert“ abschalten, und die Erkennung läuft über
`SPI_GETHIGHCONTRAST`. Reduced Motion gibt es seit M9.

### 6. Zustände nicht nur über Farbe

Jeder Zustand hat Text oder Form:

- Schalter zeigen „An/Aus“.
- Erfolge zeigen Häkchen oder Kreis und „(noch nicht)“.
- Die Sammlung sagt „Noch nicht getroffen“.
- Der Sync-Status ist Text.
- Der Korb wird beim Treffer größer, nicht nur heller.

### 7. Teilen

Die Share Card öffnet jetzt auch das Teilen-Fenster von Windows. Für Desktop-Apps geht das über das Fenster-Handle
(`DataTransferManagerInterop`). Das Bild wird als PNG-Datei und als Bitmap übergeben.

## Konsequenzen

- Neue Texte kommen immer über `Texts.Get("Bereich.Name")` in beide `.resw`-Dateien.
- Accessibility Insights ist ein Werkzeug auf echtem Windows. Der Smoke-Test deckt die häufigste kritische Klasse ab
  (Bedienelemente ohne Namen). Den Rest prüft die Checkliste M12.
