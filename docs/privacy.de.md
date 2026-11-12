# Datenschutzerklärung

Stand: 12. November 2026.

Diese Erklärung beschreibt Weatherly für Windows (der Arbeitstitel; der Name im Microsoft Store kann
abweichen). Die englische Fassung ist [Privacy policy](privacy.md).

Kontakt: [GitHub-Issues](https://github.com/TeslaV0D/weatherly_windows/issues).

## Was die App tut

Weatherly zeichnet animiertes Wetter über die offenen Fenster. Klicks gehen durch das Wetter hindurch.
Es gibt kein Konto, keine Werbung und keine Analyse.

## Daten, die auf diesem PC bleiben

Die App liest, wo Fenster sind und wie groß sie sind, damit das Wetter auf den Kanten liegen kann.
Sie liest keine Fenstertitel und keine Fensterinhalte und nimmt den Bildschirm nicht auf.
Eine Einstellung kann das Wetter vor Screenshots und Bildschirmübertragung verbergen.

Für eine App-Regel und um zu erkennen, dass das Startmenü oder die Suche vorn ist, liest die App den
Dateinamen des Prozesses (zum Beispiel `notepad.exe`). Der Dateiname landet nur dann in den
Einstellungen, wenn du eine Regel anlegst. Gesendet wird er nicht.

Einstellungen, die Besuchersammlung und Fehlerberichte liegen unter `%LOCALAPPDATA%\Weatherly`
(`settings.json`, `collection.json` und `logs`). Ein Fehlerbericht enthält Version, Windows-Version,
Architektur und den Stacktrace. Benutzername und Profilpfad werden ersetzt, bevor der Bericht
geschrieben wird. Berichte werden nicht hochgeladen. Den Ordner öffnest du unter Einstellungen,
Erweitert, und du kannst ihn löschen.

Eine Installation als Paket hält diese Dateien bei der App; das Deinstallieren entfernt sie.
Ein Start ohne Paket nutzt den echten Ordner `%LOCALAPPDATA%\Weatherly`, der bleibt, bis du ihn löschst.

Die Teilen-Karte der Besucher ist ein Bild mit Namen und Punkten. Sie verlässt den PC nur, wenn du sie
selbst kopierst, speicherst oder teilst. Desktop und Fenstertitel sind nicht darauf.

## Daten, die gesendet werden

Es wird nichts gesendet, solange du den Wetter-Sync nicht einschaltest.

Mit Wetter-Sync geht ein auf eine Dezimale gerundeter Standort (etwa 11 km) an MET Norway,
`https://api.met.no`, über HTTPS, alle 15 bis 60 Minuten. Die Anfrage trägt außerdem einen User-Agent
mit App-Name, Version und der Adresse dieses Repositorys. Dein Name steht darin nicht. Die Bedingungen
von MET Norway stehen unter <https://developer.yr.no/doc/TermsOfService/>.

Statt der Ortung des PCs kannst du eine Stadt wählen. Die Städteliste ist eingebaut (GeoNames); die
Suche braucht kein Netz. Windows fragt, bevor die App die Ortung des PCs nutzen darf, und die App läuft
normal weiter, wenn du ablehnst.

Es gibt keine weitere Netzwerknutzung. Keine Telemetrie, keine Werbe-ID, keine Cookies, kein Konto.

## Berechtigungen

Das Paket meldet die Ortung und Full Trust an, das eine Desktop-App braucht, um über andere Fenster zu
zeichnen. Kamera, Mikrofon, Kontakte oder Bildschirmaufnahme meldet es nicht an.

## Kinder

Die App richtet sich nicht an Kinder. Sie hat kein Konto und keine sozialen Funktionen.

## Änderungen

Der aktuelle Text ist diese Datei im öffentlichen Repository. Der Store-Eintrag verlinkt darauf, eine
Aktualisierung ist also unter derselben Adresse sichtbar.
