# ADR-0009: Laub

- Status: akzeptiert (Optik vorbehaltlich Boss-Abnahme)
- Datum: 2026-11-11
- Meilenstein: M6

## Kontext

PLAN.md, Abschnitt 4.3:

- Blätter mit Flatter-Physik und Pseudo-3D-Wenden,
- Liegen auf Oberflächen mit Obergrenze pro 100 px,
- Cursor-Böe und Schütteln.

## Entscheidungen

### 1. Blattform als Sprite-Form

Wie bei den Schneeflocken zeichnet der Sprite-Shader die Blätter direkt (`SpriteShape.Leaf`). Es gibt vier
Varianten: spitzes Oval, Ahorn, Eiche und schmales Blatt. Alle haben eine dunklere Mittelrippe und einen
Stiel. Es braucht keine Textur und kein Asset.

Die Farben kommen aus einer eigenen Herbstpalette: Rot, Orange, Goldgelb, Ocker, Braun und ein spätes
Grün. Eine eigene Farbe in den Einstellungen ersetzt die Palette.

### 2. Flatter-Physik

- **Wenden:** Jedes Blatt hat eine Wende-Phase. Seine Breite folgt |cos(Phase)|, darunter liegt eine
  Untergrenze, damit es nie ganz verschwindet. Die Rückseite ist etwas dunkler.
- **Seitliche Kraft:** Der Auftrieb folgt sin(Phase) und schiebt das Blatt seitlich. Das ergibt das Pendeln.
- **Fallen:** Steht das Blatt flach zur Luft (cos² groß), fällt es langsamer.
- **Bremsen:** Luftwiderstand zieht das Blatt zur Windgeschwindigkeit. Die Fallgeschwindigkeit liegt so
  bei etwa 80–100 px/s × DPI.

### 3. Tiefe

70 % der Blätter sind nah. Der Rest ist kleiner, dunkler und fällt hinter den Fenstern. Das ist dieselbe
Regel wie bei Regen und Schnee.

### 4. Liegen

- **Lage:** Ein Blatt liegt flach entlang der Kante, leicht verkippt, und wandert mit seinem Fenster.
- **Verdeckung:** Verdeckt ein höheres Fenster die Stelle, ist das Blatt dort nicht zu sehen.
- **Obergrenze:** Eine Kante ist in feste 100-px-Abschnitte geteilt. Liegen in einem Abschnitt schon vier
  Blätter, rutscht dort das älteste ab. Ein Fenster rund um jede Landestelle reichte nicht: Es ließ Haufen
  mit bis zu sieben Blättern pro 100 px zu. Ein Test hat das gefunden.
- **Nach dem Ausschalten:** Liegende Blätter blenden in 12 s aus. Danach stoppt der Render-Loop.

### 5. Cursor-Böe

- **Abtasten:** `OverlayHost` liest einmal pro Frame `GetCursorPos`, ohne Hook. Das passiert nur, solange
  der Render-Loop läuft.
- **Glätten:** `CursorMotion` glättet die Geschwindigkeit über etwa 50 ms. Ein Sprung schneller als
  20 000 px/s ist ein Teleport und keine Böe.
- **Wirkung:** Ab 600 px/s × DPI bekommen Blätter im Umkreis von 90 px × DPI einen Stoß. Er wird nach
  außen schwächer. Liegende Blätter heben dabei ab.

Schwelle und Reichweite sind geschätzt. Die Abnahme muss sie bestätigen.

### 6. Schütteln und Schließen

Schütteln wirft alle Blätter des Fensters hoch und zur Seite. Schließen lässt sie fallen. Wie beim Schnee
landen sie nicht wieder auf dem Fenster, von dem sie kommen.

## Konsequenzen

- Der Cursor steht jetzt allen Effekten zur Verfügung (`SimulationContext.Cursor`, `CursorVelocity`).
  Gras (M8) kann ihn fürs Wegdrücken nutzen.
- **Sandbox:** Mit `--effect=leaves --scatter-leaves` oder den Tasten L und K lassen sich die Blätter
  testen. Die Maus über eine Kante ziehen oder ein Fenster schütteln.
