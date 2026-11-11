# Manuelle Checkliste MVP-Cut – Tray und Einstellungen

Automatisch geprüft: Menü-Modell (Häkchen, Befehle, Pause), Tooltip, Speichern und Laden der
Einstellungen (auch kaputte Dateien). Auf dem Windows-Runner prüft der Smoke-Test, dass die Shell das
Tray-Icon annimmt.

## Start

```bash
dotnet run --project src/Weatherly.App -p:WindowsPackageType=None
```

## 1. Tray-Icon

- [ ] Das Icon erscheint im Infobereich (eventuell im Überlauf „^“). Hover zeigt z. B. „Weatherly: clear sky“.
- [ ] Rechtsklick öffnet das Menü. Ein Klick daneben schließt es wieder.
- [ ] Linksklick öffnet das Fenster.
- [ ] Regen → Stark: Es regnet, im Menü ist „Heavy“ markiert, der Tooltip sagt „rain 100 %“.
- [ ] Schnee → Leicht, dann „Pause all effects“: alles weg. Noch einmal: alles wieder da.
- [ ] Sound an/aus über das Menü.
- [ ] Explorer im Task-Manager neu starten: Das Icon kommt zurück.

## 2. Fenster

- [ ] Fenster schließen (X): Das Fenster verschwindet, das Wetter läuft weiter, das Icon bleibt.
- [ ] Über das Icon wieder öffnen: Die Regler zeigen den aktuellen Stand.
- [ ] „Quit“: Icon, Fenster und Wetter sind weg, kein Prozess bleibt übrig.

## 3. Einstellungen

- [ ] Regen 60 %, Schnee leicht, Sound an, Quit, neu starten: Alles ist wie vorher.
- [ ] Datei `%LOCALAPPDATA%\Weatherly\settings.json` mit Unsinn füllen und starten: Die App startet mit Standardwerten.

## 4. Alltag (ein Arbeitstag)

- [ ] Ein paar Stunden mit leichtem Regen oder Schnee laufen lassen: Stört nichts, keine Klicks gehen verloren, kein Fokusklau.
- [ ] Notizen zu allem, was nervt oder gefällt:

## Ergebnis

| Datum | Windows-Version | Ergebnis | Anmerkungen |
|---|---|---|---|
|  |  |  |  |
