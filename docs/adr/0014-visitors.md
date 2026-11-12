# ADR-0014: Visitors

- Status: akzeptiert
- Datum: 2026-11-12
- Meilenstein: M11

## Kontext

PLAN.md, Abschnitt 6: Eigene Figuren besuchen den Desktop, laufen auf Fenstern, reagieren auf das Wetter und lassen
sich (opt-in) einsammeln. Boss hat Stil und Figuren abgenommen: flach-geometrisch, acht Figuren mit je einem
Lieblingswetter.

| Figur | Was | Lieblingswetter | Seltenheit |
|---|---|---|---|
| Pip | Schnecke mit Blatthut | Regen | häufig |
| Tuck | Pinguin mit Schal | Schnee | gelegentlich |
| Bristle | Igel mit Laub | Laub | häufig |
| Lumen | Glühwürmchen (schwebt) | Nebel | selten |
| Sol | Katze mit Sonnenbrille | Sonne | gelegentlich |
| Drift | Wölkchen auf Beinen | Wind | häufig |
| Sprout | Topfpflanze auf Füßen | Gras | gelegentlich |
| Nim | Maus mit Schirm | jedes Wetter | häufig |

## Entscheidungen

### 1. Verhalten als Effekt

`VisitorSystem` ist ein `IEffectSystem`. Es läuft wie die Wettereffekte, ohne Allokationen und mit höchstens drei
Figuren gleichzeitig. Die Zustände sind:

- Auftauchen,
- Laufen,
- Stehen,
- in der Luft,
- gehalten,
- beleidigt,
- Gehen.

So verhalten sich die Figuren:

- **Auf Fenstern:** Eine Figur steht relativ zu ihrem Fenster, fährt also beim Verschieben mit. Sie läuft nur auf dem
  sichtbaren Teil der Oberkante.
- **Am Ende der Kante:** Sie springt in einem Bogen zu einem Fenster in Reichweite (150 px seitlich, 90 px hoch) oder
  dreht um. Die Landung erkennt dieselbe Suche, die Regen und Schnee nutzen.
- **Fenster zu oder geschüttelt:** Die Figur fällt und landet auf dem nächsten Fenster oder der Taskleiste.
- **Wetter:** Ein Requisit kommt zum stärksten Wetter (Schirm, Schneeball, Sonnenbrille, Laterne, Blatt). Im
  Lieblingswetter braucht eine Figur keins: Sie freut sich und hüpft.
- **Besuchsdauer:** Nach 40 bis 90 Sekunden geht die Figur von selbst.

### 2. Planung über die Wanduhr

Der Render-Loop schläft, solange nichts zu sehen ist (ADR-0005). Deshalb plant `VisitorScheduler` mit der Uhr des
Hosts.

- **Ablauf:** Der Host fragt im Leerlauf, ob ein Besuch fällig ist, und weckt dann den Loop.
- **Abstände:** Selten 30 Minuten, manchmal 10 (Standard), oft 3, jeweils ±50 %. Bei „oft“ dürfen zwei Figuren
  gleichzeitig da sein.
- **Wer kommt:** Gewichtet nach Seltenheit. Das Lieblingswetter macht eine Figur bis zu viermal so wahrscheinlich.

### 3. Daten statt Zeichenbefehle

Der Simulationsteil liefert pro Figur eine `VisitorPose` im `RenderFrame`:

- Ort,
- Größe,
- Blickrichtung,
- Neigung,
- Stauchung,
- Wippen,
- Schrittphase,
- Blinzeln,
- Requisit,
- Gesichtsausdruck,
- Deckkraft.

Der D2D-Renderer und die Hit-Fenster brauchen so keinen Simulationscode.

### 4. Sammlung

`VisitorAlbum` wird lokal in `collection.json` neben den Einstellungen gespeichert, nie im Netz. Im Code heißt es
„Album“, weil der Analyzer Typnamen auf „…Collection“ für Listen reserviert.

- **Punkte:** nach Seltenheit 10, 25 oder 50, doppelt im Lieblingswetter.
- **Combo:** Ein Einsammeln innerhalb einer Minute nach dem letzten erhöht den Faktor um 0,5, höchstens auf ×3.
- **Erfolge:**
  - erster Gast,
  - zehn Besucher,
  - alle acht,
  - die Seltene,
  - im Lieblingswetter,
  - fünf Wetterarten,
  - drei in Folge,
  - 100 Punkte.

### 5. Zeichnen mit dem SVG-Renderer von Direct2D

Die Figuren sind SVGs mit benannten Teilen (`eyes`, `foot-left`, `foot-right`). `VisitorPainter` zerlegt sie beim
ersten Gebrauch in einzelne `ID2D1SvgDocument`s und bewegt die Teile getrennt:

- Die Füße wechseln sich beim Laufen ab.
- Der Körper wippt.
- Die Augen blinzeln.
- Stauchen und Neigen beim Springen.
- Gespiegelt nach Laufrichtung.

Gesichter (Bogenaugen, Brauen) und die Sonnenbrille entstehen als kleine SVGs aus den Augenpositionen (`VisitorArt`).
Spritesheets und eigener Geometrie-Code sind nicht nötig, und die Figuren bleiben bei jeder DPI scharf. Direct2D startet
erst mit dem ersten Visitor.

### 6. Hit-Fenster und Korb

Das Overlay bleibt click-through.

- **Hit-Fenster:** Im Interaktiv-Modus (opt-in) folgt jedem Visitor ein eigenes kleines Fenster. Es ist geschichtet
  mit Alpha 1/255: unsichtbar, aber anklickbar (Alpha 0 ließe Klicks durch). Es ist topmost und wird nie aktiviert.
- **Tracking:** Die Hit-Fenster tragen den Klassen-Präfix des Overlays, deshalb sieht das Fenster-Tracking sie nicht.
- **Greifen:** Beim Greifen fängt das Hit-Fenster die Maus (`SetCapture`). Der Korb erscheint unten rechts im
  Arbeitsbereich des Hauptmonitors und leuchtet, solange die Figur darüber ist. Loslassen über dem Korb sammelt sie ein,
  anderswo fällt sie, schmollt und geht.
- **Sauberkeit:** Hat ein anderes Fenster die Maus übernommen, wird die Figur einfach losgelassen. Gehende Figuren
  verlieren ihr Hit-Fenster sofort.

### 7. Ohne Maus

- **Tastenkürzel:** Strg+Alt+V (`RegisterHotKey`) sammelt die erste Figur ein. Ist das Kürzel belegt, sagt die
  Einstellungsseite das.
- **Tray-Menü:** Es hat dafür einen Eintrag, solange eine Figur da ist.
- **Tooltip:** Der Tooltip des Tray-Icons nennt die Figur und den Weg zum Einsammeln. Narrator liest ihn vor.
- **Hinweise:** Wahlweise kommt eine Benachrichtigung bei jeder Ankunft. Eingesammelte Figuren melden Punkte und Erfolge
  immer still per Benachrichtigung, und Windows hält sie bei „Nicht stören“ zurück.

### 8. Sounds, Share Card

- **Sounds:** Drei prozedurale Klänge pro Figur (Ankommen, Einsammeln, Schmollen), Sinustöne mit eigener Tonhöhe pro
  Figur (`VisitorSynth`). Es gibt keine Samples, also auch keine Lizenzfragen. Sie sind nur zu hören, wenn der Sound an
  ist (Lautstärke „Visitors“).
- **Share Card:** Die Karte ist ein XAML-Element der Visitors-Seite. Sie wird über `RenderTargetBitmap` als PNG in die
  Zwischenablage oder in eine Datei gespeichert. PLAN.md nennt dafür Direct2D. XAML liefert dasselbe Bild ohne zweiten
  Text-Renderer, die Figuren kommen aus denselben SVGs (`SvgImageSource`).
- **Teilen-Dialog:** Das Teilen-Fenster von Windows (`DataTransferManager`) fehlt noch: Es braucht für Desktop-Apps
  eigene Interop und folgt mit M12. Kopieren und Speichern decken das Teilen bis dahin ab.

## Konsequenzen

- Das Greifen über Hit-Fenster ist echtes Windows-Verhalten: Der Smoke-Test zieht eine Figur mit echter Maus-Eingabe in
  den Korb. Wie es sich mit mehreren Monitoren, Touch und Stift anfühlt, prüft die Checkliste M11.
- Ob Tempo, Sprungweite, Häufigkeit und Besuchsdauer sich gut anfühlen, zeigt erst die Abnahme. Alle Werte stehen als
  Konstanten in `VisitorSystem`, `VisitorCatalog` und `VisitorScheduler`.
