# Weather mapping

How forecast data becomes effects (PLAN.md, section 5). Code: `WeatherMapper` in `src/Sillfall.Core/Weather/`. Each row has a test in `WeatherMapperTests`.

Source is MET Norway, `locationforecast/2.0/compact`. The step containing the current time is used. Precipitation and symbol come from `next_1_hours`, otherwise from `next_6_hours` (amount divided by 6).

## Precipitation

| Input | Result |
|---|---|
| Amount in mm/h | Intensity on log scale: 0.1 mm/h → 0.15 … ≥ 8 mm/h → 1.0. Below 0.05 mm/h: 0 |
| Symbol with `rain`/`drizzle` | Rain, at least light (`light…` 0.2 mm/h, normal 1 mm/h, `heavy…` 4 mm/h). MET often reports 0.0 mm for showers |
| Symbol with `snow` | Snow (symbol overrides temperature) |
| Symbol with `sleet` | Half rain, half snow |
| Amount without matching symbol | Snow at ≤ 1 °C, else rain |

## Lying snow

| Temperature | Result |
|---|---|
| ≤ 2 °C | Frost: snow stays on surfaces when snowfall stops (rain still melts it) |
| > 2 °C | Snow melts when snowfall stops |

## Fog, sun, wind, leaves

| Input | Result |
|---|---|
| Symbol `fog` | Fog 0.75 |
| Humidity ≥ 97%, wind < 3 m/s, cloud cover ≥ 90% | Haze: fog 0.3 |
| Cloud cover, day/night | Sun = 1 − cloud cover, daytime only, not during precipitation or dense fog |
| Day/night | From sun altitude (`SolarPosition`); compact format has no day flag |
| `wind_speed`, `wind_from_direction` | Wind = speed · sin(direction blowing toward) · 400/15. East is right; 15 m/s fills the slider (±400). North wind blows into the screen → 0 |
| `wind_speed` | Gustiness 0.3 + v/20, max 1 (compact has no gust field) |
| Sep–Nov (southern hemisphere: Mar–May), wind > 4 m/s | Leaves 0.3 + (v − 4)/10, max 1 |
| – | Grass does not follow weather (grows faster in rain) |

## Invert

| Normal | Inverted |
|---|---|
| Clear sky | Rain (strength = 1 − cloud cover) |
| Rain, snow, or fog (daytime) | Sun |
| Heat from 15 °C (full at 30 °C) | Snow instead of rain |
| Frost | Lying snow thaws. Warmth above 2 °C keeps it |
| Fog | Clears |
| Wind, leaves | Unchanged |

## Blending (custom sync)

- Per element (rain, snow, leaves, fog, sun, wind): `final = lerp(manual, weather, weight)`. Weight is 0–100%, default 100%.
- Moving a slider while sync runs pauses that element at the manual value until “Resume sync”.
- Battery factor applies after blending.
