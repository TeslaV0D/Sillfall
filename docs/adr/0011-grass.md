# ADR-0011: Gras

- Status: akzeptiert (Optik vorbehaltlich Boss-Abnahme)
- Datum: 2026-11-11
- Meilenstein: M8

## Kontext

PLAN.md, Abschnitt 4.6:

- Halme pro Oberfläche, die Dichte richtet sich nach der Breite,
- Wachstum bis etwa 18 px × DPI, bei Regen schneller,
- Wiegen im Wind, optional Ausweichen vor dem Cursor,
- beim Schließen ausblenden statt abwerfen.

## Entscheidungen

### 1. Halm als Sprite-Form

Der Plan nennt sich verjüngende Quads aus 3 Segmenten, die der Vertex-Shader biegt. Umgesetzt ist eine
Sprite-Form `Blade`: ein aufrechtes Quad, in dem der Pixel-Shader einen sich verjüngenden Halm zeichnet.
Die Mittellinie biegt sich mit dem Quadrat der Höhe zur Seite. Das sieht aus wie eine Biegung um die Wurzel.
Die Stärke steckt in `Sprite.Variant`, das Quad wird nach `Sprite.BladeHalfHeight` dafür breit genug gemacht.

Ein Sprite pro Halm statt drei hält die Sprite-Zahl klein: etwa 250 Halme pro 1000 px Kante. Die Wurzel
ist dunkler als die Spitze.

### 2. Streifen pro Oberfläche

- **Anlegen:** Jede Fensteroberkante und jeder Boden bekommt beim ersten Gras einen Streifen mit etwa
  einem Halm pro 4 px × DPI. Speicher wird nur dabei angelegt und wenn ein Fenster breiter wird.
- **Lage:** Die Halme sind relativ zum linken Ende gespeichert und wandern mit dem Fenster.
- **Größe ändern:** Wird das Fenster schmaler, verschwinden die Halme jenseits der neuen Kante. Wird es
  breiter, sprießen neue nach.
- **Wiedererkennbar:** Jede Position bekommt immer denselben Halm, weil der Zufall pro Position gesät wird.
  Wächst ein Fenster wieder, sieht das Gras deshalb nicht neu gewürfelt aus.
- **Büschel:** Eine langsame Welle über die Position macht hohe und niedrige Abschnitte.

### 3. Wachstum

Das Gras wächst mit 0,8 px/s × DPI × Intensität, mal (1 + 2 × Regen) laut Plan. Ohne Regen ist es nach
etwa 20 s ausgewachsen, mit vollem Regen dreimal so schnell. Der Wert ist geschätzt: „Wachsen sehen“ ist
reizvoll, sollte aber nicht ewig dauern.

### 4. Bewegung

- **Wind:** Der Wind neigt alle Halme. Darüber liegt eine langsame Welle über die Breite, jeder Halm hat
  seine eigene Phase.
- **Cursor:** Halme im Umkreis von 36 px × DPI biegen sich vom Cursor weg, nach außen schwächer.
  Gelesen wird der Cursor, den M6 einführt.

### 5. Ende

- **Fenster geschlossen:** Sein Gras blendet an der alten Stelle in 0,6 s aus.
- **Gras ausgeschaltet:** Es schrumpft und verblasst in 3 s, danach stoppt der Render-Loop.

## Konsequenzen

Solange Gras an ist, läuft der Render-Loop dauerhaft, weil es sich wiegt. Das ist bei jedem aktiven Effekt
so. Die Leistung prüft die Abnahme.
