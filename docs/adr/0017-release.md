# ADR-0017: Release

- Status: accepted
- Date: 2026-11-12
- Milestone: M14

## Context

PLAN.md, M14 asks for:

- MSIX for x64 and ARM64,
- a store listing with our own screenshots,
- a privacy policy,
- a winget manifest,
- signing (Store or Azure Trusted Signing, now called Artifact Signing),
- a name check.

Acceptance is Store certification. This repository cannot pass that on its own: it needs a Partner Center
account, a reserved name, and screenshots from a real desktop.

## Decisions

### 1. Product name

M14 was first built under the working title “Weatherly”. The name check on 12 November 2026 found:

- The Mac App Store lists “Weatherly” by Gloop Labs, LLC, a menu bar weather app (id 1499228739). Same
  category. PLAN.md already named this conflict.
- The USPTO has “WEATHERLY” registered as mark 2180348 (Kellogg Brown & Root LLC, classes 037 and 042,
  plant construction and related technical services). Different goods, but the word is a live mark.
- A Microsoft Store search showed no app titled Weatherly. That is not a reservation.

The product has since been renamed to **Sillfall** (`AppInfo.Name`, `<Product>` in `Directory.Build.props`).
Whether Sillfall is free is decided by Partner Center, not by this repository. The listing in
`docs/store/listing.md` is submitted only after the name is reserved.

Two identifiers must not change after the first release, or shared links and the autostart choice are lost:
the scheme `sillfall://` (`AppInfo.UriScheme`) and the startup task id `SillfallStartup`
(`AppInfo.StartupTaskId`). They were renamed together with the product because nothing had shipped yet.

### 2. Development package and store identity

`Package.appxmanifest` carries the development identity `Sillfall.Dev` / `CN=Sillfall Development`.
During the MSIX build the target `StampPackageManifest` writes name, publisher, version and the display
names into a copy. Without overrides the development values stay, and the version comes from
`Directory.Build.props` (four parts). For submission these variables set the reserved identity:

- `SILLFALL_PACKAGE_NAME`
- `SILLFALL_PACKAGE_PUBLISHER` (certificate subject, starts with `CN=`)
- `SILLFALL_PUBLISHER_DISPLAY_NAME`

The publisher must match the signing certificate.

### 3. Two signatures, one job

`tools/ci/package.ps1` builds per architecture:

- `*.unsigned.msix` for Partner Center. The Store signs it.
- `*.msix` signed with a certificate the run creates, plus `*.cer` for sideloading. The private key is not
  kept. This package is not publicly trusted.

The `package` job runs on pull requests, on “Run workflow”, and on tags `v*`. Only a tag named `v` plus the
version from `Directory.Build.props` may package. A tag also creates a draft GitHub release.

Public signing happens only on a tag, and only when the reserved identity and the Azure Artifact Signing
secrets are set (`azure/artifact-signing-action`, the successor of Trusted Signing). A copy of the unsigned
package is signed. Without the secrets the draft keeps the development certificate. winget only gets the
publicly signed package.

### 4. Privacy policy and winget

The policy is in English in `docs/privacy.md` and in German in `docs/privacy.de.md`. The About page links the
English address (`AppInfo.PrivacyPolicyUrl`). The same address is in the store listing and the winget
manifest. The repository is public, so the page is reachable without an account.

winget: `winget/TeslaV0D.Sillfall/0.1.0/`. The installer hashes are 64 zeros on purpose. They are replaced
once a signed release exists (`wingetcreate`), not before. The identifier `TeslaV0D.Sillfall` is fixed once
winget accepts the manifest.

## Consequences

- Checklist M14 is the acceptance: reserve the name, take screenshots, upload the unsigned package, wait for
  certification, then winget.
- A development-signed package installs only when the `.cer` is in “Trusted People” and sideloading is
  allowed.
