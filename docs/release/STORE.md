# Microsoft Store listing (M14)

Fill these into Partner Center after the name **Sillfall** is reserved. Screenshots must be taken on a real Windows desktop (the images in `docs/media/` are overlay-only renders and are not Store screenshots).

## Reservation

1. Partner Center → Apps and games → New product → MSIX or PWA app.
2. Reserve the name **Sillfall**.
3. Copy the reserved package identity into `src/Sillfall.App/Package.appxmanifest` (`Identity Name` and `Publisher`). The checked-in identity (`Sillfall.Dev` / `CN=Sillfall Development`) is for local builds only.

## Listing copy (en-US)

**Short description** (about 250 characters)

Animated weather for your Windows desktop. Rain, snow, fog, leaves, sunlight, and grass settle on the edges of open windows. Clicks pass through.

**Description**

Sillfall draws a small weather system over the windows you already have open.

Rain splashes and runs down the sides. Snow piles up and slides off when you close or shake a window. Fog, warm light, autumn leaves, and grass each have their own intensity. Turn on weather sync and the scene can follow conditions outside, or the opposite of them.

The overlay does not take clicks. It pauses for fullscreen games, the lock screen, and Remote Desktop. Sound is off until you turn it on.

Sillfall reads window positions and sizes only. It does not read titles or contents, does not record the screen, and does not include analytics. See the privacy policy.

**Category:** Personalization (or Utilities if Personalization is unavailable)

**Privacy policy URL:** publish `docs/PRIVACY.md` (GitHub, or a page you control) and paste that HTTPS URL. Partner Center rejects a listing without one.

**Age rating:** answer the questionnaire honestly. There is no account, no chat, and no user-generated public content. Visitors are simple original characters.

**Capabilities to declare in notes if asked:**

- `runFullTrust` — desktop WinUI app with an overlay window
- `location` — used only after the user turns on weather sync; Windows shows the system prompt

## Packages

CI job `package` (tags `v*`, manual “Run workflow”, and pull requests) uploads unsigned `.msix` artifacts built by `tools/ci/package.ps1` for x64 and ARM64.

For the Store, either:

- upload the packages in Partner Center and let Microsoft sign them after the reserved identity is in the manifest, or
- sign with a trusted certificate and also publish the same signed files as a GitHub Release for winget (see [WINGET.md](WINGET.md)).

## Screenshots to capture

At least four, 1366×768 or larger, with the desktop visible (not a black overlay frame):

1. Rain on several windows
2. Snow settled on window tops
3. Settings, Effects page
4. Tray flyout

Do not include other companies’ names, logos, or app artwork in the shots.
