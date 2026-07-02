# PlexTool - Workplan

Living build tracker. Functions like a todo list and micro plan. Update it at each phase
boundary: check off done items, refresh **Current state**, and append to the **Decisions log** /
**Known edges**. Keep memories current at the same time. ASCII punctuation only.

## Current state

- **Phase**: P0 complete; P1 is next.
- **Build status**: `dotnet build` clean (0 warnings), `dotnet run` launches the shell window.
  No tests yet (Core has no logic yet).
- **What runs today**: a tabbed main window (Import / New Structure / Rename / Cleanup / Settings
  as placeholders), a working update banner wired to Velopack + the GitHub repo, and settings +
  DPAPI secret persistence plumbed through `AppHost` (no Settings UI yet).
- **Next**: P1 - Settings tab and the SSH + Plex connections.

## Phases

### P0 - Scaffold + infra + update path  [DONE]
- [x] git init + `origin` = matthewjenner/PlexTool
- [x] Solution + 3 projects (Core, App, Core.Tests) mirroring Klakr
- [x] Directory.Build.props (single VersionPrefix, starts at 0.1.0), .gitignore
- [x] Scripts/run.sh + bump-version.sh
- [x] .github/workflows/release.yml (Velopack, win-x64 self-contained, packId PlexTool)
- [x] Latest package versions verified against the NuGet registry
- [x] Program.cs Velopack bootstrap, app.manifest (asInvoker - no elevation)
- [x] App.axaml + tabbed MainWindow shell + update banner (Install/Skip/Later)
- [x] Services: AppPaths, AppSettings, SettingsStore, Secrets, SecretStore (DPAPI), UpdateService,
      AppVersion, AppHost
- [x] CLAUDE.md, Docs (DESIGN, TECHARCH, workplan), memories
- [x] Smoke test: builds, runs, no crash

### P1 - Settings + connections
- [ ] Settings tab: SSH (host/port/user, key path or password, auth toggle), remote Movies/Shows
      paths, Plex URL + token, local staging path, naming scheme, cleanup defaults
- [ ] Secrets entered here, redacted (dots), stored via SecretStore
- [ ] `ConnectionManager` + `SftpMediaFileSystem` + `LocalMediaFileSystem` (implement IMediaFileSystem)
- [ ] SSH "Test connection" with host-key trust-on-first-use
- [ ] `PlexClient`: "Test" + list sections + map Movies/Shows
- [ ] Smoke: connect to the server, list a directory; hit Plex, list libraries

### P2 - New Structure
- [ ] Core: NameSanitizer, EpisodeParser, PlexNamer (+ tests)
- [ ] FolderStructurePlan + StructureCreator (local + remote)
- [ ] New Structure tab (Movie/Show, name, year/seasons, preview, create)

### P3 - Rename / Normalize
- [ ] RenamePlan (old -> new, collision detection, already-normalized no-op) + tests
- [ ] RenameExecutor (in-place; local Move + SFTP RenameFile)
- [ ] Rename tab with dry-run preview and select-to-apply; bulk normalize-existing mode

### P4 - Import
- [ ] ImportPlan + ImportRunner (create dirs, SFTP upload w/ progress, verify, delete source)
- [ ] Plex partial-path scan trigger after import
- [ ] Watch mode: FileSystemWatcher on staging, auto-import settled arrivals, live progress

### P5 - Cleanup
- [ ] EmptyFolderScanner in Core (deepest-first, min-age, exclusions, symlink skip) + tests
- [ ] CleanupRunner + prune-empty-parents; Cleanup tab; once + watch modes

### P6 - Polish
- [x] App icon (Assets\icon.ico, multi-res 16-256) + <ApplicationIcon> + Window Icon (done early)
- [ ] README for users
- [ ] Live end-to-end verify against a throwaway structure on the real server
- [ ] Error surfacing / retries polish

## Decisions log

- **Destination is Ubuntu, not Windows** -> dropped robocopy; transfer is SSH/SFTP via SSH.NET.
  Side effect: no elevation / UAC needed at all (asInvoker manifest).
- **Reach the server over SSH/SFTP** (not the mounted SMB share) for robustness and to run remote
  structure/rename/cleanup/permissions over one connection.
- **Secrets**: non-secret settings in `settings.json`; secrets (SSH password/passphrase, Plex
  token) in a separate DPAPI-encrypted `secrets.dat` (CurrentUser). Prefer key-based SSH auth.
  Hard requirement from the user: no leaked passwords.
- **Naming**: default to Plex recommended form (`Show - S01E01`, `Movie (Year)`), with a legacy
  toggle. Include an in-place bulk "normalize existing library" pass.
- **Renames are in-place** (never copy+delete) so Plex keeps watched state / metadata.
- **Plex API in scope, minimally**: post-import library scan trigger + section listing only.
- **Versions**: latest as of 2026-07-02, except Avalonia stays on 12.x (no stable 13) and
  FluentAssertions stays on 7.x (8.x is a paid license).
- **Core/App split**: `IMediaFileSystem` in Core, implementations in App - the Klakr purity rule.

## Known edges / TODO

- Plex re-match risk on normalize for manually-matched items - test a small batch first.
- SSH.NET clients are not thread-safe - serialize access in ConnectionManager.
- Host-key TOFU: decide the UX for a fingerprint-changed refusal (P1).
- Large uploads: resume/retry behavior on a dropped SFTP connection (P4 polish).
- Watch mode "file settled" detection needs a debounce/min-age like the cleanup gate (P4).
