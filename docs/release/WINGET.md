# winget (M14)

Submit only after a **signed** MSIX is attached to a GitHub Release (or the Microsoft Store URL is stable). Unsigned CI artifacts are not valid winget installers.

## Manifests

Templates live in `packaging/winget/`. Before the pull request to [microsoft/winget-pkgs](https://github.com/microsoft/winget-pkgs):

1. Publish `Sillfall-<version>-x64.msix` and `Sillfall-<version>-arm64.msix` on a GitHub Release.
2. Replace `InstallerUrl` and `InstallerSha256` in `Sillfall.Sillfall.installer.yaml`.

```powershell
Get-FileHash .\Sillfall-0.1.0-x64.msix -Algorithm SHA256
```

3. Set `PackageVersion` and `ReleaseDate` to that release.
4. Validate locally (Windows, winget installed):

```powershell
winget validate --manifest packaging/winget
```

5. Open a pull request with [WingetCreate](https://github.com/microsoft/winget-create) or by copying the folder to `manifests/s/Sillfall/Sillfall/<version>/`.

Package identifier: `Sillfall.Sillfall`.

Store-only distribution can use `InstallerType: msstore` and `ProductCode` equal to the Store product id instead of file hashes. Use that path if you do not want a sideload MSIX.
