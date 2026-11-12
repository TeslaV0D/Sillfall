# Wetter-Mapping

Wie aus der Vorhersage Effekte werden (PLAN.md, Abschnitt 5). Code: `WeatherMapper` in
`src/Weatherly.Core/Weather/`. Jede Zeile hat einen Test in `WeatherMapperTests`.

Quelle ist MET Norway, `locationforecast/2.0/compact`. Verwendet wird der Schritt, in dem die aktuelle Zeit liegt.
Niederschlag und Symbol kommen aus `next_1_hours`, sonst aus `next_6_hours` (Menge geteilt durch 6).

## Niederschlag

| Eingabe | Ergebnis |
|---|---|
| Menge in mm/h | Intensität auf Log-Skala: 0,1 mm/h → 0,15 … ≥ 8 mm/h → 1,0. Unter 0,05 mm/h: 0 |
| Symbol mit `rain`/`drizzle` | Regen, mindestens leicht (`light…` 0,2 mm/h, normal 1 mm/h, `heavy…` 4 mm/h). MET meldet bei Schauern oft 0,0 mm |
| Symbol mit `snow` | Schnee (Symbol schlägt Temperatur) |
| Symbol mit `sleet` | halb Regen, halb Schnee |
| Menge ohne passendes Symbol | Schnee bei ≤ 1 °C, sonst Regen |

## Liegender Schnee

| Temperatur | Ergebnis |
|---|---|
| ≤ 2 °C | Frost: Schnee bleibt liegen, wenn es aufhört zu schneien (Regen taut ihn trotzdem) |
| > 2 °C | Schnee schmilzt, sobald es nicht mehr schneit |

## Nebel, Sonne, Wind, Laub

| Eingabe | Ergebnis |
|---|---|
| Symbol `fog` | Nebel 0,75 |
| Feuchte ≥ 97 %, Wind < 3 m/s, Bewölkung ≥ 90 % | Dunst: Nebel 0,3 |
| Bewölkung, Tag/Nacht | Sonne = 1 − Bewölkung, nur tagsüber, nicht bei Niederschlag oder dichtem Nebel |
| Tag/Nacht | Aus dem Sonnenstand berechnet (`SolarPosition`), weil das Compact-Format keinen Tag-Wert liefert |
| `wind_speed`, `wind_from_direction` | Wind = Geschwindigkeit · sin(Richtung, in die er weht) · 400/15. Osten ist rechts, 15 m/s füllen den Regler (±400). Nordwind weht in den Bildschirm hinein und gibt 0 |
| `wind_speed` | Böigkeit 0,3 + v/20, höchstens 1 (das Compact-Format hat keine Böen) |
| Sep–Nov (Süden: Mär–Mai), Wind > 4 m/s | Laub 0,3 + (v − 4)/10, höchstens 1 |
| – | Gras folgt keinem Wetter (es wächst bei Regen schneller) |

## Invert

| Normal | Invertiert |
|---|---|
| klarer Himmel | Regen (Stärke = 1 − Bewölkung) |
| Regen, Schnee oder Nebel (tagsüber) | Sonne |
| Hitze ab 15 °C (voll bei 30 °C) | Schnee, statt Regen |
| Frost | Liegender Schnee taut. Wärme über 2 °C hält ihn |
| Nebel | verschwindet |
| Wind, Laub | bleiben |

## Mischen (Custom Sync)

- Pro Element (Regen, Schnee, Laub, Nebel, Sonne, Wind): `final = lerp(manuell, Wetter, Gewicht)`. Das Gewicht ist 0–100 %, Standard 100 %.
- Bewegt der Nutzer einen Regler, während der Sync läuft, wird dieses Element pausiert und behält den manuellen
  Wert, bis „Sync fortsetzen“ gedrückt wird.
- Der Akku-Faktor wirkt nach dem Mischen.
