# ADR-0004: Fenster-Tracking und Oberflächen

- Status: akzeptiert
- Datum: 2026-11-11
- Meilenstein: M2

## Kontext

Die Effekte brauchen die sichtbaren Kanten der offenen Fenster: Oberkanten (Schnee, Laub, Pfützen,
Gras), Seiten (Rinnsale) und einen Boden pro Monitor. PLAN.md, Abschnitte 2, 3.2 und 3.3 legen die
Win32-Seite fest. Offen waren die genauen Verdeckungsregeln, wer welche Fenster ignoriert und wo das
Tracking läuft.

## Entscheidungen

### 1. Tracking auf dem Overlay-Thread

`OverlayHost` besitzt auch das Tracking. Die WinEvent-Hooks brauchen eine Message-Loop, und der
Overlay-Thread hat schon eine. Snapshot, Oberflächen und Darstellung bleiben so auf einem Thread,
ohne Locks.

- Hooks (`WINEVENT_OUTOFCONTEXT`, schmale Bereiche): Vordergrund, Move/Size Start/Ende, Minimieren
  Start/Ende, Objekt Destroy/Show/Hide/Reorder, LocationChange, Cloaked/Uncloaked. Objekt-Events zählen
  nur für ganze Fenster (`OBJID_WINDOW`, `CHILDID_SELF`), also nicht für Caret, Cursor oder Controls.
- Ereignisse werden gebündelt: Das erste startet einen 16-ms-Timer, weitere bis dahin lösen nichts aus.
  So entsteht höchstens ein Snapshot pro Frame, auch beim Ziehen eines Fensters.
- Zusätzlich ein 10-Hz-Poll für Änderungen ohne Event. Er läuft nur, solange Overlays existieren.
- Ein Snapshot, der dem vorigen gleicht, löst nichts aus. Im Leerlauf wird also nicht gezeichnet.

### 2. Welche Fenster zählen

`EnumWindows` liefert die Z-Order von oben nach unten. Ignoriert werden unsichtbare, minimierte und
cloaked Fenster (andere virtuelle Desktops, suspendierte Store-Apps), Tool-Fenster, klick-durchlässige
Layered-Fenster (Overlays, auch fremde), Fenster unter 100 × 60 px sowie die Shell-Klassen `Progman`,
`WorkerW` (Desktop) und `Shell_TrayWnd`, `Shell_SecondaryTrayWnd` (Taskleisten). Die eigenen Overlays
erkennt das Tracking am Klassennamen-Präfix.

Gelesen werden nur Geometrie und der Klassenname. Bounds kommen von `DWMWA_EXTENDED_FRAME_BOUNDS`,
nicht von `GetWindowRect`, damit die unsichtbaren Resize-Ränder nicht mitzählen.

### 3. Verdeckungsregeln

| Oberfläche | Lage | Verdeckt, wenn ein höheres Fenster … |
|---|---|---|
| Oberkante | Zeile über `Top` | … die Kantenzeile oder die Zeile darüber bedeckt. Liegt ein Fenster direkt auf der Kante, ist kein Platz für Schnee. |
| Seite links/rechts | Spalte direkt außerhalb des Rahmens | … diese Spalte oder die Randspalte des Fensters bedeckt. |
| Boden | Unterkante der Arbeitsfläche (Taskleistenoberkante) | … bis zum Boden reicht (auch durch das Fenster selbst, z. B. maximiert). |

Dazu kommt: Alles wird pro Monitor geschnitten. Eine Oberkante auf der Monitoroberkante (maximiertes
Fenster) hat keinen Platz und entfällt. Ein Fenster über zwei Monitore liefert zwei Stücke mit
gleichem Schlüssel.

### 4. Identität und lokale Koordinaten

Jedes Stück trägt einen `SurfaceKey(Fenster-Id, Kante)` und den `Origin` der ganzen Kante (linke
Fensterkante bzw. obere). Akkumulationen ab M4 speichern relativ zum Origin und wandern so mit dem
Fenster. Verdeckte Teile fallen aus der Liste, die Daten dahinter bleiben beim Effekt erhalten.

Der Boden ist an den Monitor-Index gebunden. Ändert sich die Monitorliste, beginnt der Boden leer.
Das ist selten und unkritisch.

### 5. Bewegung und Shake

`WindowMotionTracker` leitet aus aufeinanderfolgenden Snapshots Ereignisse ab: erschienen,
verschwunden (geschlossen, versteckt, minimiert), bewegt (mit Geschwindigkeit in px/s), Größe
geändert, geschüttelt. Shake: mindestens drei Richtungswechsel in 600 ms mit über 20 px Ausschlag,
getrennt für x und y.

### 6. Keine Allokationen beim Ziehen

`SurfaceExtractor`, `WindowSnapshot` und `SurfaceSet` sind wiederverwendbar und nach dem Aufwärmen
allokationsfrei (per Test mit `GC.GetAllocatedBytesForCurrentThread` geprüft). Ausnahme: Ein neues
Fenster legt im Motion-Tracker einmalig einen Zustand an.

### 7. Prüfung ohne Desktop

- 36 Unit-Tests für Verdeckung und Clipping, dazu Tests für Bewegung und Shake.
- WARP-Tests rendern den Debug-Layer und prüfen, dass jedes gezeichnete Pixel in einem Band der
  extrahierten Oberflächen liegt und umgekehrt.
- Im Smoke-Test verschiebt die App ihr eigenes Fenster und prüft, dass das Tracking die neue Position
  pixelgenau übernimmt. Das Skript prüft im Desktop-Screenshot, dass die Debug-Linien genau auf den
  zwei Zeilen über und den zwei Spalten neben dem App-Fenster liegen. Die Fensterposition liest es
  dafür selbst über DWM, unabhängig vom Tracker.

## Konsequenzen

- Ein 10-Hz-Poll kostet auch im Leerlauf etwas CPU (ein `EnumWindows` pro 100 ms). Das ist gewollt
  (PLAN.md) und wird in M13 auf Referenz-Hardware gemessen.
- Fenster, die ihre Geometrie ohne Event ändern, werden spätestens nach 100 ms erkannt.
- Popup-Menüs, Tooltips und ähnliches sind meist Tool-Fenster oder zu klein und bekommen keine Kanten.
