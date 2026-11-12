# ADR-0014: Visitors

- Status: akzeptiert (Teil 1: Core. Zeichnen, Hit-Fenster und Oberfläche folgen)
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

## Konsequenzen

- Ob Tempo, Sprungweite, Häufigkeit und Besuchsdauer sich gut anfühlen, zeigt erst die Abnahme. Alle Werte stehen als
  Konstanten in `VisitorSystem`, `VisitorCatalog` und `VisitorScheduler`.
