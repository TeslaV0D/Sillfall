# Sillfall privacy policy

Last updated: 12 November 2026

Sillfall is a Windows desktop app that draws animated weather on the edges of open windows. This policy describes what stays on your PC and what leaves it.

## What the app reads

- **Window geometry.** Position and size of visible windows, so weather can land on their edges. Sillfall does not read window titles, window contents, or the screen.
- **Process file name, only for rules you create.** If you add an app rule (“pause while this app is in front”), Sillfall stores that executable’s file name on this PC. It does not send it anywhere.
- **Cursor position, while leaf effects are on.** Used only to stir leaves near the pointer. It is not recorded.
- **Optional location, only if you turn weather sync on.** Windows asks first. The position is rounded to about 11 km before it is used, then sent only to the weather service below. It is not stored in settings.

## What leaves the PC

Only when weather sync is switched on:

- A rounded location, or the city you searched for, is requested from **MET Norway** (`api.met.no`, Locationforecast). The request includes a product User-Agent (`Sillfall/<version>` and the project URL). MET Norway’s terms apply to that request.
- Nothing else is uploaded. There is no account, no analytics, and no crash upload.

City search uses a city list shipped inside the app. That search does not use the network.

## What stays on the PC

Under `%LOCALAPPDATA%\Sillfall`:

- `settings.json` — your effects, rules, and options
- `collection.json` — visitor album and score, if you use visitors
- `logs\` — local error reports, if the app crashes. Paths and user names are redacted. Reports are not sent.

Uninstalling the app does not always delete this folder. Delete it yourself if you want the data gone.

## Share cards and scene links

If you copy a score card or a `sillfall://` scene link, that content goes only where you paste it. Sillfall does not post it.

## Children

Sillfall is not directed at children and does not collect personal information for an account.

## Contact

Open an issue on the project repository, or write to the publisher listed on the Microsoft Store listing once the app is published.
