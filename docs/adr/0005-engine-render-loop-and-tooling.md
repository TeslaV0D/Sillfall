# ADR-0005: Engine, Render-Loop und Werkzeuge

- Status: akzeptiert
- Datum: 2026-11-11
- Meilenstein: M3

## Kontext

Ab M4 kommen die Effekte. Vorher brauchen sie ein Fundament (PLAN.md, M3): fester Zeitschritt,
Render-Loop mit Idle-Stopp, ein Datenformat zwischen Simulation und GPU, Instancing, Direct2D,
eine Sandbox und Golden-Images.

## Entscheidungen

### 1. Engine in Core, Loop im Host

`SimulationEngine` (Core) kennt Monitore, Fenster, Oberflächen, Wind und die Effekte. Sie läuft mit
festem Schritt (`FixedStepClock`, 1/120 s, höchstens 8 Schritte pro Frame) und baut pro Frame ein
`RenderFrame` aus `Sprite`s. Sie ist plattformneutral und läuft identisch in der App, der Sandbox,
FrameDump und den Tests.

Der Render-Loop lebt im `OverlayHost` auf dem Overlay-Thread:

- **Idle:** blockiert in `GetMessage`. Keine Frames, keine GPU-Arbeit.
- **Aktiv** (mindestens ein Effekt hat etwas zu tun): leert die Message-Queue ohne zu blockieren und
  rendert einen Frame pro Durchlauf. Getaktet wird über das Frame-Latency-Waitable der Swap-Chain
  (MaxFrameLatency 1). Als Schutz vor einer Busy-Loop gilt eine Obergrenze von 240 fps.
- Hat kein Effekt mehr etwas zu tun, stoppt der Loop selbst und präsentiert noch einen leeren Frame.

### 2. Sprites als einziges Format zwischen Simulation und GPU

Ein `Sprite` ist 48 Bytes: Mitte, halbe Größe, Rotation, Form (Rechteck, weiche Scheibe, Streifen),
premultiplied Farbe. Alle Partikel-Effekte liefern Sprites. Die GPU zeichnet sie mit einem
instanzierten Draw aus einem Structured Buffer (`SpriteBatch`). Pro Monitor wird vorher auf der CPU
gecullt. Flächige Effekte (Nebel, Sonne) bekommen ab M7 eigene Pässe.

Effekte schreiben in Struct-of-Arrays mit fester Kapazität. Damit allokiert der Frame-Loop nichts.
Geprüft wird das zweimal: im Core-Test (Engine, 600 Frames) und live im Smoke-Test auf Windows (Host
misst `GC.GetAllocatedBytesForCurrentThread` nach 60 Aufwärm-Frames, Ergebnis: 0 Bytes).

### 3. Kalibrier-Tropfen statt sofort Regen

`TestDropsEffect` ist ein absichtlich einfacher Effekt: Tropfen fallen, landen auf Kanten, wandern mit
dem Fenster, fallen beim Schütteln oder Schließen ab und blenden aus. Er prüft jede Engine-Funktion,
bevor M4 die echte Regen-Optik baut, und bleibt danach als Diagnose-Effekt.

### 4. Direct2D

`Direct2DCanvas` legt Direct2D über DXGI-Surface-Interop auf dasselbe Device. Gezeichnet wird in jede
BGRA-Textur, also auch in Swap-Chain-Backbuffer. Genutzt wird es ab M11 (Visitors, Share-Card).
DirectWrite (Text) kommt mit dem ersten Text.

### 5. Werkzeuge

- **Sandbox** (`tools/Weatherly.Sandbox`): normales Fenster mit Fake-Desktop und ziehbaren
  Fake-Fenstern, dieselbe Engine und derselbe Renderer wie die App.
- **FrameDump** (`tools/Weatherly.FrameDump`): spielt JSON-Szenen (`Scenes/*.json`) deterministisch ab
  und rendert die Capture-Zeitpunkte mit WARP als PNG plus Kontaktbogen.
- **Golden-Images:** `GoldenImageTests` rendern dieselben Szenen und vergleichen mit
  `tests/Weatherly.Render.Tests/Goldens/` (Toleranz 2 pro Kanal, höchstens 0,1 % der Pixel).
  Fehlt ein Golden, schlägt der Test fehl und legt das Bild in die Previews. Übernommen wird es erst
  nach Sichtprüfung.

### 6. Abweichung: kein Branch `ci-previews/<branch>`

PLAN.md sieht vor, dass die CI Vorschauen auf einen eigenen Branch pusht. Die Session darf nur auf
ihren zugewiesenen Branch schreiben. Ein CI-Job mit Schreibrecht auf andere Branches würde das
umgehen. Stattdessen landen alle Vorschauen (Render-Tests, FrameDump, Desktop-Screenshots) im
CI-Artefakt `previews`. Der Agent lädt es herunter und sieht sich die PNGs direkt an. Das erfüllt
denselben Zweck.

## Konsequenzen

- Effekte implementieren nur `IEffectSystem` und liefern Sprites. Render-Code brauchen sie nicht.
- Neue Szenen für Vorschauen und Goldens sind JSON-Dateien, kein Code.
- Ein Golden ändert sich nur bewusst: Bild prüfen, aus dem Artefakt übernehmen, committen.
