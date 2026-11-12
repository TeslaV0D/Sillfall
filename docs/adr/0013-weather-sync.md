# ADR-0013: Wetter-Sync

- Status: akzeptiert (Teil 1: Core; Oberfläche und Standortdienst folgen in Teil 2)
- Datum: 2026-11-12
- Meilenstein: M10

## Kontext

PLAN.md, Abschnitt 5: Die Effekte sollen echtem Wetter folgen. Gefordert sind:

- Provider MET Norway,
- Standort aus der Ortung oder einer offline gesuchten Stadt,
- Mapping-Tabelle,
- Invert,
- Gewicht pro Element,
- Pause bei Handeingriff,
- Pflicht-Attribution.

## Entscheidungen

### 1. Netzwerk nur im Provider

`IWeatherProvider` (Core) ist der Vertrag. `MetNorwayProvider` in `Weather/Providers/` ist die einzige Klasse mit
Netzwerk-Code (Privacy-Test).

- **Rundung:** Der Provider rundet den Standort selbst auf eine Dezimale (etwa 11 km), bevor er die Adresse baut.
  Getestet in `PrivacyInvariantTests` und `WeatherSyncTests`.
- **User-Agent:** `<AppInfo.Name>/<Version> github.com/TeslaV0D/weatherly_windows`. Die MET-Bedingungen verlangen
  eine Kennung mit Kontakt. Über den Nutzer steht darin nichts.
- **Caching:** `If-Modified-Since` mit dem letzten `Last-Modified`. Bei 304 bleibt die Vorhersage. Fehler kommen als
  Ergebnis zurück, nie als Exception.

### 2. Polling als Zustandsmaschine ohne Uhr

`WeatherSync` bekommt die Zeit übergeben. So lässt sich der Plan testen, ohne zu warten:

- nächste Anfrage laut `Expires`, begrenzt auf 15 bis 60 Minuten,
- Backoff 1, 2, 4 … bis 30 Minuten,
- offline gilt die letzte Vorhersage weiter (Zustand `Offline`),
- ein anderer Ort fängt neu an.

### 3. Tag und Nacht

Das Compact-Format hat kein `is_day`, und viele Symbole tragen kein `_day`/`_night`. Deshalb rechnet `SolarPosition`
den Sonnenstand mit den Almanach-Formeln (auf etwa 1° genau). Getestet mit Berlin, Tromsø (Polarnacht), Sydney und
dem Äquator.

### 4. Nebel und Böen ohne Messwert

`fog_area_fraction` und Böen gibt es nur im Complete-Format.

- **Nebel:** aus dem Symbol `fog`, plus einer Dunst-Regel aus Feuchte, Wind und Bewölkung.
- **Böigkeit:** aus der Windgeschwindigkeit.

Der Wechsel auf das Complete-Format lohnt sich derzeit nicht: Die Antwort wäre etwa dreimal so groß.

### 5. Frost hält den Schnee

Bisher schmolz Schnee, sobald die Intensität 0 war. Für „Schmelzen bei > 2 °C“ hat `SnowEffect` jetzt `Frozen`
(über `OverlayOptions.SnowStays`). Bei Frost bleibt liegender Schnee, Regen taut ihn weiter. Das gilt nur, solange
der Sync den Schnee steuert, also nicht bei einem pausierten Schnee oder bei Gewicht 0.

### 6. Invert

Die Regeln stehen in `docs/weather-mapping.md`:

- klar → Regen,
- nass → Sonne,
- Hitze → Schnee,
- Frost taut,
- Nebel verschwindet,
- Wind und Laub bleiben.

PLAN.md nennt nur „spiegeln“. Diese Auslegung ist meine und wird bei der Abnahme bestätigt.

### 7. Städteliste offline

GeoNames `cities15000` (CC BY 4.0, im Plan vorgesehen) wird von `tools/cities/build_cities.py` zu einer gzip-TSV
mit 1,5 MB verdichtet und als Ressource in Core eingebettet.

- **Suchschlüssel:** Das Skript legt sie fertig an: Name, ASCII-Name und lateinische Alternativnamen ohne Akzente,
  dazu die deutsche Schreibweise mit ae/oe/ue. So findet „Muenchen“, „München“ und „Munich“ dieselbe Stadt.
- **Speicher:** etwa 5 MB, geladen erst beim ersten Suchen.
- **Postleitzahlen:** Die PLZ-Suche (GeoNames Postal, Download pro Land) ist laut Plan optional. Sie ist
  zurückgestellt und nicht Teil von M10.

## Konsequenzen

- Die Attribution „Weather data: MET Norway (CC BY 4.0)“ und „City list: GeoNames (CC BY 4.0)“ muss in den
  Sync-Einstellungen und auf der Info-Seite stehen (Teil 2).
- Ob die Grenzen der Mapping-Tabelle sich gut anfühlen (8 mm/h als „voll“, Nebel 0,75, 15 m/s als voller Wind),
  zeigt erst echtes Wetter. Die Werte stehen als Konstanten in `WeatherMapper`.
