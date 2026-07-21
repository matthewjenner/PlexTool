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
Src/PlexTool.Core/      Pure logic, no UI/IO/platform deps. Fully unit-tested.
   MediaEntry, IMediaFileSystem, MediaKind
   Paths/PosixPath              Normalize/Combine/TranslatePrefix/IsSafeSegment (remote paths)
   Naming/                      NamingScheme, NameSanitizer, EpisodeParser, PlexNamer, SubtitleName
   Planning/                    RenamePlanner, ImportPlanner
   Cleanup/EmptyFolderScanner
Src/PlexTool.App/       Avalonia app + all I/O.
   Backends/                    LocalMediaFileSystem, SftpMediaFileSystem, SshService
   Services/                    AppHost, AppPaths, AppSettings, SettingsStore, Secrets, SecretStore,
                                PlexClient, UpdateService, AppVersion
   ViewModels/                  MainWindow + one per tab (Settings/Rename/Import/Cleanup/Tools),
                                OperationTarget, ViewModelBase
   Views/                       MainWindow + one View per tab
Tests/PlexTool.Core.Tests/   xUnit (v2) for Core, against an in-memory IMediaFileSystem fake.
Tests/PlexTool.App.Tests/    Headless Avalonia (xunit v3) - SettingsLayoutTests (footer/scroll regression).
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

## The tabs (Import | Rename | Cleanup | Tools | Settings)

Every operation follows the same shape: **compute a plan -> show it as a reviewable list ->
user confirms -> apply**. That is the GUI form of the scripts' universal `-WhatIf`/`-DryRun`.
Nothing overwrites: collisions are shown and skipped, and `IMediaFileSystem.Move` throws rather
than clobber.

- **Import** (`ImportViewModel`, `ImportPlanner`) - server-only: move a staged item from the
  storage box's staging folder into the library. Builds the target folders, renames each file to
  Plex form, moves them as an instant **server-side rename** (staging and library share the
  filesystem - no upload), optionally removes the emptied source, then triggers a path-scoped Plex
  scan (`ToPlexPath`-translated). **Import absorbed the old "New Structure" tab** - it builds the
  tree as part of importing; standalone empty-folder creation was dropped.
- **Rename / Normalize** (`RenameViewModel`, `RenamePlanner`) - scan a Movies/Shows library and
  rename media in place toward Plex form (`Movie (Year).ext`, `Show - S01E01.ext`). In-place only
  (SFTP `RenameFile` / local `File.Move`, never copy-then-delete) so Plex keeps watched state.
  Per-row select; the bulk normalize pass. Server or Local.
- **Cleanup** (`CleanupViewModel`, `EmptyFolderScanner`) - remove empty folders deepest-first with a
  min-age gate, wildcard name exclusions, symlink skip, and optional prune-empty-parents. Empty
  folders only, never files. Server (Movies/Shows/Staging/Custom) or Local.
- **Tools** (`ToolsViewModel`) - quick one-off actions: manual Plex scans (Ctrl+M / Ctrl+T via
  window `KeyBindings`), Test SSH, Test Plex, Open config folder. This is the home for utility
  actions - see "Add a Tools utility" below.
- **Settings** (`SettingsViewModel`) - SSH, split/unified topology + path mapping, remote paths,
  staging, Plex + library mapping, naming, cleanup defaults, and the encrypted secrets.

**Deferred (see workplan):** watch/monitor modes (auto-import, continuous cleanup) and
local->remote upload import (staging is remote, so import is a rename today, not an upload).

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
  or unicode ellipsis - write "-" and "...". Text that reads as machine-generated is a defect here.
- **Preview before apply**: no operation mutates the filesystem (local or remote) without first
  showing the computed plan for confirmation.
- **In-place renames**: never copy-then-delete to rename; it makes Plex treat the file as new
  and can drop watched state. Use SFTP `RenameFile` / local `File.Move`.
- **Threading**: SFTP/SSH and long transfers run off the UI thread; marshal progress and state
  back via `Dispatcher.UIThread.Post(...)` before raising `PropertyChanged`.
- **Naming**: ViewModels end in `ViewModel`. Views end in `Window` or `View`.

## Dependency policy

- **Stay on the latest stable version**, and **verify it from the registry before pinning** - do not
  rely on recall. Check `https://api.nuget.org/v3-flatcontainer/<package-id-lowercase>/index.json`
  (or nuget.org) and take the highest non-preview/rc/beta version. The same applies to GitHub
  Actions `uses:` pins - confirm the current major from the action's own `action.yml`.
- **Two standing exceptions** (see Gotchas for why): the **Avalonia** family stays on the latest
  **12.x** and every Avalonia package moves together; **FluentAssertions** stays on **7.x**.
- Record the versions chosen (and the date checked) in `Docs/TECHARCH.md` under Dependencies.

## Keeping the workplan current

`Docs/workplan.md` is the living build tracker - current phase, what is done, what is next, the
decisions log, and the deferred/backlog list. It is a repo file, not scratch notes: at each feature
or phase boundary, tick off the done items, refresh the **Current state** block, and append anything
notable to **Decisions log** / **Deferred** / **Backlog**. A decision that was deliberately made and
might otherwise be re-litigated (or re-discovered the hard way) belongs there.

## Releasing / versioning (identical to Klakr)

- Version lives in `Directory.Build.props` as a single `<VersionPrefix>`.
- `./Scripts/bump-version.sh` (default Patch; pass `Minor`/`Major`) bumps it. Do this whenever a
  feature or behavior change is complete and will ship, ideally in the same commit. Do NOT bump
  for docs, comments, or refactors with no user-visible effect - a push without a bump is a
  deliberate no-op (CI skips when the release already exists), so docs commits do not clutter the
  release list.
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
- **Plex scan**: `PlexClient.RefreshAsync` does a partial-path scan (`?path=...`) of just the
  imported folder when given a path, else a full-section refresh. Import passes
  `settings.ToPlexPath(scanPath)` so the split-topology mount prefix is used.
- **ScrollViewer padding**: never put `Padding` on a `ScrollViewer` - it is excluded from the scroll
  extent, so the bottom padding's worth of content becomes unreachable (this caused the Settings
  footer overlap). Put the padding/margin on the inner content instead. `SettingsLayoutTests` guards this.
- **Layout tests select the Settings tab by header, not index** - adding/removing tabs must not
  break them.
- **New tab = 5 wiring points**: a `ViewModel`, a `View` (+`.axaml.cs`), a property on
  `MainWindowViewModel`, and a `<TabItem>` in `MainWindow.axaml`.

## Input safety and test rigor (required)

Paths and names are adversarial input - a media title or a configured path can contain whitespace,
trailing slashes, or traversal/injection attempts. Rules:

- **Build remote paths only from safe segments.** `PosixPath.IsSafeSegment` rejects "."/".."
  traversal, embedded '/', control chars (incl. NUL/newline), and surrounding whitespace. The P2
  naming sanitizer must produce segments that pass it.
- **Normalize before comparing/joining.** Use `PosixPath.Normalize` / `Combine` / `TranslatePrefix`
  (Core), never raw string concatenation or `StartsWith`. Prefix matching is segment-boundary safe
  (so `/srv/plex-media` never matches `/srv/plex-media-extra`) and trailing-slash tolerant.
- **No shell string-building with user paths.** Prefer SFTP protocol operations (SftpClient
  CreateDirectory/RenameFile/Delete) which take path arguments directly - no shell parsing, so no
  command injection. If a shell command is ever unavoidable (e.g. chown), single-quote every path
  argument and reject names failing `IsSafeSegment`. This matters most in P4 (import/chown) and P5
  (cleanup).
- **Tests must cover whitespace, trailing slashes, and injection/traversal** for any path or name
  logic - see `PosixPathTests` as the template. New naming/planning logic gets the same treatment.

## Common tasks

- **Add an app-wide setting**: add a property to `AppSettings` (non-secret) or `Secrets`
  (sensitive), surface it in the Settings view model, persist via `AppHost.UpdateSettings` /
  `AppHost.UpdateSecrets`.
- **Add a media operation backend method**: extend `IMediaFileSystem` in Core, implement in both
  `LocalMediaFileSystem` and `SftpMediaFileSystem`, and in the in-memory test fake.
- **Add a Tools utility (quick action)**: add a `[RelayCommand]` method to `ToolsViewModel` (set
  `Result` with the outcome), a button in `ToolsView.axaml`, and optionally a `<KeyBinding>` in
  `MainWindow.axaml`'s `<Window.KeyBindings>`. This is the intended home for keypress utilities.
- **Cut a release**: `./Scripts/bump-version.sh`, commit, push to `main`.

## What NOT to do

- Don't put secrets in `settings.json`, logs, or exception messages. Ever.
- Don't hardcode the server IP, paths, username, or any credential.
- Don't pull UI/SSH/filesystem dependencies into `PlexTool.Core`.
- Don't rename by copy-then-delete (breaks Plex watched state).
- Don't mutate the filesystem without a preview/confirm step.
- Don't let `Move` overwrite - collisions are surfaced and skipped, never clobbered.
- Don't put `Padding` on a `ScrollViewer` (see gotchas).
- Don't bump FluentAssertions to 8.x (paid license).
