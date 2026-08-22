# Releasing & the in-app updater

How MicForge is built into an installer and how the in-app updater ships new versions.

## How the in-app updater works

Settings → **Updates** ([`MainViewModel`](../ViewModels/MainViewModel.cs)) uses the shared
`DotNetLib.Core.Updating` service (from the local NuGet feed — see `nuget.config`), and also checks
once on startup:

1. Queries `https://api.github.com/repos/lukr-99/MicForge/releases/latest` (public API, no auth).
2. Compares the release tag against the running assembly version.
3. If newer, the user can install it: downloads the `.exe` asset and launches it, then quits so the
   installer can replace files.

For this to work, each release must attach the **installer `.exe`** built below, and the repo's
Releases must be public.

## Building the installer

Requires the .NET 10 SDK and **Inno Setup 6** (ISCC). One command:

```
./installer/build-installer.ps1            # reads <Version> from the csproj
./installer/build-installer.ps1 -Version 1.9.0
```

It publishes a self-contained `win-x64` build and compiles
[`installer/MicForge.iss`](../installer/MicForge.iss) into
`installer/MicForge-Setup-<version>.exe` — a per-user install (no admin/UAC) to
`%LocalAppData%\Programs\MicForge` with a Start-menu shortcut and an uninstaller entry. The built
`.exe` and `installer/publish/` are gitignored (Release assets, not source).

## Publishing a release the updater will find

1. Bump `<Version>` (+ `FileVersion`/`AssemblyVersion`) in `MicForge.csproj`, and `MyAppVersion` /
   `VersionInfoVersion` in `installer/MicForge.iss`.
2. `./installer/build-installer.ps1`.
3. Create a **GitHub Release** tagged `v<version>` and attach `MicForge-Setup-<version>.exe`.
4. Ensure the repo's Releases are **public**.

## Current state (2026-08-23)

- Updater wired (v1.8.0, startup + manual check). Installer script added; `MicForge-Setup-1.8.0.exe`
  builds successfully. No GitHub Release published yet — until one exists with the `.exe` asset, the
  in-app check reports up to date.
