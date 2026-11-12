# ADR-0013: Weather sync

- Status: accepted
- Date: 2026-11-12
- Milestone: M10

## Context

PLAN.md, section 5: Effects should follow real weather. Required:

- MET Norway provider,
- location from device geolocation or offline city search,
- mapping table,
- invert,
- weight per element,
- pause on manual override,
- mandatory attribution.

## Decisions

### 1. Network only in the provider

`IWeatherProvider` (Core) is the contract. `MetNorwayProvider` in `Weather/Providers/` is the only class with network code (privacy test).

- **Rounding:** The provider rounds location to one decimal (~11 km) before building the URL. Tested in `PrivacyInvariantTests` and `WeatherSyncTests`.
- **User-Agent:** `<AppInfo.Name>/<Version> github.com/TeslaV0D/weatherly_windows`. MET terms require an identifier with contact. Nothing about the user is included.
- **Caching:** `If-Modified-Since` with last `Last-Modified`. On 304 the forecast is kept. Errors return as results, never as exceptions.

### 2. Polling as a state machine without a clock

`WeatherSync` receives time from outside so the schedule is testable without waiting:

- next request per `Expires`, clamped 15–60 minutes,
- backoff 1, 2, 4 … up to 30 minutes,
- offline keeps the last forecast (`Offline` state),
- a new location starts fresh.

### 3. Day and night

Compact format has no `is_day`, and many symbols lack `_day`/`_night`. `SolarPosition` computes sun altitude with almanac formulas (~1° accuracy). Tested with Berlin, Tromsø (polar night), Sydney, and the equator.

### 4. Fog and gusts without direct fields

`fog_area_fraction` and gusts exist only in the Complete format.

- **Fog:** from symbol `fog`, plus a haze rule from humidity, wind, and cloud cover.
- **Gustiness:** from wind speed.

Switching to Complete is not worth it yet: response would be ~3× larger.

### 5. Frost keeps snow on the ground

Snow used to melt as soon as intensity hit 0. For “melt above 2 °C”, `SnowEffect` now has `Frozen` (via `OverlayOptions.SnowStays`). With frost, lying snow remains; rain still melts it. Applies only while sync controls snow, not when snow is paused or weight is 0.

### 6. Invert

Rules are in `docs/weather-mapping.md`:

- clear → rain,
- wet → sun,
- heat → snow,
- frost thaws,
- fog clears,
- wind and leaves unchanged.

PLAN.md only says “mirror”. This interpretation is provisional pending maintainer sign-off.

### 7. Offline city list

GeoNames `cities15000` (CC BY 4.0, per plan) is compressed by `tools/cities/build_cities.py` to a 1.5 MB gzip TSV embedded as a Core resource.

- **Search keys:** Built by the script: name, ASCII name, Latin alternate names without accents, plus German transliteration with ae/oe/ue so “Muenchen”, “München”, and “Munich” find the same city.
- **Memory:** ~5 MB, loaded on first search.
- **Postal codes:** GeoNames postal lookup (per-country download) is optional in the plan. Deferred, not part of M10.

### 8. In the app

- **`AppController`:** Every 30 seconds checks whether a fetch is due and applies the forecast so effects follow the hour boundary.
- **Pause on manual override:** Effect sliders, wind, tray menu, and flyout go through `SetIntensity`/`SetWind`. While sync runs, each such change pauses that element. A banner offers “Resume sync”.
- **Location:** `DeviceLocation` uses the Windows location service at 5 km accuracy. Windows prompts for permission; MSIX manifest has `location` capability. Position is rounded immediately and held only in memory.
- **Smoke test:** No provider; sync never uses the network there.

## Consequences

- Attribution “Weather data: MET Norway (CC BY 4.0)” and “City list: GeoNames (CC BY 4.0)” on sync settings and About.
- Whether mapping thresholds feel right (8 mm/h as “full”, fog 0.75, 15 m/s full wind) needs real weather. Values are constants in `WeatherMapper`.
