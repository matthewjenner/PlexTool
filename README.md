# PlexTool

A Windows desktop app for prepping media and importing it onto a home Plex server. It builds the
correct folder structure, renames files to Plex's naming convention, moves them into the library,
sweeps up empty folders, and triggers a Plex scan - with a preview of every change before it
happens, and no plaintext credentials on disk.

The Plex library lives on a Linux server (in a split rack: a file server holds the media, a
separate Plex box mounts it). PlexTool talks to the storage box directly over SSH/SFTP, so there
is no drive mapping, no elevation, and no UAC prompt. It replaces a set of PowerShell scripts
(folder creation, renaming, moving, empty-folder cleanup) with a single GUI.

## Status

All five workflow surfaces are built: Import, Rename / Normalize, Cleanup, Tools, Settings. See
`Docs/workplan.md` for the phase log and what is deferred.

## The tabs

- **Import** - move a staged item from the storage box's staging folder into the library: it builds
  the target folders, renames each file to Plex form, moves them as an instant server-side rename
  (same filesystem, no upload), optionally removes the emptied source folder, and triggers a Plex
  scan of just that path. Preview shows every move first.
- **Rename / Normalize** - scan a Movies or Shows library and rename media in place toward Plex form
  (`Movie (Year).ext`, `Show - S01E01.ext`). Per-row checkboxes, Select all/none. In-place renames
  keep Plex watched state. This is the bulk "normalize my existing library" pass.
- **Cleanup** - sweep a folder (Movies / Shows / Staging / Custom / Local) for empty directories:
  deepest-first, with a min-age gate, name exclusions (wildcards), symlinks skipped, and optional
  prune-empty-parents. Empty folders only - never files. Preview first.
- **Tools** - quick one-off actions: manual Plex scans (Ctrl+M Movies, Ctrl+T Shows), test the SSH
  and Plex connections, and open the config folder.
- **Settings** - the SSH connection (key or password, host-key trust-on-first-use), split/unified
  topology with the storage->mount path mapping, remote library + staging paths, the Plex URL +
  token with library mapping, naming scheme, and cleanup defaults.

Every operation previews before it applies, and nothing overwrites: collisions are shown and
skipped rather than clobbered.

## Your data stays yours

- Non-secret settings live in `%APPDATA%\PlexTool\settings.json` - per-user, on your machine, never
  in the repo.
- Credentials (SSH password or key passphrase, Plex token) are stored in a **DPAPI-encrypted** file
  (`secrets.dat`) that only your Windows account on this machine can decrypt. Secrets are never
  written in plain text, never logged, and never committed. Key-based SSH auth is recommended so
  ideally no password is stored at all.

## Requirements

- [.NET 10 SDK](https://dotnet.microsoft.com/download)
- Windows
- An SSH-reachable Linux storage box, and (optionally) a Plex token for scan triggers

## Server setup (one-time)

PlexTool acts entirely as your SSH user, so that user needs **read + write + traverse** on the media
tree - it creates folders, renames/moves files, and removes empty folders. Media libraries are
usually owned by a service account (e.g. `plex:plex`) with group-writable permissions, so the
standard setup is to **join that group** rather than change ownership:

```bash
# 1. Let your user write everywhere the media group can
sudo usermod -aG <media-group> "$USER"

# 2. Make new folders inherit the media group (setgid on the library roots).
#    Combined with a umask of 0002 this yields 0775 dirs / 0664 files - group-writable.
sudo chmod g+s /path/to/media /path/to/media/movies /path/to/media/shows /path/to/media/downloads
```

Group membership applies at next login; PlexTool opens a fresh SSH session per action, so it picks
it up immediately. Verify with a quick `mkdir`/`rmdir` in a library folder as your user.

Two things worth knowing:

- **Files created over SFTP are owned by your SSH user**, not the service account - Linux assigns a
  new file the creating uid, and only root can `chown` to another user. That is fine: the *group*
  (plus group-write) is what grants the service account access, which is why the setgid step matters.
- If the same tree is also exported over Samba with `force user`, the setgid step keeps
  SFTP-created and SMB-created files consistent (same group, same masks).

## Build & run

```bash
dotnet build
dotnet run --project Src/PlexTool.App
dotnet test

./Scripts/run.sh            # clean + build + run (optional: Debug|Release)
```

## Project layout

```
Src/PlexTool.Core/     Pure logic - naming, planning, cleanup, path helpers, the IMediaFileSystem
                       abstraction. No UI or platform code. Fully unit-tested.
Src/PlexTool.App/      Avalonia UI + all I/O (local + SFTP backends, services, view models, views).
Tests/PlexTool.Core.Tests/   xUnit tests for Core (against an in-memory IMediaFileSystem fake).
Tests/PlexTool.App.Tests/    Headless Avalonia layout tests.
Docs/                  DESIGN.md, TECHARCH.md, workplan.md.
Scripts/               Bash helpers: run.sh, bump-version.sh.
.github/workflows/     Release pipeline - reads Directory.Build.props, ships to Releases.
```

## Releasing (maintainer notes)

The version lives in `Directory.Build.props` (a single `<VersionPrefix>`). Bump it, push to
`main`, and the workflow reads the version, skips if that release already exists, otherwise tests,
publishes a self-contained win-x64 build, packs it with Velopack, and creates the GitHub release.

```bash
./Scripts/bump-version.sh           # patch (default); pass Minor or Major
```

The repo must be public for the in-app update check to work.
