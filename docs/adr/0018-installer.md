# ADR-0018: Setup .exe

- Status: accepted
- Date: 2026-11-12
- Milestone: M14

## Context

The Store needs a reserved name and certification before anyone can install Sillfall from there (ADR-0017). The
MSIX in the release is either unsigned (Store upload) or signed with a development certificate that has to be
trusted by hand. Neither is a download a normal user can run. The first public release needs one file that
installs with a double click.

The app already runs unpackaged (`WindowsPackageType=None`, Windows App SDK self-contained). In that mode it
writes the `sillfall://` protocol and the autostart Run entry for the current user itself (`Integration`).

## Decision

A classic setup .exe per architecture, built with **Inno Setup 6** from the unpackaged, self-contained publish
output:

- `tools/ci/installer.ps1` runs `dotnet publish -p:WindowsPackageType=None --self-contained` and compiles
  `tools/installer/Sillfall.iss`. Output: `Sillfall-Setup-<version>-<arch>.exe` plus `SHA256SUMS-setup-<arch>.txt`.
- **Per-user install** by default (`PrivilegesRequired=lowest`, into `%LOCALAPPDATA%\Programs\Sillfall`): no UAC
  prompt, and it matches the app's own HKCU entries. The setup dialog still offers an install for all users.
- Start menu entry, optional desktop icon, English and German setup texts, launch after install. Before
  installing, setup shows a privacy summary (`tools/installer/privacy-en.txt`, `privacy-de.txt`).
- Setup registers `sillfall://` right away. Uninstall removes that key and the autostart value `Sillfall` under
  HKCU, which the app may have written. Settings and the visitor collection in `%LOCALAPPDATA%\Sillfall` stay, as
  the privacy policy says.
- `AppMutex=Local\Sillfall.SingleInstance` (the app's single-instance mutex): setup and uninstall ask to close a
  running Sillfall instead of failing on locked files.
- The `AppId` GUID in the script identifies the installation for updates. It never changes.
- CI job `installer` (x64, ARM64) runs with the same triggers as `package`. Inno Setup is installed on the runner
  with Chocolatey when it is missing.
- A tag `v<version>` now **publishes** the GitHub release directly (no draft) with the setups as the main
  download, plus the MSIX files from ADR-0017.

Alternatives:

- **Portable ZIP:** no Start menu entry, no uninstall, users keep old copies around. Rejected as the main download.
- **WiX/MSI:** more ceremony (component GUIDs, upgrade codes) for the same result. Inno Setup is smaller and needs
  no build integration.
- **Single-file exe:** WinUI 3 with the Windows App SDK self-contained still needs loose files (`.pri`, XAML
  resources); a single file would mean an extractor in front of the app.

### Signing: SignPath Foundation

The setups are signed through SignPath's free program for open-source projects (SignPath.io, certificate by
SignPath Foundation): free for open-source projects and built for GitHub Actions. Azure Artifact Signing stays
the option for the MSIX (ADR-0017).

- The release job uploads both setups as one artifact and submits one signing request
  (`signpath/github-action-submit-signing-request@v3`). It waits up to five hours for the manual approval in
  SignPath, then replaces the setups and their checksums with the signed files.
- The artifact configuration (`tools/installer/signpath-artifact-configuration.xml`) only signs
  `Sillfall-Setup-*.exe` with product name `Sillfall` and the tag's version. Inno Setup writes both into the
  version resource.
- The step only runs when the secret `SIGNPATH_API_TOKEN` and the variables `SIGNPATH_ORGANIZATION_ID`,
  `SIGNPATH_PROJECT_SLUG` and `SIGNPATH_SIGNING_POLICY_SLUG` are set. Without them the release is unsigned and the
  release notes say so.
- SignPath's terms require a released project, a “Code signing policy” section with team roles and a privacy
  statement (README), GitHub-hosted runners, MFA for all members, and a manual approval per release.
- Not signed: the app's own `Sillfall.App.exe` and DLLs inside the setup, and Inno Setup's uninstaller. Signing
  them would need a second signing request before the setup is compiled. Files installed locally carry no
  download mark, so SmartScreen does not check them.

## Consequences

- Until SignPath has accepted the project, the setup is **not code-signed**, and SmartScreen warns about an
  unknown publisher. Signed releases show “SignPath Foundation” as the publisher. SmartScreen reputation still
  builds up with downloads.
- The download is large (the .NET runtime and the Windows App SDK travel with the app), in exchange for nothing
  else to install.
- An unpackaged install has no package identity. Autostart uses the Run key, and the Store version and the
  setup version can be installed side by side; they share `%LOCALAPPDATA%\Sillfall`.
- Checklist M14 has a section for the setup.
