# Weatherly for Windows – Implementierungsplan für Fable

> Ziel: Ein natives Windows-Tool, das animiertes Wetter (Regen, Schnee, Nebel, Laub, Sonnenlicht, Gras) **über** den offenen Fenstern rendert. Das Wetter interagiert mit der Fenstergeometrie. Klicks gehen durch. Optional folgt es dem echten Wetter.
> Funktionale Vorlage: die macOS-App *Weatherling: Live Weather FX*. Wir übernehmen **Mechaniken**, keine Marke, keine Charaktere, keine Assets.

Dieses Dokument ist als Arbeitsauftrag für einen Coding-Agenten (Fable in Claude Code) geschrieben. Jeder Meilenstein hat Scope, technische Vorgaben, Tests und eine Definition of Done (DoD). Fable arbeitet die Meilensteine der Reihe nach ab. Pro Meilenstein gibt es einen Branch, einen PR und grüne CI.

---

## 0. Feature-Inventar (Referenz → Windows-Ziel)

| Bereich | Referenzverhalten (macOS) | Windows-Umsetzung |
|---|---|---|
| Regen | Tropfen, Splashes, Pfützen auf Fensterkanten, Rinnsale an Fensterseiten, Wasserfälle bei Starkregen | Partikelsystem + 1D-Wasserfeld pro Oberfläche + Drip-/Stream-Partikel |
| Schnee | Sammelt sich auf Fensteroberkanten, rutscht beim Schließen ab, schmilzt | 1D-Heightfield pro Oberfläche, Abrutschen bei Close/Move/Shake, Schmelzen |
| Nebel | Zieht mit dem Wind rein und raus | Fullscreen-fBm-Noise-Shader mit Domain-Warping, Low-Res |
| Laub | Weht, landet auf Fenstern, Fenster schütteln verstreut es, Cursor erzeugt Böe | Rigid-ish 2D-Partikel mit Flatter-Aerodynamik, Cursor-Velocity-Field, Shake-Detection |
| Sonnenlicht | Warme Strahlen zwischen den Fenstern | Occluder-Maske aus Fenster-Rects + Radial-Blur-God-Rays |
| Gras | Wächst auf Fensteroberkanten, schneller bei Regen, biegt sich im Wind | Instanced Blades pro Oberfläche, Vertex-Shader-Sway |
| Pro Effekt | Intensität, eigene Farbe, Helligkeit, Fixed/Random (mit Wechselrate) | Einheitliches `EffectSettings`-Modell |
| Templates | Szenen mit Name und Emoji speichern, Presets, Teilen als Link/Datei | JSON-Szenen `.wscene` + Protokoll `weatherly://scene?d=…` |
| Wetter-Sync | Echtes Wetter (Standort/Stadt/PLZ), Invert, Custom Sync pro Element, manuelle Änderung pausiert Sync | MET Norway API + gebündelte Städteliste + Mapping-Tabelle |
| Besucher | Kleine Charaktere besuchen Fenster; einsammeln per Drag in eine Box → Punkte, Sammlung, Score-Karte, Leaderboards | **Eigene** Charaktere („Visitors“), Drag-Hit-Windows, lokale Sammlung, PNG-Share-Card; Leaderboards optional/später |
| Sound | Regen, Schnee, Charaktere, je eigene Lautstärke | XAudio2, prozeduraler Regen-Sound + CC0-Samples |
| System | Pro Display, Per-App Pause/Hide, Akku-Optionen, Smoothness (Least Energy → Smoothest/Auto), Screenshot-Optionen | Per-Monitor-Overlays, Prozessnamen-Regeln, Power-Notifications, FPS-Cap, `WDA_EXCLUDEFROMCAPTURE` |
| Accessibility | Screenreader, Reduced Motion, Dark UI, kein Farb-only | Narrator/UIA (WinUI), `SPI_GETCLIENTAREAANIMATION`, High Contrast, Keyboard-Collect |
| Privacy | Nur Fenstergeometrie, keine Titel oder Inhalte, keine Analytics, keine Screen-Recording-Rechte | Identisch. Zusätzlich lokale Prozessnamen nur für Regeln, Koordinaten gerundet |

**Windows-Extras (Bonus, nicht Pflicht):** Taskleiste als „Boden“-Oberfläche, Snap-Layouts-aware, Mica-UI.

### IP-Regeln (hart)
- Eigener Name. Arbeitstitel „Weatherly“. Vor dem Store-Release Namenskollision prüfen, im Mac App Store existiert bereits eine Wetter-App „Weatherly“.
- Keine Charaktere, Namen, Grafiken, Sounds, Texte oder Screenshots der Referenz-App. Alle Assets sind selbst erstellt (SVG/prozedural) oder CC0. Jede Quelle wird in `ASSETS.md` dokumentiert.

---

## 1. Tech-Stack-Entscheidung

### Empfehlung: C# / .NET 10 + Direct3D 11 + DirectComposition + WinUI 3

| Option | Pro | Contra |
|---|---|---|
| **A: C# .NET 10, Vortice.Windows (D3D11/DXGI/DComp/D2D/XAudio2), CsWin32, WinUI 3** ✅ | Schnelle Iteration, Kernlogik plattformneutral → **Tests laufen auf Linux**, gute Agent-Ergonomie | Etwas mehr RAM (~80–120 MB), GC-Disziplin nötig (keine Allocs im Frame-Loop) |
| B: C++20, D3D11, DComp, C++/WinRT + WinUI 3 | Maximale Performance, kleinster Footprint | Langsamere Iteration, Kernlogik schwer auf Linux testbar |
| C: Rust, windows-rs, eigener D3D11-Renderer | Sicher und schnell | WinUI-Integration mühsam, wgpu + DComp-Transparenz ist fummelig |

Begründung für A: Der teure Teil ist GPU-gebunden. Die CPU-Simulation (≤ 20k Partikel) schafft C# mit `System.Numerics`-SIMD locker unter 1 ms. Der wichtigste Punkt: Fable läuft eventuell in einem Linux-Container. Mit A sind ~60 % des Codes (Simulation, Geometrie, Wetter-Mapping, Settings) dort bau- und testbar.

### Kernbibliotheken
- `Vortice.Direct3D11`, `Vortice.DXGI`, `Vortice.DirectComposition`, `Vortice.Direct2D1`, `Vortice.XAudio2`, `Vortice.D3DCompiler`
- `Microsoft.Windows.CsWin32` (Source-Generator für Win32-P/Invoke: `EnumWindows`, `DwmGetWindowAttribute`, `SetWinEventHook`, …)
- `Microsoft.WindowsAppSDK` (WinUI 3 für Tray-Flyout und Settings)
- `H.NotifyIcon.WinUI` (Tray-Icon) oder ein eigener Wrapper um `Shell_NotifyIcon`
- Tests: xUnit, FluentAssertions, Verify (Snapshot-Tests)

---

## 2. Architektur

```
┌──────────────────────── Weatherly.App (WinUI 3, net10.0-windows) ────────────────────────┐
│ Tray-Flyout · Settings-Fenster · Templates · Onboarding · Composition Root               │
└───────────────┬───────────────────────────────┬───────────────────────────┬──────────────┘
                │ SettingsStore (Observable)    │                           │
┌───────────────▼──────────────┐  ┌─────────────▼────────────┐  ┌───────────▼─────────────┐
│ Weatherly.Platform (Win32)   │  │ Weatherly.Render (D3D11) │  │ Weatherly.Audio          │
│ OverlayWindow pro Monitor    │  │ Device/SwapChain/DComp   │  │ XAudio2, Regen-Synth     │
│ WindowTracker (WinEventHook) │  │ Pipelines je Effekt      │  └──────────────────────────┘
│ MonitorService, DPI          │  │ D2D für Visitors/Text    │
│ PowerService, FullscreenSvc  │  │ HLSL-Shader (embedded)   │
│ ForegroundAppService         │  └─────────────▲────────────┘
│ CursorService                │                │ RenderFrame (immutable Snapshot)
└───────────────┬──────────────┘  ┌─────────────┴────────────────────────────────────────┐
                │ WindowSnapshot  │ Weatherly.Core (net10.0, plattformneutral)           │
                └────────────────►│ SurfaceExtractor (Occlusion) · Simulation (fixed dt)  │
                                  │ Rain/Snow/Leaves/Grass/Fog/Sun-Systeme · Wind         │
                                  │ WeatherSync (Mapping) · Scheduler (Random-Modus)      │
                                  │ Settings-Modelle · Scene-Serialisierung · Visitors-AI │
                                  └───────────────────────────────────────────────────────┘
```

### Repo-Struktur
```
/src
  Weatherly.Core/            net10.0: keine Windows-Abhängigkeit
  Weatherly.Platform/        net10.0-windows: Win32-Interop via CsWin32
  Weatherly.Render/          net10.0-windows: D3D11/DComp/D2D, Shaders/*.hlsl
  Weatherly.Audio/           net10.0-windows
  Weatherly.App/             WinUI 3, MSIX
/tools
  Weatherly.Sandbox/         Debug-App: Effekte in normalem Fenster, Fake-Fenster per Maus verschiebbar
  Weatherly.FrameDump/       CLI: scene.json + Fake-Fenster-Layout → N PNG-Frames (WARP, headless)
/tests
  Weatherly.Core.Tests/      läuft auf Linux + Windows
  Weatherly.Render.Tests/    Windows-only, WARP-Golden-Images
/docs                        ADRs, Mapping-Tabellen, manuelle Testchecklisten
.github/workflows/ci.yml
CLAUDE.md                    Konventionen für Agenten
ASSETS.md                    Lizenz und Herkunft jedes Assets
```

### Datenfluss pro Frame
1. `WindowTracker` liefert einen `WindowSnapshot` (Z-Order, Bounds in physischen Pixeln, HWND-ID, Flags). Event-getrieben, Poll-Fallback mit 10 Hz.
2. `SurfaceExtractor` (Core) berechnet daraus sichtbare **Oberflächen**:
   - horizontale Segmente: sichtbare Fensteroberkanten, verdeckte Bereiche abgezogen
   - vertikale Segmente: sichtbare Seitenkanten, für Rinnsale
   - Boden: Taskleisten-Oberkante bzw. Monitor-Unterkante
3. Die Simulation tickt mit festem `dt = 1/120 s`, akkumuliert und deterministisch mit Seed.
4. Daraus wird ein immutable `RenderFrame` gebaut: Instanz-Buffer und Uniforms. Das Rendering läuft pro Monitor-Overlay.
5. Im Idle (alle Effekte aus, pausiert, Fullscreen-App) **stoppt der Render-Loop komplett** (0 % GPU).

---

## 3. Kritische Windows-Technik (Fable: genau so umsetzen)

### 3.1 Overlay-Fenster (ein Fenster pro Monitor)
- Styles: `WS_POPUP`, ExStyles `WS_EX_LAYERED | WS_EX_TRANSPARENT | WS_EX_TOPMOST | WS_EX_TOOLWINDOW | WS_EX_NOACTIVATE | WS_EX_NOREDIRECTIONBITMAP`.
- `SetLayeredWindowAttributes(hwnd, 0, 255, LWA_ALPHA)`, damit Click-Through via `WS_EX_TRANSPARENT` zuverlässig greift.
- Rendering: D3D11-Device → `IDXGIFactory2.CreateSwapChainForComposition` mit `DXGI_ALPHA_MODE_PREMULTIPLIED`, `B8G8R8A8_UNORM`, `FLIP_SEQUENTIAL`, 2 Buffer, `FRAME_LATENCY_WAITABLE_OBJECT` mit MaxFrameLatency 1 → `IDCompositionDevice` → Target → Visual → `SetContent(swapChain)`.
- **Alle Shader geben premultiplied Alpha aus.** Clear-Farbe ist `(0,0,0,0)`.
- DPI: Prozess `PerMonitorAwareV2` (App-Manifest). Alle Koordinaten intern in physischen Pixeln. Bei `WM_DPICHANGED` und `WM_DISPLAYCHANGE` werden die Overlays neu aufgebaut.
- Screenshot-Option: `SetWindowDisplayAffinity(hwnd, WDA_EXCLUDEFROMCAPTURE)` (ab Win10 2004), umschaltbar in den Settings.
- Adapter-Wahl: `IDXGIFactory6.EnumAdapterByGpuPreference(MINIMUM_POWER)`, damit auf Hybrid-Laptops die iGPU läuft.
- Device-Lost (`DXGI_ERROR_DEVICE_REMOVED/RESET`): Device komplett neu aufbauen. Simulation-State bleibt in Core erhalten.
- **Kein Lesen des Bildschirminhalts.** Keine Refraktion über echtes Backdrop-Sampling. Tropfen-Optik wird gefaked (Highlight, dunkler Rand, leichter Schatten).

### 3.2 Fenster-Tracking
- `EnumWindows` (liefert Top→Bottom-Z-Order). Filter:
  - `IsWindowVisible`, nicht `IsIconic`
  - `DwmGetWindowAttribute(DWMWA_CLOAKED) == 0` (filtert virtuelle Desktops und suspendierte UWP-Apps)
  - kein `WS_EX_TOOLWINDOW`, keine eigenen Overlay-HWNDs, Fläche > 0, Mindestgröße ca. 100×60 px
- Bounds: `DwmGetWindowAttribute(DWMWA_EXTENDED_FRAME_BOUNDS)` statt `GetWindowRect`. Win10/11 haben unsichtbare Resize-Ränder.
- Events: `SetWinEventHook(WINEVENT_OUTOFCONTEXT)` für `EVENT_OBJECT_LOCATIONCHANGE` (nur `OBJID_WINDOW`), `EVENT_OBJECT_SHOW/HIDE/DESTROY`, `EVENT_SYSTEM_FOREGROUND`, `EVENT_SYSTEM_MINIMIZESTART/END`, `EVENT_SYSTEM_MOVESIZESTART/END`. Events werden gecoalesced und der Snapshot max. 1× pro Frame neu gebaut.
- **Niemals** `GetWindowText` aufrufen. Für Per-App-Regeln wird nur der Prozessname gelesen: `GetWindowThreadProcessId` → `QueryFullProcessImageName` → Dateiname.
- Sonderfälle: maximierte Fenster (Oberkante = Monitor-Oberkante → keine Akkumulation, da kein Platz), Snap-Layouts, `ApplicationFrameWindow` (UWP), Taskleiste `Shell_TrayWnd` als Boden-Oberfläche, Start-Menü bzw. Suche offen (Foreground-Klasse prüfen → Effekte optional ausblenden).
- Shake-Detection: ≥ 3 Richtungswechsel der Fensterposition innerhalb von 600 ms mit Amplitude > 20 px.
- Window-Velocity aus Positionsdeltas → bei hoher Beschleunigung rutschen Schnee und Laub ab.

### 3.3 Oberflächen-Identität
- Jede Akkumulation (Schnee-Heightfield, Wasser, Laub, Gras) hängt an `(HWND, Edge)` und wird in **fensterlokalen** Koordinaten gespeichert. Verschiebt man das Fenster, wandert der Schnee mit.
- Resize: Feld wird auf die neue Breite resampled.
- Verdeckung: Daten bleiben erhalten, nur der verdeckte Teil wird maskiert (nicht gerendert, keine Kollision).
- Close/Hide/Minimize: Inhalt wird in fallende Partikel konvertiert („rutscht ab“).

### 3.4 System-Zustände
- Fullscreen und Präsentationsmodus: `SHQueryUserNotificationState` (`QUNS_RUNNING_D3D_FULL_SCREEN`, `QUNS_BUSY`, `QUNS_PRESENTATION_MODE`) → pausieren.
- Akku und Energiesparen: `RegisterPowerSettingNotification(GUID_ACDC_POWER_SOURCE, GUID_POWER_SAVING_STATUS)`. Optionen: *nichts ändern / Auto-Smoothness / pausieren*.
- Lock, Sleep, RDP: `WTSRegisterSessionNotification`, `WM_POWERBROADCAST` → pausieren.
- Reduced Motion: `SystemParametersInfo(SPI_GETCLIENTAREAANIMATION)` → Option „Effekte pausieren“.
- Smoothness: *Least Energy* (30 fps, halbe Partikelzahl, Low-Res-Fog) · *Balanced* (60) · *Smoothest* (Monitor-Refresh, max. 144) · *Automatic* (Netzteil → Smoothest, Akku → Least Energy, außerdem dynamische Reduktion, wenn die Frame-Time > Budget).

---

## 4. Effekt-Spezifikationen

Alle Effekte implementieren dasselbe Interface:
```csharp
interface IEffectSystem {
    void Configure(EffectSettings s, GlobalWeather w);   // Intensität 0..1, Farbe, Helligkeit
    void Step(float dt, in SimContext ctx);              // Surfaces, Wind, Cursor, Events
    void OnSurfaceEvent(in SurfaceEvent e);              // Closed, Moved(vel), Shaken, Resized
    void Emit(RenderFrameBuilder b);                     // nur Daten, kein D3D
}
```
Globaler **Wind**: Basisvektor plus Böen aus 1D-Simplex-Noise über die Zeit. Alle Effekte lesen ihn.

### 4.1 Regen
- Tropfen: Position, Geschwindigkeit, Tiefe `z ∈ [0,1]` für Parallaxe, Größe und Alpha skalieren mit z. Spawn oberhalb des Monitors plus seitlich je nach Wind.
- Darstellung: geschwindigkeitsausgerichtete gestreckte Quads (Länge ∝ |v|·dt·k), weicher Kern, Highlight, Instanced Draw.
- Kollision: Tropfen mit `z > depthThreshold` (Setting „Depth“) kollidieren mit dem ersten horizontalen Segment, das sie kreuzen (Swept-Test y_prev→y_now). Der Rest fällt bis zum Boden.
- Splash: 3–6 Sekundär-Partikel, ballistisch, kurze Lebenszeit, plus ein kleiner Ring-Sprite.
- Pfützen: 1D-Wasserfeld pro Oberfläche (Bins à 4 px·DPI). Treffer addieren Wasser, Ausgleich über Diffusion. Gerendert als dünner glänzender Film mit Wellen-Noise.
- Rinnsale: Wasser fließt zu den Segment-Enden ab. An den Fensterecken spawnen Drips mit einer Rate ∝ Wasservolumen. Die Drips folgen der Seitenkante (vertikales Segment) mit Meander-Noise und hinterlassen einen Trail (Ring-Buffer von Punkten → Triangle-Strip).
- Wasserfälle: bei Intensität > 0,8 kontinuierlicher Sheet-Emitter an den Ecken.
- Budget: max. 12k Tropfen + 2k Splash-Partikel bei Intensität 1,0.

### 4.2 Schnee
- Flocken: Sprite-Atlas (4–6 prozedurale Varianten), Taumeln über `sin`-Drift plus Wind, langsame Rotation.
- Akkumulation: Heightfield pro Oberfläche (Bins à 2 px·DPI). Landende Flocken addieren Volumen. Angle-of-Repose-Relaxation (`if h[i]-h[i+1] > maxSlope → Transfer`). Max. Höhe 28 px·DPI, Überlauf fällt als Klumpen über die Segment-Enden.
- Massenerhaltung wird per Unit-Test geprüft: Volumen vorher = nachher + abgegangene Klumpen.
- Rendering: Heightfield → Triangle-Strip, weiche Oberkante (SDF-Falloff), leichter Blau-Schatten unten, Glitzer-Sparkles.
- Abrutschen: bei Close, Hide oder Minimize, bei Window-Beschleunigung über dem Schwellwert und bei Shake. Das Heightfield wird in Klumpen-Partikel zerlegt.
- Schmelzen: wenn die Schnee-Intensität 0 ist bzw. der Sync > +2 °C meldet, nimmt h exponentiell ab. Optional tropft der Schnee dabei.

### 4.3 Laub
- Blatt: Position, Geschwindigkeit, Winkel, Winkelgeschwindigkeit, Flip-Phase (Pseudo-3D über `scaleX = cos(phase)`), Typ, Farbvariante (Herbstpalette, durch Custom-Color überschreibbar).
- Aerodynamik: Drag plus Flatter-Lift ∝ `sin(flipPhase)`. Ergibt natürliches Pendeln.
- Landen: Das Blatt bleibt auf der Oberfläche liegen, leicht rotiert. Max. N Blätter pro 100 px. Kommen mehr, rutschen ältere ab.
- Cursor-Böe: `CursorService` liest jeden Frame `GetCursorPos`, das braucht keinen Hook. Ist die Cursor-Geschwindigkeit > Schwellwert, bekommen Blätter im Radius einen Impuls ∝ v_cursor mit Falloff. Liegende Blätter werden wieder aufgewirbelt.
- Shake: alle Blätter dieses Fensters bekommen einen Impuls und lösen sich.

### 4.4 Nebel
- Fullscreen-Pass in ¼-Auflösung, bilinear hochskaliert.
- fBm-Simplex-Noise (4 Oktaven) mit Domain-Warping. UV scrollt mit dem Wind, Dichte schwankt langsam (Einziehen/Abziehen).
- Höhengradient: unten dichter. Leichtes Aufhellen über Fensteroberkanten, die Fenster „ragen“ aus dem Nebel.
- Max. Alpha begrenzt (ca. 0,35), damit die Lesbarkeit erhalten bleibt.

### 4.5 Sonnenlicht
- Occluder-Maske in ¼-Auflösung: Fenster-Rects schwarz, Rest weiß.
- God-Rays: Radial-Blur von der Sonnenposition (oben links/rechts, Setting, langsam wandernd), 48 Samples, Decay und Weight. Additiv mit warmer Farbe.
- Dazu: weicher Bloom-Glow am Sonnenpunkt, Staub-Partikel im Strahl (ca. 100), subtiler warmer Tint am Rand.
- Bei Wetter-Sync: nur tagsüber (`is_day`), Stärke ∝ (1 − cloud_cover).

### 4.6 Gras
- Pro Oberfläche eine Halm-Liste. Halm-Dichte ∝ Breite. Jeder Halm hat Phase, Max-Höhe und Farbvariante.
- Wachstum: `h += rate · (1 + 2·rainIntensity) · dt`, bis max (ca. 18 px·DPI). Gras wandert mit dem Fenster. Beim Schließen wird es nicht abgeworfen, es verschwindet mit einer kurzen Fade-Animation.
- Rendering: Instanced getaperte Quads (3 Segmente). Der Vertex-Shader biegt sie über `wind + noise(t, phase)`. Optional drückt der Cursor das Gras weg.

### 4.7 Kombinierbarkeit
- Alle 6 Effekte laufen unabhängig und gleichzeitig.
- Interaktionen: Regen beschleunigt Gras. Regen auf Schnee lässt ihn schneller schmelzen. Wind wirkt auf alles.
- Render-Reihenfolge: Sonne → Nebel (hinten) → Gras → Akkumulationen → Laub → Schnee → Regen → Visitors → UI-Popups.

### 4.8 Random-Modus
- Pro Effekt `Fixed | Random`. Random bewegt die Intensität per geglättetem Random-Walk innerhalb von [min, max]. Die Wechselrate ist einstellbar (Minuten bis Stunden). Seeded, damit testbar.

---

## 5. Wetter-Sync

- **Provider:** MET Norway `locationforecast/2.0/compact`. Kommerzielle Nutzung ist unter CC BY 4.0 mit Attribution erlaubt, ein eindeutiger `User-Agent` ist Pflicht, Cache-Header (`Expires`, `If-Modified-Since`) müssen respektiert werden.
  - Achtung: Die freie Open-Meteo-API ist laut ihren Terms **nur nicht-kommerziell** nutzbar. Da die App verkauft wird, kommt Open-Meteo nur als bezahlter Plan oder im Dev-Betrieb in Frage.
  - Der Provider liegt hinter einem `IWeatherProvider`-Interface, damit er austauschbar bleibt.
- **Standort:**
  1. Optional `Windows.Devices.Geolocation`. Koordinaten werden **vor dem Senden auf 1 Dezimale gerundet** (~11 km).
  2. Oder manuelle Stadtsuche **offline** über die gebündelte GeoNames-Liste `cities15000` (CC BY 4.0, ~2 MB). Für die Ortsauflösung wird kein Netzwerk gebraucht.
  3. PLZ: GeoNames-Postal-Codes als optionaler Download pro Land.
- **Polling:** alle 15–30 Min bzw. gemäß `Expires`-Header, mit Backoff bei Fehlern. Offline → letzter Stand und Hinweis im UI.
- **Mapping** (`docs/weather-mapping.md`, in Core unit-getestet):

| Input | → Effekt |
|---|---|
| `precipitation_amount` (mm/h) + `symbol_code` rain/sleet/showers | Regen-Intensität: 0,1 mm/h → 0,15 … ≥ 8 mm/h → 1,0 (log-Skala) |
| symbol snow/sleet, Temperatur ≤ 1 °C | Schnee-Intensität (analog), Schmelzen bei > 2 °C |
| symbol fog bzw. `fog_area_fraction` | Nebel-Dichte |
| `wind_speed` + `wind_from_direction` | Wind-Vektor (Richtung auf die Bildschirm-x-Achse projiziert), Böen aus `wind_speed_of_gust` |
| `cloud_area_fraction`, Tag/Nacht | Sonnenlicht |
| Monat Sep–Nov + Wind > 4 m/s (Nordhalbkugel, Südhalbkugel invertiert) | Laub (optional) |

- **Invert:** Mapping-Ausgabe spiegeln: Regen ↔ Sonne, Schnee bei Wärme. Die Regeln dafür sind dokumentiert.
- **Custom Sync:** Gewicht 0–100 % pro Element (`final = lerp(manual, synced, weight)`).
- **Manueller Override:** Bewegt der Nutzer einen Slider, wird der Sync für dieses Element pausiert. Ein Banner im UI bietet „Sync fortsetzen“ an.
- Pflicht-Attribution „Weather data: MET Norway“ im About-Dialog und in den Sync-Settings.

---

## 6. Visitors (eigene Charaktere)

- **Eigenes Konzept, eigene Namen.** Vorschlag: 8 Start-Charaktere. Jeder hat eine Persönlichkeit, eine Wetter-Affinität (taucht häufiger bei Regen, Schnee usw. auf), 4–6 Animationen (Enter, Walk, Idle, React, Leave, Picked-Up) und 2–3 Sounds.
- **Assets:** SVG-Vektorgrafiken, die Fable selbst schreibt (einfache Formen, klarer Stil). Sie werden per **Direct2D** auf demselben D3D11-Device gerendert (DXGI-Surface-Interop), so bleiben sie bei jeder DPI scharf. Animation läuft über Transform-Keyframes und Shape-Morphing, Spritesheets sind nicht nötig.
- **Verhalten:** Zustandsmaschine in Core. Ein Visitor spawnt am Rand einer Oberfläche, läuft auf Fensteroberkanten, springt zwischen nahen Fenstern, reagiert auf Wetter (Schirm auf, Schneeball, Sonnenbrille) und geht wieder. Häufigkeit: *Never / Rarely / Sometimes (Default) / Often*.
- **Interaktiver Modus (opt-in):** Das Overlay bleibt click-through. Für jeden sichtbaren Visitor gibt es ein **kleines separates Hit-Fenster** (topmost, unsichtbar, `WS_EX_NOACTIVATE`, Alpha 1/255), das seiner Bounding-Box folgt. Nur dort sind Klick und Drag möglich. Eine „Box“ (Sammelkorb) erscheint am Bildschirmrand. Wird der Visitor über der Box losgelassen, gibt es Punkte. Wird er woanders losgelassen, geht er beleidigt.
- **Keyboard-/Narrator-Alternative:** Visitor erscheint → Toast bzw. Tray-Badge → Tastenkürzel oder Button „Einsammeln“.
- **Sammlung und Punkte:** lokal in `%LOCALAPPDATA%\Weatherly\collection.json`, Seltenheiten, Box/Visitor-Kombos, Achievements.
- **Share Card:** Score bzw. Visitor als PNG über D2D gerendert → Zwischenablage, Datei speichern oder Windows-Share-Sheet (`DataTransferManager`).
- **Leaderboards:** v1 nicht dabei. Später optional ein eigener minimaler Backend-Dienst mit Opt-in und eigener Datenschutzerklärung.

---

## 7. UI (WinUI 3)

- **Tray-Icon** mit Linksklick → Flyout im Stil der Windows-11-Schnelleinstellungen (Mica/Acrylic):
  - Master-Toggle, 6 Effekt-Kacheln (Toggle + Intensitäts-Slider), Wind-Slider, Template-Picker, Sync-Status, „Mehr…“
- **Settings-Fenster** (NavigationView):
  1. *Effekte*: pro Effekt Intensität, Farbe (ColorPicker), Helligkeit, Fixed/Random mit Bereich und Rate, Effekt-spezifische Optionen (Regen-Depth, Schnee-Max-Höhe, …)
  2. *Templates*: Liste mit Emoji und Name, Presets (eigene: „Gewitter“, „Winterabend“, „Herbstwind“, „Neon“), Speichern, Import/Export `.wscene`, Link kopieren
  3. *Wetter-Sync*: An/Aus, Standort, Invert, Custom-Sync-Gewichte, Status und Attribution
  4. *Visitors*: Häufigkeit, interaktiver Modus, Sammlung (Grid), Punkte, Share
  5. *Sound*: pro Kategorie Lautstärke, Master-Mute
  6. *Displays*: pro Monitor an/aus und eigene Intensität
  7. *Apps*: Regeln pro Prozess (*pausieren, wenn im Vordergrund* / *über diesem Fenster ausblenden*), Picker aus laufenden Apps
  8. *Leistung*: Smoothness, Akku-Verhalten, Live-FPS/CPU/GPU-Anzeige (Debug)
  9. *Erweitert*: von Screenshots ausschließen, Autostart (`StartupTask` im MSIX), Reduced-Motion-Verhalten, Reset
  10. *Info*: Version, Attribution, Datenschutz („liest nur Fenstergeometrie“)
- Settings-Persistenz: `%LOCALAPPDATA%\Weatherly\settings.json`, versioniert mit Migrationen. Writes debounced.
- Single-Instance (Mutex + Redirect). `weatherly://`-Protokoll für Template-Links.

---

## 8. Audio

- XAudio2, ein Mastering-Voice, Submix pro Kategorie (Regen, Schnee/Wind, Visitors).
- **Regen prozedural:** rosa Rauschen → Bandpass mit intensitätsabhängigem Cutoff, plus zufällige Tropfen-Transienten. Die Dichte koppelt an die echte Splash-Rate der Simulation. Keine Lizenzfragen und es loopt nahtlos.
- Wind: gefiltertes braunes Rauschen, Amplitude ∝ Böen.
- Visitors und UI: kurze CC0-Samples oder selbst synthetisierte Sounds, alle in `ASSETS.md`.
- Default: Sound **aus**.

---

## 9. Performance-Budgets (verbindlich, in CI und Debug-HUD gemessen)

| Metrik | Ziel (iGPU, z. B. Intel Iris Xe, 1440p, 60 fps, alle Effekte 50 %) |
|---|---|
| CPU (gesamter Prozess) | < 3 % eines Kerns im Mittel |
| Sim-Step | < 1,0 ms p95 |
| GPU-Frame | < 2,5 ms p95 |
| RAM | < 150 MB |
| Allocations im Frame-Loop | 0 B (per `GC.GetAllocatedBytesForCurrentThread` im Test geprüft) |
| Idle (alles aus/pausiert) | 0 Frames, ~0 % CPU |

Techniken: Struct-of-Arrays, `Span<T>`, gepoolte Buffer, `Map(WRITE_DISCARD)` für Instanz-Buffer, Low-Res-Passes für Fog und Sun, Culling pro Monitor, dynamische Partikel-Reduktion bei Budget-Überschreitung.

---

## 10. Test- und Verifikationsstrategie (wie Fable ohne Windows-Desktop sieht, was es baut)

1. **Core-Unit-Tests (Linux + Windows):** Occlusion-Extraktion (viele Layouts: überlappend, kaskadiert, Multi-Monitor, negative Koordinaten), Heightfield-Massenerhaltung, Shake-Detection aus synthetischen Positionsreihen, Wetter-Mapping-Tabelle, Settings-Migration, Scene-Roundtrip, Random-Walk-Grenzen, Visitor-Zustandsmaschine.
2. **Deterministische Snapshot-Tests:** fester Seed + feste Fenster-Layouts + N Ticks → Verify-Snapshot der Simulationsstatistik (Partikelzahl, Gesamtschnee, Positionen-Hash).
3. **Render-Golden-Tests (Windows-CI, WARP):** `Weatherly.FrameDump` rendert definierte Szenen headless per WARP (`D3D_DRIVER_TYPE_WARP`) in PNGs, die mit Toleranz gegen Goldens verglichen werden. Zusätzlich gibt es metrische Assertions (z. B. „Schnee-Pixel liegen nur oberhalb der Fensterkanten ±30 px“, „Alpha außerhalb der Effekte = 0“).
4. **Visuelle Review-Schleife für Fable:** Ein CI-Job pusht gerenderte Vorschau-PNGs und kurze GIF-Sequenzen auf den Orphan-Branch `ci-previews/<branch>`. Fable holt ihn per `git fetch` und schaut sich die Bilder direkt an (Read-Tool kann PNGs anzeigen). So iteriert Fable an der Optik, ohne echten Desktop.
5. **Sandbox-App:** normales Fenster mit verschiebbaren Fake-Fenstern und allen Effekten, für die manuelle Iteration auf echtem Windows.
6. **Manuelle Checklisten** (`docs/manual-tests/*.md`) pro Meilenstein. Boss prüft sie auf seinem Windows-PC: Click-Through, Multi-Monitor mit gemischter DPI, Snap, virtuelle Desktops, Fullscreen-Spiel, Akku, Lock/Unlock, RDP, Screenshot-Ausschluss.

### Zwei Betriebsmodi für Fable
- **Modus A – Cloud (Linux-Container):** Core plus Tests lokal, Windows-Build/WARP über GitHub Actions `windows-latest`, Optik über `ci-previews`. Reicht für M0, M2-Core, M4–M8-Simulation und M10.
- **Modus B – lokal auf Boss' Windows-PC** (Claude Code direkt auf Windows oder per Remote Control angebunden): Fable kann die App wirklich starten, per PowerShell Screenshots machen und Fenster-Layouts skripten. **Für M1, M3, M9, M11 und M13 dringend empfohlen.**

---

## 11. CI/CD

- `ci.yml`:
  - Job `core` (ubuntu-latest): `dotnet build` + `dotnet test tests/Weatherly.Core.Tests`
  - Job `windows` (windows-latest): Build der ganzen Solution (x64 + ARM64), Render-Tests mit WARP, FrameDump → Previews als Artifact und auf den `ci-previews`-Branch
  - Job `package` (nur Tags): MSIX-Build und Signierung
- Analyzer: `TreatWarningsAsErrors`, Nullable an, `dotnet format --verify-no-changes`.
- Shader werden zur Build-Zeit kompiliert (`dxc`/`fxc` aus dem Windows SDK) und als Ressourcen eingebettet. Ein Fehler im Shader bricht den Build ab.

---

## 12. Meilensteine (Reihenfolge verbindlich)

Größe: S ≈ 1 Session, M ≈ 2–3, L ≈ 4+ Agent-Sessions.

| # | Meilenstein | Größe | Inhalt | DoD |
|---|---|---|---|---|
| **M0** | Fundament | S | Solution und Projektstruktur, CsWin32, Vortice, CI (beide Jobs), `CLAUDE.md`, `ASSETS.md`, ADR-001 (Stack) | CI grün, Hello-Test in Core, leere WinUI-App baut |
| **M1** | Overlay-Spike | M | Per-Monitor-DComp-Overlay, premultiplied Alpha, Click-Through, Topmost, DPI v2, Hot-Plug von Monitoren, Capture-Exclusion, Testmuster-Rendering | Manuelle Checkliste M1 bestanden (Klicks gehen durch, kein Fokusklau, 0 % GPU im Idle) |
| **M2** | Fenster-Tracking und Oberflächen | M | WindowTracker (Events + Poll), Filter, DWM-Bounds, Z-Order, `SurfaceExtractor` mit Occlusion, Shake/Velocity, Debug-Layer zeichnet Oberflächen | ≥ 30 Occlusion-Unit-Tests, Debug-Linien sitzen pixelgenau auf echten Fensterkanten |
| **M3** | Engine und Tooling | M | Fixed-Timestep-Loop, Waitable-Swapchain-Pacing, Idle-Stopp, RenderFrame, Instancing-Pipeline, D2D-Interop, Sandbox-App, FrameDump + WARP-Goldens + `ci-previews` | Previews erscheinen auf dem Branch, 0 Allocs im Loop |
| **M4** | Regen | L | Tropfen, Depth, Splashes, Pfützen, Rinnsale, Wasserfälle, Wind, prozeduraler Regen-Sound | Goldens, Budgets eingehalten, Boss-Abnahme der Optik |
| **M5** | Schnee | M | Flocken, Heightfield, Repose, Abrutschen (Close/Move/Shake), Schmelzen | Massenerhaltungs-Tests, Abrutsch-Demo in der Sandbox |
| — | **MVP-Cut** | | M0–M5 + Minimal-Tray (Toggles/Slider) | Erste nutzbare Version für Boss' Alltag |
| **M6** | Laub | M | Flatter-Physik, Landen, Cursor-Böe, Shake-Scatter | Tests + Previews |
| **M7** | Nebel + Sonne | M | fBm-Fog, Occluder-God-Rays, Staub | Goldens, GPU-Budget |
| **M8** | Gras | S | Wachstum, Regen-Boost, Wind-Sway | Tests + Previews |
| **M9** | UI komplett | L | Tray-Flyout, Settings-Fenster (alle 10 Seiten), Farben, Fixed/Random, Templates und Protokoll, Per-Display, Per-App-Regeln, Smoothness, Akku, Autostart | Alle Settings wirken live, persistieren und migrieren |
| **M10** | Wetter-Sync | M | MET-Provider, Geolocation, Offline-Städtesuche, Mapping, Invert, Custom Sync, Override-Pause | Mapping-Tests, Offline/Fehler-Handling, Attribution sichtbar |
| **M11** | Visitors | L | 8 eigene Charaktere (SVG/D2D), KI-Zustandsmaschine, Hit-Fenster, Box, Punkte, Sammlung, Achievements, Share Card, Sounds | Drag & Collect funktioniert ohne Click-Through-Regression, Keyboard-Alternative vorhanden |
| **M12** | A11y und Lokalisierung | M | Narrator-Labels, High Contrast, Reduced Motion, Farb-unabhängige Zustände, `.resw` EN und DE | Accessibility Insights ohne kritische Findings |
| **M13** | Härtung | M | Fullscreen/Game-Detection, Lock/Sleep/RDP, Device-Lost, Start-Menü-Handling, lokale Crash-Logs (ohne Telemetrie), Langzeittest 8 h ohne Leak | Soak-Test bestanden, Budgets auf Referenz-Hardware |
| **M14** | Release | M | MSIX (x64/ARM64), Store-Listing (eigene Screenshots), Privacy-Policy, winget-Manifest, Signierung (Store bzw. Azure Trusted Signing), Namensprüfung | Store-Zertifizierung bestanden |

---

## 13. Arbeitsregeln für Fable (in `CLAUDE.md` übernehmen)

1. **Ein Meilenstein = ein Branch = ein PR.** Vor dem PR: Core-Tests lokal grün, CI grün, Previews geprüft.
2. **Core bleibt Windows-frei.** Keine `System.Windows`-, Win32- oder Vortice-Referenzen in `Weatherly.Core`. Das erzwingt ein Arch-Test.
3. **Keine Allocations im Frame-Loop.** Hot-Paths mit `[SkipLocalsInit]`, `Span`, SoA.
4. **Privacy-Invarianten** sind als Tests abgesichert: kein `GetWindowText`-Import im ganzen Code (Analyzer bzw. Grep-Test), keine Netzwerkaufrufe außer `IWeatherProvider`, Koordinaten-Rundung getestet.
5. **IP-Invariante:** Jedes Asset hat einen Eintrag in `ASSETS.md` (Quelle, Lizenz, Autor). Fehlt er, schlägt der CI-Check fehl.
6. **Unsicherheit an Boss eskalieren**, statt zu raten. Das gilt für alles Optische und für alles, was echtes Windows-Verhalten betrifft (dann manuelle Checkliste anlegen).
7. Jede nicht-triviale Entscheidung bekommt eine kurze ADR in `docs/adr/`.

---

## 14. Risiken und Gegenmaßnahmen

| Risiko | Gegenmaßnahme |
|---|---|
| Overlay liegt über Start-Menü, Notifications oder UAC | Foreground-Klasse erkennen → ausblenden. UAC (Secure Desktop) ist ohnehin separat |
| Fullscreen-Spiele oder Anti-Cheat reagieren auf Overlays | Bei Fullscreen bzw. D3D-Exclusive pausieren und Overlay verstecken. Keine Injection, keine Hooks in fremde Prozesse (nur `WINEVENT_OUTOFCONTEXT`) |
| Click-Through vs. interaktive Visitors | Getrennte Mini-Hit-Fenster statt Umschalten des Overlays |
| Gemischte DPI und negative Monitor-Koordinaten | Durchgehend physische Pixel, Monitor-lokale Transformation erst im Render, Unit-Tests mit Multi-Monitor-Layouts |
| Akkuverbrauch | iGPU-Präferenz, Idle-Stopp, Auto-Smoothness, Low-Res-Passes |
| Event-Flut beim Fenster-Drag | Coalescing auf max. 1 Snapshot pro Frame |
| Wetter-API-Lizenz | MET Norway (kommerziell okay), Provider austauschbar |
| Namens- oder Markenkonflikt | Namensrecherche vor M14, Arbeitstitel ist austauschbar (`AppInfo.Name`) |

---

## 15. Kickoff-Prompt für Fable

```
Lies PLAN.md vollständig. Starte mit Meilenstein M0.
Arbeite auf einem neuen Branch, halte dich strikt an Abschnitt 13 (Arbeitsregeln),
erstelle CLAUDE.md aus Abschnitt 13 und richte CI gemäß Abschnitt 11 ein.
Wenn M0 die DoD erfüllt: PR öffnen, kurz zusammenfassen, was manuell zu prüfen ist,
und auf mein OK warten, bevor du M1 beginnst.
```

Folge-Prompts pro Meilenstein:
```
Setze M<n> aus PLAN.md um. Lies vorher die ADRs und den Code aus den vorherigen Meilensteinen.
Erzeuge Previews für alle neuen Effekte und prüfe sie selbst, bevor du den PR öffnest.
Lege docs/manual-tests/M<n>.md mit allem an, was ich auf echtem Windows testen muss.
```
