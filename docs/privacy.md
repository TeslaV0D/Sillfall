# Privacy policy

Last updated: 12 November 2026.

This policy describes Weatherly for Windows (the working title; the name on the Microsoft Store may differ).
The German text is [Datenschutzerklärung](privacy.de.md).

Contact: [GitHub issues](https://github.com/TeslaV0D/weatherly_windows/issues).

## What the app does

Weatherly draws animated weather on top of the open windows. Clicks pass through the weather.
There is no account, no advertising, and no analytics.

## Data that stays on this PC

The app reads where windows are and how big they are, so the weather can land on their edges.
It does not read window titles or window contents, and it does not record the screen.
A setting can hide the weather from screenshots and screen sharing.

For an app rule, and to notice that the Start menu or search is in front, the app reads the file name
of that process (for example `notepad.exe`). The file name is kept in the settings only when you add
a rule. It is not sent anywhere.

Settings, the visitor collection and error reports are files under `%LOCALAPPDATA%\Weatherly`
(`settings.json`, `collection.json`, and `logs`). An error report contains the version, the Windows
version, the architecture and the stack trace. The user name and the profile path are replaced before
the report is written. Reports are not uploaded. You can open the folder from Settings, Advanced,
and you can delete it.

A packaged install keeps those files with the app; uninstalling the package removes them.
An unpackaged run uses the real `%LOCALAPPDATA%\Weatherly` folder, which stays until you delete it.

The visitor share card is a picture of names and points. It leaves the PC only if you copy it,
save it or share it yourself. It does not contain the desktop or window titles.

## Data that is sent

Nothing is sent unless you switch weather sync on.

With weather sync on, the app sends a location rounded to one decimal place (about 11 km) to
MET Norway, `https://api.met.no`, over HTTPS, every 15 to 60 minutes. The request also carries a
user agent with the app name, the version and the address of this repository. It does not carry
your name. MET Norway's terms are at <https://developer.yr.no/doc/TermsOfService/>.

You can pick a city instead of the PC's location. The city list is bundled (GeoNames); searching
it does not use the network. Windows asks before the app may use the PC's location, and the app
runs normally if you say no.

There is no other network use. No telemetry, no advertising identifier, no cookies, no account.

## Permissions

The package declares the location capability and full trust, which a desktop app needs to draw
over other windows. It does not declare camera, microphone, contacts or screen capture.

## Children

The app is not directed at children. It has no account and no social features.

## Changes

The current text is this file in the public repository. The store listing links to it, so an update
of the policy is visible at the same address.
