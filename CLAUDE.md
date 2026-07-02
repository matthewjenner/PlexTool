# PlexTool

Windows Avalonia (.NET 10) desktop app that preps media locally and imports it onto a remote
Ubuntu Plex server over SSH/SFTP, then triggers a Plex library scan. It replaces four local
PowerShell scripts (folder creation, renaming, moving, empty-folder cleanup) with a GUI that
previews every change before applying it. Infrastructure (build, release, auto-update, settings
persistence) is lifted directly from the Klakr project - keep the two in step.

See `Docs/DESIGN.md` for product detail, `Docs/TECHARCH.md` for architecture, and
`Docs/workplan.md` for the living build tracker (current phase, what is done, what is next,
decisions log). This file is the quick orientation - read it first.

## Stack

- .NET 10, C#
- Avalonia 12 (UI) - pinned to the 12.x line; there is no stable Avalonia 13 yet, and the
  whole Avalonia package family (Desktop, Fluent, Inter) must move together.
- CommunityToolkit.Mvvm (MVVM source generators)
- SSH.NET (SFTP transfer + remote shell commands to the Ubuntu server)
- System.Security.Cryptography.ProtectedData (DPAPI encryption for secrets)
- Velopack (in-app update check + release packaging via the `vpk` CLI in CI)
- System.Text.Json (settings + secret serialization)
- xUnit + FluentAssertions (tests; FluentAssertions pinned to 7.x - see below)

## Layout

```
Src/PlexTool.Core/      Pure logic - naming, planning, cleanup scanning, the IMediaFileSystem
                        abstraction. No UI, no I/O, no platform deps. Fully unit-testable.
Src/PlexTool.App/       Avalonia app + all I/O. Backends (local + SFTP), Services, Execution
                        runners, ViewModels, Views.
Tests/PlexTool.Core.Tests/   xUnit tests for Core (against an in-memory IMediaFileSystem fake).
Docs/                   DESIGN.md, TECHARCH.md, workplan.md.
Scripts/                Bash helpers - run.sh (clean+build+run), bump-version.sh.
.github/workflows/      release.yml - reads Directory.Build.props, ships via Velopack.
Directory.Build.props   Single source of truth for the app version.
```

## Build & run

```bash
dotnet restore
dotnet build
dotnet run --project Src/PlexTool.App
dotnet test

./Scripts/run.sh            # clean + build + run (optional: Debug|Release arg)
```

## Core principle: Core stays pure, App does I/O

`PlexTool.Core` computes *plans* and never touches disk or the network. It defines
`IMediaFileSystem` (list, stat, mkdir, rename/move, delete, open-write-stream) as an interface
only. `PlexTool.App` provides the two implementations - `LocalMediaFileSystem` and
`SftpMediaFileSystem` (SSH.NET) - and executes the plans against whichever the user targets.
This is the direct analogue of Klakr's `IInputHook`/`IInputSimulator` living in Core: it keeps
all parsing/planning/scanning logic testable with an in-memory fake and no real server. Do not
pull Avalonia, SSH.NET, or filesystem calls into Core.

## The four operations (each a tab)

Every operation follows the same shape: **compute a plan -> show it as a reviewable list ->
user confirms -> apply**. That is the GUI form of the scripts' universal `-WhatIf`/`-DryRun`.

- **Import** - staging folder to server: classify Movie/Show, create the remote structure over
  SFTP, upload with byte-progress, verify, delete local source (move semantics), trigger a Plex
  scan of just that path. Watch mode auto-imports new arrivals.
- **New Structure** - create `Movie (Year)` or `Show/Season NN` folders, local or remote.
- **Rename / Normalize** - rename to the Plex form; in-place only (SFTP rename / local move,
  never copy-then-delete) so Plex keeps watched state and metadata. Includes the bulk
  "normalize an existing library" pass. Always dry-run preview first.
- **Cleanup** - remove empty folders deepest-first with a min-age gate, name exclusions, symlink
  skip, and optional prune-empty-parents. Local or remote; once or watch.

## Secrets and settings (hard requirement: no leaked passwords)

- **Non-secret** config (SSH host/port/user, remote paths, Plex URL, section maps, staging path,
  naming/cleanup defaults) -> `%APPDATA%\PlexTool\settings.json` via `SettingsStore`. Safe to
  read in a text editor.
- **Secrets** (SSH password, SSH key passphrase, Plex token) -> `%APPDATA%\PlexTool\secrets.dat`
  via `SecretStore`, encrypted with **Windows DPAPI, CurrentUser scope** plus fixed entropy. The
  blob is decryptable only by the same Windows user on the same machine; a copied/synced/leaked
  file is useless. Secrets never appear in `settings.json`, logs, exception text, or the UI
  (redacted as dots).
- Everything is configured in the Settings tab - no host, IP, path, or credential is ever
  hardcoded or committed. `.gitignore` plus the `%APPDATA%` location keep secrets out of git.
- Prefer key-based SSH auth (recommended default) so ideally no password is stored at all.
- SSH host key is remembered on first connect (trust-on-first-use); a later mismatch is refused
  as a possible MITM.

## Conventions

- **MVVM**: ViewModels never reference Views. Use CommunityToolkit.Mvvm `[ObservableProperty]`
  and `[RelayCommand]` source generators rather than hand-rolling `INotifyPropertyChanged`.
- **ASCII punctuation only** in all UI text, code, comments, and docs. No em-dashes, en-dashes,
  or unicode ellipsis - write "-" and "...". The user notices AI-artifact punctuation.
- **Preview before apply**: no operation mutates the filesystem (local or remote) without first
  showing the computed plan for confirmation.
- **In-place renames**: never copy-then-delete to rename; it makes Plex treat the file as new
  and can drop watched state. Use SFTP `RenameFile` / local `File.Move`.
- **Threading**: SFTP/SSH and long transfers run off the UI thread; marshal progress and state
  back via `Dispatcher.UIThread.Post(...)` before raising `PropertyChanged`.
- **Naming**: ViewModels end in `ViewModel`. Views end in `Window` or `View`.

## Releasing / versioning (identical to Klakr)

- Version lives in `Directory.Build.props` as a single `<VersionPrefix>`.
- `./Scripts/bump-version.sh` (default Patch; pass `Minor`/`Major`) bumps it. Do this whenever a
  feature or behavior change is complete and will ship, ideally in the same commit. Do NOT bump
  for docs, comments, memory updates, or refactors with no user-visible effect.
- Push to `main`: `.github/workflows/release.yml` reads the version, skips if the `vX.Y.Z`
  release already exists, else tests + publishes win-x64 self-contained + `vpk pack --packId
  PlexTool --mainExe PlexTool.App.exe` + creates the GitHub release. Plain pushes without a bump
  are no-ops. CI never commits.
- The repo MUST be public for the in-app update check (unauthenticated `GithubSource`) to work.
- The user handles all git adds/commits. The `origin` remote is
  `https://github.com/matthewjenner/PlexTool.git`.

## Gotchas

- **FluentAssertions pinned to 7.x**: version 8.x moved to a paid commercial license. 7.x is the
  last Apache-2.0 release. Do not bump to 8.x without the user accepting that license.
- **DPAPI is Windows-only**: `SecretStore.Save` is guarded with `[SupportedOSPlatform("windows")]`
  and the app targets Windows. Do not assume it works cross-platform.
- **Plex rename safety**: renaming a file is metadata-safe *only* if the new name still resolves
  to the same movie/episode (Plex ties watched state and artwork to the matched item, not the
  path). Rename in place, and test a small batch before a full library normalize.
- **Remote cleanup skips symlinks**, not Windows reparse points - the server is Linux. The min-age
  gate, name exclusions, and deepest-first ordering from the script are preserved.
- **Plex scan**: prefer a partial-path scan (`?path=...`) of just the imported folder over a full
  section refresh, so it is fast.
- **Icon**: `Assets\icon.ico` and the `<ApplicationIcon>` reference are deferred to P6 - the
  csproj currently has no app icon on purpose.

## Common tasks

- **Add an app-wide setting**: add a property to `AppSettings` (non-secret) or `Secrets`
  (sensitive), surface it in the Settings view model, persist via `AppHost.UpdateSettings` /
  `AppHost.UpdateSecrets`.
- **Add a media operation backend method**: extend `IMediaFileSystem` in Core, implement in both
  `LocalMediaFileSystem` and `SftpMediaFileSystem`, and in the in-memory test fake.
- **Cut a release**: `./Scripts/bump-version.sh`, commit, push to `main`.

## What NOT to do

- Don't put secrets in `settings.json`, logs, or exception messages. Ever.
- Don't hardcode the server IP, paths, username, or any credential.
- Don't pull UI/SSH/filesystem dependencies into `PlexTool.Core`.
- Don't rename by copy-then-delete (breaks Plex watched state).
- Don't mutate the filesystem without a preview/confirm step.
- Don't bump FluentAssertions to 8.x (paid license).
