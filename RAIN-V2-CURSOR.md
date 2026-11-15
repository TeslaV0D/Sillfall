# Regen v2 – Implementierungsanleitung für Cursor

> Auftrag: Den Regen-Effekt sichtbar schöner und physikalisch glaubwürdiger machen, **ohne** die Budgets aus
> PLAN.md §4 zu reißen (sim < 1,0 ms p95, GPU-Frame < 2,5 ms p95, **0 B Allokation im Frame-Loop**).
>
> Lies vorher `CLAUDE.md`, `docs/adr/0006-rain.md` und `src/Sillfall.Core/Effects/Rain/RainEffect.cs` komplett.
> Alle Regeln aus `CLAUDE.md` gelten weiter. Diese Anleitung ist auf Deutsch, aber **Code, Bezeichner, Kommentare,
> ADR und Checklisten schreibst du auf Englisch** (Projektkonvention).

---

## 0. Rahmen und harte Regeln

- Branch: `feat/rain-v2`, abgezweigt von `main` (nicht vom aktuellen Branch). Ein PR am Ende.
- **Keine Bildtexturen.** Alles bleibt prozedural im Pixelshader (SDF), wie Flocken und Blätter. Damit entfällt
  ein `ASSETS.md`-Eintrag, und es gibt keine Texture-Sampler-Kosten.
- **Kein Hintergrund-Sampling.** Echte Lichtbrechung bräuchte den Desktop-Inhalt – verboten
  (Privacy-Invariante, PLAN.md §3 „Never sample the desktop framebuffer“). Brechung wird nur *vorgetäuscht*
  (dunkler Rand + heller Kern + Glanzpunkt, siehe 2.2).
- `Sillfall.Core` bleibt Windows-frei. `Simulate`/`Emit` allokieren nicht: nur feste SoA-Arrays,
  `stackalloc`/`Span` mit `[SkipLocalsInit]`, kein LINQ, keine Closures, keine `new` im Loop.
- `Sprite` bleibt **48 Byte** (`RenderFrameTests` prüft das).
- Jede Phase = eigener Commit, danach Core-Tests + Render-Tests grün (Goldens dürfen erst in Phase 4 neu).
- Bei optischen Unsicherheiten nicht raten: in die Checkliste schreiben (Regel 6).

### 0.1 Arbeiten in einer Remote-Session (Linux-VM)

Die Remote-Umgebung ist Linux. Dort geht **nicht**: HLSL kompilieren (`Shaders.targets` überspringt das
außerhalb von Windows), `Sillfall.slnx` mit `-p:Platform=x64` bauen, Render-Tests (WARP), Smoke-/Soak-Test, Sandbox.
Deshalb:

- Setup: .NET SDK 10.0.100 oder neuer (siehe `global.json`), z. B. per `dotnet-install.sh --channel 10.0`.
- Lokal nach jedem Commit nur:
  ```bash
  dotnet build Sillfall.CrossPlatform.slnf
  dotnet test --project tests/Sillfall.Core.Tests
  dotnet format Sillfall.CrossPlatform.slnf --verify-no-changes
  ```
- **Shader-Fehler und Render-Tests siehst du nur in CI** (Job `windows`, x64). Nach jedem Commit, der `.hlsl`
  oder `Emit*` ändert, pushen und den CI-Lauf abwarten (`gh run watch`, Logs mit `gh run view --log-failed`).
  HLSL besonders sorgfältig schreiben: Warnungen sind Fehler (`/WX`), z. B. implizite Verkürzung
  `float4 → float3` oder nicht initialisierte Variablen.
- Die `.cso`-Dateien in `src/Sillfall.Render/Shaders/` **nicht** anfassen; der Windows-Build kompiliert die
  Shader selbst aus den `.hlsl`.
- „In der Sandbox feinjustieren“ heißt remote: Werte ändern, pushen, das CI-Artefakt **`previews`** laden
  (`gh run download <run-id> -n previews`) und die FrameDump-Bilder (`framedump/`) ansehen.
- Neue Goldens (Phase 4): Nach dem Löschen der alten schlägt `Test Sillfall.Render` in CI fehl, das ist erwartet.
  Die Kandidaten liegen im Artefakt `previews` unter `render/golden-actual-*.png`. **Nicht selbst committen** –
  im PR auflisten und auf die Freigabe des Maintainers warten.
- Leistungsmessung auf echter GPU geht remote nicht: Zahlen aus den Core-Tests (Sim-Zeit, Sprite-Anzahl) in die
  PR-Beschreibung, die GPU-Punkte bleiben in der Checkliste für den Maintainer offen.

---

## 1. Diagnose: warum der Regen heute nicht gut aussieht

| # | Problem | Ursache im Code |
|---|---|---|
| D1 | Tropfen wirken wie gleichförmige Striche/Bindestriche | `EmitDrops`: `SpriteShape.Streak` = Kapsel mit **gleicher Deckkraft über die ganze Länge**, harte Enden, kein Kopf |
| D2 | Auf hellem Hintergrund fast unsichtbar, auf dunklem grell | Ein einziger hellblauer Farbton, keine Kontrastkante |
| D3 | Regen wirkt wie ein Gitter | Alle Tropfen gleicher Tiefe `z` haben gleiche Größe, gleiche Richtung (`vx` hängt nur von `z` ab) |
| D4 | Dünne ferne Tropfen flimmern | Radius < 1 px → Kantenaliasing beim Wandern über Pixel |
| D5 | „Rauschband“ aus Punkten über jeder Fensterkante (in `rain-heavy-01.png` gut sichtbar) | `Hit`: **jeder** Treffer erzeugt 3–6 runde 1,1-px-Punkte in Zufallsrichtung, Alpha 0,7 |
| D6 | Pfützen sind treppig | `EmitPuddles`: ein Rechteck pro 4-px-Bin + zweites Rechteck als Glanz (2 Sprites/Bin) |
| D7 | Rinnsale (Drips) laufen wie auf Schienen | Konstante Beschleunigung bis `DripMaxSpeed`, Spur verblasst nach Punktanzahl statt nach Zeit |

---

## 2. Phase 1 – Shader und Sprite (Render + Core/Rendering)

### 2.1 Zweiten Parameter im Sprite freigeben

`src/Sillfall.Core/Rendering/Sprite.cs`:

- Das private Feld `_padding` wird zu einer öffentlichen Init-Property `public float Param { get; init; }`
  (gleiche Position, Layout bleibt 48 Byte). XML-Doku: „Second shape parameter, see the shape's docs.
  Ignored by shapes that do not use it.“
- `Sprite.vs.hlsl`: im HLSL-Struct `Padding` → `Param`; im `VertexOutput` und im `PixelInput` von
  `Sprite.ps.hlsl` ein neues Feld `nointerpolation float Param : PARAM;` ergänzen und durchreichen.
- Bestehende Shapes ignorieren `Param` → keine Golden-Änderung durch diesen Schritt.

### 2.2 Neue Shape `Raindrop = 7`

`SpriteShape` um `Raindrop = 7` erweitern (Doku-Kommentar wie die anderen). `Streak` **bleibt** (Drip-Spuren).

Geometrie im Quad (`Local`, x = Bewegungsrichtung, **+x = Kopf/Vorderseite**, wie heute über
`Rotation = atan2(vy, vx)`):

- `HalfSize.x` = halbe Länge, `HalfSize.y` = Kopfradius `r`.
- `Param` = Randstärke 0..1 (0 = kein dunkler Rand, für ferne Tropfen).

Pixelshader-Logik (in `Sprite.ps.hlsl` als **erste** Abfrage in `main`, weil häufigste Shape):

```hlsl
if (input.Shape == 7)
{
    // Raindrop: tapered capsule, head at +x. Bright core, darker rim, tail fades out.
    float r = max(input.HalfSize.y, 0.001);
    float halfLength = max(input.HalfSize.x, r);
    float t = saturate((input.Local.x / halfLength + 1.0) * 0.5);       // 0 tail .. 1 head
    float radius = r * lerp(0.35, 1.0, t);                               // taper towards the tail
    float axis = halfLength - r;
    float2 p = float2(max(abs(input.Local.x) - axis, 0.0), input.Local.y);
    float distance = length(p) - radius;
    float cover = Coverage(distance);

    float along = lerp(0.12, 1.0, t * t);                                // tail fades
    float across = saturate(abs(input.Local.y) / max(radius, 0.001));    // 0 axis .. 1 edge
    float shade = lerp(1.0, 0.45, smoothstep(0.45, 1.0, across) * input.Param); // darker rim

    float4 color = input.Color * (cover * along);
    color.rgb *= shade;

    // Glint near the head: fakes refraction without sampling the desktop.
    float2 glintCenter = float2(axis, -0.35 * r);
    float glint = saturate(1.0 - length(input.Local - glintCenter) / max(0.8 * r, 0.6)) * 0.35;
    color.rgb = min(color.rgb + glint * color.a, color.a);              // stay premultiplied (rgb <= a)
    return color;
}
```

Hinweise:
- `rgb ≤ a` muss gelten (premultiplied). `shade ≤ 1` und das `min` garantieren das.
- Der dunkle Rand macht Tropfen auf hellem Hintergrund lesbar, der helle Kern auf dunklem (behebt D2).
- Kein `pow`, kein `atan2`, keine Schleife: ~20 ALU-Ops, vergleichbar mit Flake.

### 2.3 Neue Shape `WaterFilm = 8` (behebt D6)

Ein Bin wird als **Trapez** gezeichnet: Wasserhöhe links und rechts getrennt, Glanzlinie im Shader statt als
zweites Sprite.

- Quad: Unterkante = Oberkante des Fensters (Fläche), Breite = Bin, Höhe = `max(hL, hR)` + 1 px.
- `Variant` = Höhe links in px, `Param` = Höhe rechts in px (beide schon mit `PixelScale`).
- Im Vertex-Shader kommt `Variant` als `Bend` (float) an – benutze `input.Bend` für `hL`.

```hlsl
if (input.Shape == 8)
{
    // Water film on a top edge: a trapezoid from height Bend (left) to Param (right), glint along the top.
    // Hard sides and bottom: bins abut without seams, and nothing reaches into the window below the edge.
    if (abs(input.Local.x) > input.HalfSize.x || input.Local.y > input.HalfSize.y) return 0;
    float u = saturate((input.Local.x / max(input.HalfSize.x, 0.001) + 1.0) * 0.5);
    float height = lerp(input.Bend, input.Param, u);
    float above = (input.HalfSize.y - input.Local.y) - height;           // > 0 above the water surface
    float cover = Coverage(above);
    float glint = saturate(1.0 - abs(above + 0.6) / 0.8) * 0.55;          // thin bright line just below the top
    float4 color = input.Color * cover;
    color.rgb = min(color.rgb + glint * color.a, color.a);
    return color;
}
```

Achtung: Der Vertex-Shader gibt allen Nicht-Rechteck-Shapes 1 px Rand. Deshalb das harte Abschneiden an
`|x| > HalfSize.x` (sonst überlappen Nachbar-Bins und es entstehen hellere Nähte) und an `y > HalfSize.y`
(sonst ragt der Film 1 px ins Fenster).

`EmitPuddles` umbauen:
1. Pro Bin `b` die Eckhöhen aus Nachbarn mitteln: `hL = h(avg(bins[b-1], bins[b]))`, `hR = h(avg(bins[b], bins[b+1]))`
   mit `h(w) = (1 + 2.5 w) * scale` (bisherige Formel). Am Rand des Feldes den Bin selbst verwenden.
2. **Ein** Sprite pro sichtbarem Bin mit `SpriteShape.WaterFilm`, `Variant = hL`, `Param = hR`.
3. Das separate Highlight-Rechteck entfällt (spart ~50 % der Pfützen-Sprites).
4. Shimmer-Rauschen wie bisher auf das Alpha.

### 2.4 Kleinere Shader-Details

- **Ring (Ripple)**: Ringdicke über `Param` steuerbar machen (Default 0 = heutige 1,2 px, damit nichts
  anderes bricht): `thickness = Param > 0 ? Param : 0.6`. Ripples starten dick (1,0 px) und werden dünner (0,4 px).
- `Streak` bleibt unverändert.

### 2.5 Tests Phase 1 (`tests/Sillfall.Render.Tests/SpriteTests.cs`)

Neue Tests nach dem Muster der vorhandenen (`Method_State_Expectation`):
- `Raindrop_HeadIsBrighterThanTail` – Alpha am Kopf-Ende > 2× Alpha am Schwanz-Ende.
- `Raindrop_WithRim_EdgeIsDarkerThanCore` – Verhältnis `rgb/a` am Rand < im Kern.
- `Raindrop_StaysPremultiplied` – für jedes Pixel `r,g,b ≤ a`.
- `WaterFilm_FollowsLeftAndRightHeights` – linke Spalte höher gefüllt als rechte bei `hL > hR`.
- `WaterFilm_AdjacentBins_HaveNoSeam` – zwei Bins nebeneinander, Alpha an der Naht = Alpha in der Mitte (±2).

---

## 3. Phase 2 – Physik (Core, `RainEffect.cs`)

Alle neuen Zustände als **zusätzliche feste Arrays** (SoA) mit `MaxDrops`/`MaxSplashes`/`MaxDrips` und in
`RemoveDrop`/Splash-Swap/`ReleaseDrip` mitkopieren. Jede neue Konstante als `private const` mit XML-Kommentar
(Einheit!).

### 3.1 Tropfengrößen-Verteilung (behebt D3)

Neues Array `_dropSize` (Durchmesser in mm, 0,5–4,0) und `_dropDrag` (vorberechnete Windanpassungsrate, 1/s).

- Beim Spawn: Marshall-Palmer-artig exponentiell verteilt, große Tropfen selten:
  ```csharp
  float mean = MeanDropDiameter(intensity);               // 0.9 mm (light) .. 1.8 mm (heavy), linear in intensity
  float d = Math.Clamp(-MathF.Log(1f - (_random.NextFloat() * 0.999f)) * mean, MinDiameter, MaxDiameter);
  ```
  `MathF.Log` nur beim Spawn, nicht pro Schritt.
- Fallgeschwindigkeit nach Gunn-Kinzer/Atlas (echte Physik, billig):
  ```csharp
  // Terminal velocity in m/s for a drop of diameter d in mm (Atlas et al. 1973).
  static float TerminalVelocity(float d) => MathF.Max(9.65f - (10.3f * MathF.Exp(-0.6f * d)), 1f);
  ```
  Bildschirm-Geschwindigkeit: `vt(d) * PixelsPerMeter * Parallax(z) * scale` mit
  `Parallax(z) = 0.55f + 0.45f * z` und `PixelsPerMeter ≈ 230` (so landet ein naher 2-mm-Tropfen bei
  ~1500 px/s wie heute). Wert in der Sandbox feinjustieren. **Ergebnis beim Spawn in einem Array
  `_dropVt` cachen**, damit `Exp` nicht pro Schritt läuft.
- Darstellung (in `EmitDrops`): Radius aus Größe **und** Tiefe:
  `radius = (0.35f + 0.3f * d) * (0.45f + 0.55f * z) * scale`.
- **Anti-Flimmern (D4)**: Ist `radius < 0.6f * scale`, setze `radius = 0.6f * scale` und multipliziere Alpha mit
  `radiusOriginal / radiusNeu`. Gleiche Lichtmenge, aber keine Sub-Pixel-Linie.
- Farbe nach Tiefe (Luftperspektive): ferne Tropfen heller/entsättigt, nahe kontrastreicher:
  `Vector3.Lerp(FarTint, color, z)`, `Param` (Rand) = `smoothstep(0.4, 0.9, z)`. Wenn `Settings.Color` gesetzt ist,
  gewinnt diese Farbe (nur Alpha/Rand variieren).
- `TerminalVelocity(float z)` (alt) wird ersetzt. In `Spawn` für die Wind-Margin einen festen Mittelwert benutzen.

### 3.2 Wind mit Trägheit und Böen-Schleiern

In `SimulateDrops`:

- **Trägheit nach Größe**: kleine Tropfen folgen dem Wind schneller.
  `_dropDrag[i] = DragRate / d` beim Spawn (z. B. `DragRate = 6f` → 1-mm-Tropfen passt sich in ~0,17 s an,
  4-mm-Tropfen in ~0,7 s). Pro Schritt:
  `_dropVx[i] += (targetVx - _dropVx[i]) * MathF.Min(1f, _dropDrag[i] * dt);`
  Bei Böen fächert der Regen so natürlich auf.
- **Turbulenz-Schleier**: Pro Schritt eine kleine Tabelle mit horizontalem Zusatzwind je Höhenband:
  ```csharp
  [SkipLocalsInit] // on Simulate or SimulateDrops
  Span<float> gusts = stackalloc float[GustBands];      // GustBands = 32
  float bandHeight = desktop.Height / (float)GustBands;
  for (int b = 0; b < GustBands; b++)
      gusts[b] = Noise1D.Sample((b * 0.35f) + (time * 0.8f), 23) * Turbulence * MathF.Abs(windX);
  ```
  Pro Tropfen: `targetVx += gusts[Math.Clamp((int)((y - top) / bandHeight), 0, GustBands - 1)]`.
  Das sind 32 Noise-Aufrufe pro Schritt statt 12 000. `Turbulence ≈ 0.25`. Bei Windstille (windX = 0) kein Effekt.
- Dafür braucht `SimulateDrops` die Zeit: `context.Time` als `float` übergeben.

### 3.3 Spritzer neu (behebt D5)

`Hit(...)` erhält zusätzlich `d` (Größe), `vx`, `vy` (Aufprallgeschwindigkeit) und den Wasserstand am Trefferpunkt
(`field.Bins[field.BinAt(localX)]`, nur bei Top-Kanten; Boden = 0).

- **Nicht jeder Treffer spritzt**:
  `float energy = d * d * d * (vy / (1500f * scale))²;` → Tropfenzahl `n = (int)(energy * SplashPerEnergy + random)`,
  begrenzt auf 0–4. Kleine/ferne Tropfen erzeugen meist 0–1, große 2–4. Ziel: im Mittel **≤ 1,2 Spritzer pro Treffer**
  (heute 4,5).
- **Kronen-Form**: Winkel nicht gleichverteilt, sondern zwei Keulen links/rechts:
  Seite `s = ±1` zufällig, Winkel ab Horizontale `20°–65°`, Geschwindigkeit
  `v = SplashSpeed * MathF.Sqrt(vy / scale) * (0.6..1.0) * scale`. Horizontal `+ 0.25f * vx` (Wind trägt mit).
- **Nasse vs. trockene Kante**: Wasserstand `> 0.4` → Spritzer × 0.6, Ripple-Größe × 1.6 (es platscht in eine Pfütze);
  trocken → Ripple × 0.6.
- **Worthington-Jet** (optional, billig): großer Tropfen (`d > 2.5`) auf nasser Kante → zusätzlich 1 Tröpfchen
  senkrecht nach oben, halbe Geschwindigkeit, etwas größer.
- **Luftwiderstand** in `SimulateSplashes`: `v *= 1 - 1.5f * dt` (linear genähert, kein `Exp`).
- **Darstellung** in `EmitSplashes`: statt `Disc` jetzt `Raindrop` entlang der Flugrichtung,
  `halfLength = max(r, speed * 0.012f)`, `r ≈ 0.7–1.0 px * scale`, `Param = 0.5`, Alpha blendet über die
  Lebenszeit aus (`life / startLife`, dafür Array `_splashStartLife`).

### 3.4 Rinnsale mit Haft-Gleit-Bewegung (behebt D7)

Neue Arrays: `_dripMass` (Wasser in Bin-Einheiten), `_trailTime` (`MaxDrips * TrailLength`, Zeitstempel je Spurpunkt).

- Spawn mit `mass = WaterPerDrip`.
- **Einspeisen statt Neu-Spawnen**: Bevor `SpawnDrip` einen neuen Drip anlegt, prüfen, ob der zuletzt gespawnte Drip
  derselben Ecke (gleicher Owner + Column) noch in den obersten `8 * scale` px hängt → dann nur `mass += WaterPerDrip`.
  Dafür reicht ein kleines Array „letzter Drip-Index je (Owner, Seite)“ – oder eine lineare Suche über `_drips`
  nur beim Spawn (selten, max. 600, ok).
- **Haften und Gleiten**:
  ```csharp
  float roughness = 0.5f + (0.5f * Noise1D.Sample(_dripY[i] / scale * 0.05f, 31)); // 0..1 along the frame (check Noise1D's range)
  float friction = PinMass * (0.7f + (0.6f * roughness));
  float targetV = MathF.Max(0f, _dripMass[i] - friction) * DripSpeedPerMass * scale;
  _dripVy[i] += (MathF.Min(targetV, DripMaxSpeed * scale) - _dripVy[i]) * MathF.Min(1f, 8f * dt);
  if (_dripVy[i] > 0f) _dripMass[i] -= TrailLoss * _dripVy[i] * dt / scale;   // water stays behind in the trail
  ```
  Effekt: Ein Rinnsal bleibt kurz hängen, sammelt Wasser (Einspeisen), rutscht ruckartig weiter – wie an echten
  Fensterscheiben. Werte so wählen, dass ein Drip mit `WaterPerDrip` sicher unten ankommt (Test!), nur eben in
  Etappen. Hat ein Drip `mass < MinMass` und steht seit > 6 s, verschwindet er (verdunstet), damit der Effekt
  nach dem Regen nicht ewig aktiv bleibt (`IsActive`).
- **Kopf wächst mit Masse**: in `EmitDrips` Kopf als `Raindrop` (Rotation `+π/2`, Kopf unten),
  `r = DripRadius * (0.8f + 0.4f * MathF.Sqrt(mass)) * scale`, `halfLength = 1.5f * r` → Tropfenform.
- **Spur trocknet mit der Zeit**: Alpha je Spurpunkt `0.45f * (1 - age / TrailLifetime)` mit `TrailLifetime ≈ 2.5 s`
  statt nach Index; Breite verjüngt sich zum alten Ende (`0.8 → 0.4 px`).
- Beim Lösen unten (`ReleaseDrip`) bekommt der fallende Tropfen eine Größe aus der Masse:
  `d = Math.Clamp(1.5f + mass, 1.5f, 4f)`.

### 3.5 Kleine Wellen auf Pfützen (optional, Phase 2b)

In `WaterField` ein zweites Array `_wave` + `_waveVelocity` (gleiche Länge wie `_water`, Allokation nur in
Konstruktor/`Resize` wie bei `_water`/`_scratch`).

- `Add(...)` gibt einen Impuls: `_waveVelocity[bin] -= amount * WaveKick`.
- `Step`: einfache Wellengleichung mit Dämpfung (pro Bin: Laplace der Nachbarn, `v += c² * lap * dt; v *= 1 - damping*dt; w += v*dt`).
- Angezeigte Höhe = `water + wave` (nur Darstellung, **Volumen unverändert**, `Total` zählt nur `_water`).
- Bei `Resize`/`Drain`: Wellen auf 0.
- Kosten: ein paar hundert Bins pro Fenster, vernachlässigbar.

### 3.6 Was **nicht** geändert wird

- Budget-Konstanten (`MaxDrops` usw.), Okklusionsregeln (`WindowOcclusion`, ADR-0006 Amendment), Home-Logik,
  `OnWindowEvent`, Spawn-Rate-Formel, `HitsPerSecond` (Sound hängt daran – Treffer zählen weiter, nicht Spritzer).

### 3.7 Tests Phase 2 (`tests/Sillfall.Core.Tests/Effects/Rain/RainEffectTests.cs`)

Bestehende Tests müssen grün bleiben. Anpassen:
- `HighDpi_ScalesDropsAndPuddleBins` filtert auf `SpriteShape.Streak` → auf `SpriteShape.Raindrop` umstellen.

Neu:
- `DropSizes_SmallDropsOutnumberLargeOnes` – Median < Mittelwert, Anteil `d > 3 mm` < 5 %.
- `TerminalVelocity_GrowsWithDiameter` und `..._LevelsOffForLargeDrops` (Atlas-Formel, Grenzwerte).
- `WindGust_SmallDropsFollowFasterThanLargeOnes`.
- `Splashes_HeavyRain_AverageAtMostOnePointTwoPerHit` und `..._StayWithinBudget`.
- `Splashes_DriftWithTheWind` – mittleres `vx` hat das Vorzeichen des Windes.
- `Drips_StickAndSlip_StopAtLeastOnceBeforeTheBottom` (bei kleiner Masse).
- `Drips_WithEnoughWater_AlwaysReachTheBottom`.
- `Drips_FedAtTheCorner_MergeInsteadOfSpawning`.
- `Drips_AfterRain_DisappearSoTheEffectGoesIdle` (`IsActive` wird `false`).
- `Puddles_EmitOneSpritePerWetBin`.
- `Emit_HeavyRain_UsesNoMoreSpritesThanBefore` – Baseline-Zahl **vor** dem Umbau messen und als Konstante
  in den Test schreiben (Kommentar mit Datum).
- Optional 3.5: `Waves_DoNotChangeWaterVolume`.
- `FrameLoop_DoesNotAllocateOnceWarm` und `SimulationStep_StaysWithinTheBudget` **unverändert** grün.

---

## 4. Phase 3 – Performance absichern

Bilanz, die du am Ende mit Zahlen belegen sollst (in der PR-Beschreibung):

| Posten | Vorher | Nachher (Ziel) |
|---|---|---|
| Sprites Tropfen | 1 / Tropfen | 1 / Tropfen (gleich) |
| Sprites Spritzer | ~4,5 / Treffer | ≤ 1,2 / Treffer |
| Sprites Pfütze | 2 / Bin | 1 / Bin |
| Sprites Drip | Kopf + ≤ 12 Spur | gleich |
| Sim pro Tropfen und Schritt | ~15 Flops | + 1 Tabellen-Lookup, 0 transzendente Funktionen |
| PS-Kosten Tropfen | Streak | ~1,5× Streak, aber kleinere Gesamtfläche durch weniger Spritzer |

Regeln:
- Tropfen-Quads **nicht länger** machen als heute (`halfLength ≈ speed * 0.009`). Overdraw ist der Hauptkostenfaktor.
- `Exp`/`Log`/`Sqrt` nur beim Spawn oder pro Treffer, nie pro Tropfen und Schritt.
- In `Sprite.ps.hlsl` die Shape-Abfragen nach Häufigkeit ordnen: `Raindrop` zuerst.
- Messen: `dotnet test --project tests/Sillfall.Core.Tests` (Budget- und Allokationstests),
  `pwsh tools/ci/smoke-test.ps1` (Allokationscheck im echten Loop), Sandbox mit `rain = 1.0` vorher/nachher
  vergleichen. Wenn es eine Frame-Zeit-Anzeige gibt, Werte notieren; sonst mit `tools/ci/soak-test.ps1` lokal.

---

## 5. Phase 4 – Goldens, Doku, Abnahme

1. **Goldens neu erzeugen**: alle `tests/Sillfall.Render.Tests/Goldens/rain-*.png` und `grass-rain-*.png` löschen,
   Render-Tests laufen lassen. Der Test legt `previews/golden-actual-<name>.png` ab. **Nicht blind übernehmen** –
   der Maintainer prüft die Bilder (Regel 6). Erst danach als Goldens committen.
2. Neue FrameDump-Szene `tools/Sillfall.FrameDump/Scenes/rain-gusty.json` (Wind 120 px/s, Böigkeit hoch,
   Intensität 0.8, zwei Fenster), damit Turbulenz und Kronen-Spritzer ein Golden haben.
3. ADR-0006 um einen Abschnitt `## Amendment 2026-11: rain v2 (shapes and physics)` ergänzen (Englisch):
   Raindrop/WaterFilm-Shapes, `Sprite.Param`, Atlas-Fallgeschwindigkeit, Größenverteilung, Splash-Energie,
   Stick-Slip-Drips, Budget-Bilanz.
4. Checkliste `docs/manual-tests/rain-v2.md` (Englisch) mit Punkten zum Abhaken auf echter Hardware:
   - dunkles und helles Hintergrundbild: Tropfen in beiden Fällen sichtbar, nicht grell
   - 100 % und 200 % Skalierung: kein Flimmern ferner Tropfen
   - Böen: Regen fächert auf, wirkt nicht wie ein Gitter
   - Spritzer: kein Rauschband über Fensterkanten bei Starkregen
   - Pfützen: glatte Oberfläche, keine Treppen, keine hellen Nähte
   - Rinnsale: halten an, rutschen weiter; Spuren trocknen ab
   - Leistung: GPU-Zeit und CPU-Last im Task-Manager vorher/nachher bei Intensität 50 % und 100 %
   - Nach Regenende geht der Effekt in den Leerlauf (0 Frames)
5. `docs/manual-tests/` Fortschritt eintragen wie bei den anderen Milestones.

---

## 6. Reihenfolge der Commits

1. `Sprite: expose Param, pass it to the pixel shader` (keine Optikänderung, alle Goldens grün)
2. `Render: Raindrop and WaterFilm shapes, ring thickness` + SpriteTests
3. `Rain: drop sizes, Atlas terminal velocity, size-based wind drag, gust bands`
4. `Rain: energy-based crown splashes`
5. `Rain: stick-slip drips with mass and drying trails`
6. `Rain: emit drops/splashes/drips/puddles with the new shapes` (ab hier ändern sich Goldens)
7. (optional) `Rain: waves on puddles`
8. `Rain v2: goldens, scene rain-gusty, ADR-0006 amendment, checklist`

Nach jedem Commit: auf Windows `dotnet build Sillfall.slnx -p:Platform=x64`, beide Testprojekte,
`dotnet format --verify-no-changes`. In der Remote-Session (Linux) die Befehle aus 0.1 und danach CI prüfen.

## 7. Definition of Done

- [ ] Alle Core- und Render-Tests grün, `dotnet format` sauber, Smoke-Test ohne Allokation.
- [ ] Sprite-Anzahl bei Starkregen ≤ Baseline (Test), Sim-Budget-Test grün.
- [ ] Keine Textur-Assets, kein Desktop-Sampling, Core ohne Windows-Referenzen (`ArchitectureTests`, `PrivacyInvariantTests`).
- [ ] Goldens vom Maintainer visuell abgenommen.
- [ ] ADR-Amendment und Checkliste vorhanden, PR-Beschreibung mit Vorher/Nachher-Zahlen und -Bildern.
