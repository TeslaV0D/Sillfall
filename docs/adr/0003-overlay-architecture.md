# ADR-0003: Overlay-Architektur

- Status: akzeptiert
- Datum: 2026-11-11
- Meilenstein: M1

## Kontext

PLAN.md, Abschnitt 3.1 legt die Technik fest: ein Fenster pro Monitor, DirectComposition, premultiplied
Alpha, Click-Through über `WS_EX_TRANSPARENT`. Offen waren: auf welchem Thread die Overlays leben,
wie Monitor-Änderungen behandelt werden und wie sich das Ganze ohne Windows-Desktop prüfen lässt.

## Entscheidungen

### 1. Eigener Overlay-Thread mit eigener Message-Loop

`OverlayHost` startet einen Thread mit Win32-Message-Loop. Dort leben alle Overlay-Fenster, das
D3D11-Device und die DirectComposition-Objekte. Die App spricht nur über thread-sichere Methoden
(`Start`, `Apply`, `GetStatus`, `Dispose`) mit dem Host.

Gründe: Die Overlays hängen nicht am WinUI-Dispatcher. Ein blockierter UI-Thread lässt sie nicht
einfrieren, und das Tool-Fenster bzw. später der Tray funktioniert unabhängig davon. Außerdem bleibt
der Host in der Sandbox-App und in Tests ohne WinUI nutzbar.

### 2. Schichtentrennung über `IOverlayRenderer`

Platform (Fenster) und Render (D3D/DComp) kennen sich nicht. Der Vertrag `IOverlayRenderer` /
`IOverlaySurface` liegt in Core. Die App verdrahtet beides. So bleibt die Regel „Platform, Render
und Audio kennen nur Core“ aus `ArchitectureTests` gültig.

### 3. Monitor-Änderungen: Neuaufbau statt Anpassen

Bei `WM_DISPLAYCHANGE`, `WM_DPICHANGED` und Änderungen der Arbeitsfläche wird nach 250 ms Ruhe
neu geplant (`OverlayLayoutPlanner`, in Core getestet). Ein Overlay, dessen Monitor Position,
Größe oder DPI geändert hat, wird zerstört und neu erstellt, statt Fenster und Swap-Chain
anzupassen. Das passiert selten und hinterlässt keinen veralteten Zustand. Ändern sich nur
Arbeitsfläche oder Primär-Flag, bleibt das Overlay bestehen.

Identität eines Monitors ist der Gerätename (`\\.\DISPLAY1`). Wechselt Windows beim Umstecken
den Namen, wird das Overlay neu erstellt. Das ist harmlos.

### 4. Device-Lost

Meldet `Present` `DXGI_ERROR_DEVICE_REMOVED/RESET/HUNG`, werden alle Surfaces freigegeben,
das Device neu erstellt und die Surfaces neu angelegt. Nach drei erfolglosen Versuchen bleibt
der Fehler im Status sichtbar. Ein Test dafür auf echter Hardware steht in der Checkliste M1.

### 5. Kein Render-Loop in M1

M1 zeigt ein statisches Testmuster. Es wird nur bei Änderungen gezeichnet (Start, Optionen,
Monitore). Damit ist „0 % GPU im Idle“ trivial erfüllt und messbar: Der Smoke-Test vergleicht
die Frame-Zähler zweier Messungen. Der Frame-Loop mit festem Zeitschritt kommt in M3.

### 6. Verifikation ohne Windows-Desktop

- **WARP-Tests** rendern das Testmuster offscreen und prüfen Pixel gegen `TestPatternLayout`,
  die gleiche Definition, aus der der Shader seine Konstanten bekommt.
- **Selbstprüfung der App:** `OverlayDiagnostics` wertet gemessene Fensterzustände aus
  (Styles, `WindowFromPoint`-Hit-Test, Vordergrundfenster, Display-Affinity, Frame-Zähler).
- **Screenshot im CI:** Das Smoke-Skript fotografiert den Desktop des Windows-Runners und prüft,
  dass das Muster wirklich auf dem Bildschirm ist, und dass es bei Screenshot-Ausschluss fehlt.
- Alle Bilder landen im CI-Artefakt `previews`.

Was das nicht abdeckt (echte Mausklicks durch das Overlay, Multi-Monitor mit gemischter DPI,
Hot-Plug, GPU-Last auf echter Hardware), steht in `docs/manual-tests/M1.md`.

## Konsequenzen

- Jeder Aufruf von außen ist ein Thread-Wechsel mit Warten. Für Optionen und Status ist das
  unkritisch. Pro-Frame-Daten (ab M3) gehen nicht über diesen Weg, sondern entstehen auf dem
  Overlay-Thread selbst.
- `WindowFromPoint` als Click-Through-Probe ist ein starker Hinweis, aber kein Beweis. Den liefert
  erst der Klick auf echter Hardware.
