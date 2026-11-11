# ADR-0010: Nebel und Sonnenlicht

- Status: akzeptiert (Optik vorbehaltlich Boss-Abnahme)
- Datum: 2026-11-11
- Meilenstein: M7

## Kontext

PLAN.md, Abschnitte 4.4 und 4.5:

- Nebel als fBm-Rauschen mit Domain-Warping in Viertelauflösung,
- God-Rays mit einer Maske der Fenster,
- Glühen und Staub.

Bis M6 bestand jeder Frame nur aus Sprites. Für Vollbild-Effekte brauchte es einen zweiten Weg vom Core
zum Renderer.

## Entscheidungen

### 1. Parameter statt Sprites

`RenderFrame` trägt zwei Ebenen: `FogLayer` (Dichte, Drift, Phase, Farbe, Maximaldeckung) und `SunLayer`
(Stärke, Position, Farbe). Dazu kommen die Fenster-Rechtecke als Verdecker, höchstens 64, die obersten zuerst.

- Core simuliert nur diese wenigen Zahlen: Nebel zieht ein und ab, driftet mit dem Wind und atmet, die
  Sonne wandert langsam.
- Gezeichnet wird im Renderer. Damit bleibt die Simulation testbar und frei von Windows.
- Die Verdecker füllt die Engine nur, solange Nebel oder Sonne sichtbar sind.

### 2. Ein Durchlauf in Viertelauflösung

`AtmospherePass` (Render) zeichnet in drei Schritten:

1. Eine Maske der Fenster in Viertelauflösung: Himmel weiß, Fenster schwarz.
2. Nebel und Strahlen in einem einzigen Pixel-Shader in Viertelauflösung, in eine Half-Float-Textur.
   8 Bit würden beim dünnen Nebel Stufen zeigen.
3. Bilineare Hochskalierung über das Ziel, premultiplied.

Nebel und Sonne liegen hinter allen Sprites (PLAN.md, Abschnitt 4.7). Die Texturen werden pro
Monitorgröße zwischengespeichert, für bis zu 4 Größen.

### 3. Nebel

- **Form:** 4 Oktaven Value-Noise mit Domain-Warping. Die Merkmalsgröße ist etwa 380 px × DPI. Der
  Nebel wandert mit 60 % des Windes plus einer Grunddrift von 6 px/s.
- **Höhe:** Nach unten wird er dichter.
- **Deckung:** höchstens 35 %, über Fenstern nur 45 % davon. Ein heller Saum über den Oberkanten lässt
  die Fenster aus dem Nebel ragen.

### 4. Sonnenlicht

- **Position:** Die Sonne steht knapp über der oberen rechten oder linken Ecke des Desktops und wandert
  über Minuten um wenige Prozent.
- **Strahlen:** 48 Schritte durch die Maske in Richtung Sonne, mit Abklingen 0,965. Der Anteil freien
  Himmels, quadriert, ergibt die Strahlen. Fenster werfen dadurch lange Schatten.
- **Glühen:** um die Sonne. Über Fenstern bleibt nur 35 % des Lichts übrig, damit Inhalte lesbar bleiben.
- **Staub:** 100 Partikel schweben im Lichtkegel. Sie funkeln und werden zur Sonne hin heller.

Additives Licht über einem transparenten Overlay ist nur bedingt möglich. Das Licht wird deshalb als
warmer Farbanteil mit wenig Deckung ausgegeben (Alpha = 30 % der Helligkeit). Ob das auf echten Desktops
warm statt milchig wirkt, entscheidet die Abnahme.

### 5. Ohne Wetter-Sync

Das Tag-Nacht-Verhalten (`is_day`) und die Stärke nach Bewölkung kommen mit M10. Die Seite der Sonne ist
im Code einstellbar (`SunEffect.Side`). In den Einstellungen erscheint sie mit M9.

## Konsequenzen

- Die GPU-Kosten wachsen: Nebel kostet etwa 60 ALU-Operationen pro Viertelpixel, die Sonne 48
  Texturzugriffe. Gemessen wird auf Referenz-Hardware, siehe Checkliste M7.
- Golden-Images `fog`, `sun` und `sun-and-fog` sowie WARP-Tests für Verdeckung, Höhenverlauf und Schatten.
