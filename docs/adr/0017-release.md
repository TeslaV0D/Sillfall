# ADR-0017: Release

- Status: akzeptiert
- Datum: 2026-11-12
- Meilenstein: M14

## Kontext

PLAN.md, M14 verlangt:

- MSIX für x64 und ARM64,
- Store-Eintrag mit eigenen Screenshots,
- Datenschutzerklärung,
- winget-Manifest,
- Signierung (Store oder Azure Trusted Signing, inzwischen Artifact Signing),
- Namensprüfung.

Abnahme ist die Store-Zertifizierung. Die kann dieses Repository nicht selbst bestehen: dafür fehlen
Partner-Center-Konto, reservierter Name und die Screenshots vom echten Desktop.

## Entscheidungen

### 1. Der Arbeitstitel bleibt, bis der Name reserviert ist

Geprüft am 12. November 2026:

- Im Mac App Store gibt es „Weatherly“ von Gloop Labs, LLC, eine Wetter-App für die Menüleiste
  (id 1499228739). Dieselbe Warengruppe. PLAN.md nennt diesen Konflikt schon.
- Beim USPTO ist „WEATHERLY“ als Marke 2180348 eingetragen (Kellogg Brown & Root LLC, Klassen 037 und
  042, Anlagenbau und zugehörige technische Dienste). Das sind andere Waren. Allein deshalb wird der
  Name nicht verworfen, aber das Wort ist als Marke lebendig.
- Eine Suche im Microsoft Store hat keinen Eintrag mit dem Titel Weatherly gezeigt. Das ist keine
  Reservierung. Ob der Name frei ist, entscheidet erst Partner Center.

Einen Ersatznamen raten wir nicht. `AppInfo.Name` bleibt „Weatherly“. Der Store-Text in
`docs/store/listing.md` nutzt denselben Arbeitstitel und wird erst eingereicht, wenn der Name
reserviert ist.

Zwei Bezeichner ändern sich bei einer Umbenennung nicht, sonst gehen geteilte Links und die
Autostart-Wahl verloren: das Schema `weatherly://` (`AppInfo.UriScheme`) und die Startup-Task-Id
`WeatherlyStartup` (`AppInfo.StartupTaskId`).

### 2. Entwicklungspaket und Store-Identität

`Package.appxmanifest` trägt die Entwicklungsidentität `Weatherly.Dev` / `CN=Weatherly Development`.
Beim MSIX-Build schreibt das Target `StampPackageManifest` Name, Publisher, Version und die sichtbaren
Namen in eine Kopie. Ohne Vorgaben bleiben die Entwicklungsdaten, die Version kommt aus
`Directory.Build.props` (vierstellig). Für die Einreichung setzen die Variablen die reservierte
Identität:

- `WEATHERLY_PACKAGE_NAME`
- `WEATHERLY_PACKAGE_PUBLISHER` (Zertifikats-Subjekt, beginnt mit `CN=`)
- `WEATHERLY_PUBLISHER_DISPLAY_NAME`

Der Publisher muss zum Signierzertifikat passen.

### 3. Zwei Signaturen, ein Job

`tools/ci/package.ps1` baut pro Architektur:

- `*.unsigned.msix` für Partner Center. Der Store signiert selbst.
- `*.msix` mit einem Zertifikat, das der Lauf erzeugt, plus `*.cer` zum Querladen. Der private Schlüssel
  wird nicht aufgehoben. Dieses Paket ist nicht öffentlich vertrauenswürdig.

Der Job `package` läuft bei Pull Requests, bei „Run workflow“ und bei Tags `v*`. Nur ein Tag, dessen
Name `v` plus die Version aus `Directory.Build.props` ist, darf paketieren. Ein Tag legt zusätzlich
einen Entwurf einer GitHub-Release an.

Öffentlich signiert wird nur auf einem Tag, und nur wenn die reservierte Identität und die Secrets für
Azure Artifact Signing gesetzt sind (`azure/artifact-signing-action`, der Nachfolger von Trusted
Signing). Signiert wird eine Kopie des unsignierten Pakets. Fehlen die Secrets, bleibt der Entwurf beim
Entwicklungszertifikat. winget bekommt erst das öffentlich signierte Paket.

### 4. Datenschutzerklärung und winget

Die Erklärung liegt auf Englisch in `docs/privacy.md` und auf Deutsch in `docs/privacy.de.md`. Die
About-Seite verlinkt die englische Adresse (`AppInfo.PrivacyPolicyUrl`). Dieselbe Adresse steht im
Store-Text und im winget-Manifest. Das Repository ist öffentlich, die Seite ist ohne Konto erreichbar.

winget: `winget/TeslaV0D.Weatherly/0.1.0/`. Die Installer-Hashes sind 64 Nullen, absichtlich. Sie werden
ersetzt, wenn ein signiertes Release existiert (`wingetcreate`), nicht vorher. Die Kennung
`TeslaV0D.Weatherly` steht fest, sobald das Manifest bei winget angenommen ist.

## Konsequenzen

- Die Checkliste M14 ist die Abnahme: Name reservieren, Screenshots selbst aufnehmen, unsigniertes Paket
  hochladen, Zertifizierung abwarten, danach winget.
- Ein Entwicklungszertifikat installiert das Paket nur, wenn das `.cer` unter „Vertrauenswürdige Personen“
  liegt und das Querladen erlaubt ist.
