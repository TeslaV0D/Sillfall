# Assets

Jedes Asset im Repo braucht hier einen Eintrag mit Quelle, Lizenz und Autor.
Der Test `AssetRegistryTests` (läuft im CI-Job `core`) schlägt fehl, wenn

- eine Mediendatei (Bild, Audio, Font) oder irgendeine Datei in einem `Assets/`-Ordner hier fehlt,
- oder ein Eintrag auf eine Datei zeigt, die es nicht mehr gibt.

**Regeln**

- Keine Charaktere, Namen, Grafiken, Sounds, Texte oder Screenshots der Referenz-App.
- Erlaubt: selbst erstellt (SVG, prozedural) oder CC0. Andere Lizenzen nur nach Rücksprache mit Boss.
- Erste Spalte: Pfad relativ zum Repo-Root in Backticks. Ein Pfad mit `/` am Ende deckt den ganzen Ordner ab.
  Ordner-Einträge sind nur für einheitlich erzeugte Dateien gedacht (z. B. Golden-Images aus dem eigenen Renderer).

## Effekt-Icons

Die sechs Effekt-Symbole in Einstellungen und Flyout (Tropfen, Schneestern, Blatt, Nebelstreifen, Sonne, Grashalme)
sind selbst gezeichnete Pfade im Code (`Ui.IconPath` in `src/Weatherly.App/Ui.cs`), MIT (Projektlizenz),
Weatherly-Projekt. Keine Dateien, daher kein Tabelleneintrag.

## App-Icons

Prozedural gezeichnet (Wolke mit drei Regentropfen auf einer abgerundeten Verlaufsfläche).
Neu erzeugen mit `python3 tools/icons/generate_app_icons.py` (braucht Pillow).

| Datei | Quelle | Lizenz | Autor |
|---|---|---|---|
| `src/Weatherly.App/Assets/AppIcon.ico` | `tools/icons/generate_app_icons.py` | MIT (Projektlizenz) | Weatherly-Projekt |
| `src/Weatherly.App/Assets/SplashScreen.scale-200.png` | `tools/icons/generate_app_icons.py` | MIT (Projektlizenz) | Weatherly-Projekt |
| `src/Weatherly.App/Assets/Square150x150Logo.scale-200.png` | `tools/icons/generate_app_icons.py` | MIT (Projektlizenz) | Weatherly-Projekt |
| `src/Weatherly.App/Assets/Square44x44Logo.scale-200.png` | `tools/icons/generate_app_icons.py` | MIT (Projektlizenz) | Weatherly-Projekt |
| `src/Weatherly.App/Assets/Square44x44Logo.targetsize-24_altform-unplated.png` | `tools/icons/generate_app_icons.py` | MIT (Projektlizenz) | Weatherly-Projekt |
| `src/Weatherly.App/Assets/Square44x44Logo.targetsize-48_altform-lightunplated.png` | `tools/icons/generate_app_icons.py` | MIT (Projektlizenz) | Weatherly-Projekt |
| `src/Weatherly.App/Assets/StoreLogo.png` | `tools/icons/generate_app_icons.py` | MIT (Projektlizenz) | Weatherly-Projekt |
| `src/Weatherly.App/Assets/Wide310x150Logo.scale-200.png` | `tools/icons/generate_app_icons.py` | MIT (Projektlizenz) | Weatherly-Projekt |

## Golden-Images

Referenzbilder der Render-Tests (`GoldenImageTests`), gerendert vom eigenen Renderer mit WARP aus den
Szenen in `tools/Weatherly.FrameDump/Scenes/`. Neue oder geänderte Goldens werden nach Sichtprüfung aus
dem CI-Artefakt `previews` (`render/golden-actual-*.png`) übernommen.

| Datei | Quelle | Lizenz | Autor |
|---|---|---|---|
| `tests/Weatherly.Render.Tests/Goldens/` | `GoldenImageTests` (eigener Renderer, WARP) | MIT (Projektlizenz) | Weatherly-Projekt |

## Daten

| Datei | Quelle | Lizenz | Autor |
|---|---|---|---|
| `src/Weatherly.Core/Weather/Cities/cities15000.tsv.gz` | GeoNames `cities15000`, verdichtet mit `tools/cities/build_cities.py` | CC BY 4.0 (Attribution in der App) | GeoNames (geonames.org) |
