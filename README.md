# PlexTool

A Windows desktop app for prepping media and importing it onto a home Plex server. It creates the
correct folder structure, renames files to Plex's naming convention, transfers them onto the
server, cleans up empty folders, and triggers a Plex library scan - all with a preview of every
change before it happens.

The Plex server is expected to be a Linux (Ubuntu) box on your network. PlexTool talks to it
directly over SSH/SFTP, so there is no drive mapping, no elevation, and no UAC prompt.

PlexTool replaces a set of PowerShell scripts (folder creation, renaming, moving, empty-folder
cleanup) with a single GUI that shares one connection and one set of saved settings.

## Status

Early development. Phase 0 (project scaffold, build/release/auto-update pipeline, settings and
encrypted-secret storage, app shell) is complete. The operation tabs land phase by phase - see
`Docs/workplan.md`.

## Features (planned)

- **Import** - move staged media onto the server: build the remote folder, upload with progress,
  verify, remove the local source, and scan just that path in Plex. A watch mode auto-imports new
  arrivals.
- **New Structure** - create `Movie (Year)` or `Show/Season NN` folders on the server or locally.
- **Rename / Normalize** - rename to Plex's recommended form with a dry-run preview; in-place so
  Plex keeps watched state. Includes a bulk pass to normalize an existing library.
- **Cleanup** - remove empty folders with a min-age gate, name exclusions, and optional
  parent-pruning; once or continuously.
- **Auto-update** - checks GitHub Releases on startup and hourly; a banner offers Install / Skip /
  Later. Updates are delivered via Velopack.

## Your data stays yours

- Settings live under `%APPDATA%\PlexTool` - per-user, on your machine, never in the repo.
- Credentials (SSH password or key passphrase, Plex token) are stored in a **DPAPI-encrypted**
  file that only your Windows account on this machine can decrypt. Secrets are never written in
  plain text, never logged, and never committed. Key-based SSH auth is recommended so ideally no
  password is stored at all.

## Requirements

- [.NET 10 SDK](https://dotnet.microsoft.com/download)
- Windows
- An SSH-reachable Linux Plex server, and (optionally) a Plex token for scan triggers

## Build & run

```bash
dotnet build
dotnet run --project Src/PlexTool.App
dotnet test

./Scripts/run.sh            # clean + build + run (optional: Debug|Release)
```

## Project layout

```
Src/PlexTool.Core/     Pure logic - naming, planning, cleanup, the IMediaFileSystem abstraction.
Src/PlexTool.App/      Avalonia UI + all I/O (local + SFTP backends, services, runners).
Tests/                 xUnit tests for Core.
Docs/                  DESIGN.md, TECHARCH.md, workplan.md.
Scripts/               Bash helpers: run.sh, bump-version.sh.
.github/workflows/     Release pipeline - reads Directory.Build.props, ships to Releases.
```

## Releasing (maintainer notes)

The version lives in `Directory.Build.props` (a single `<VersionPrefix>`). Bump it, push to
`main`, and the workflow reads the version, skips if that release already exists, otherwise tests,
publishes a self-contained win-x64 build, packs it with Velopack, and creates the GitHub release.

```bash
./Scripts/bump-version.sh           # 0.1.0 -> 0.1.1 (default: Patch)
./Scripts/bump-version.sh Minor     # 0.1.5 -> 0.2.0
```

The repo must be public for the in-app update check to work.
