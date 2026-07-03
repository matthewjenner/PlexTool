# PlexTool - Workplan

Living build tracker. Functions like a todo list and micro plan. Update it at each phase
boundary: check off done items, refresh **Current state**, and append to the **Decisions log** /
**Known edges**. Keep memories current at the same time. ASCII punctuation only.

## Current state

- **Phase**: P3 code-complete (Rename / Normalize); P4 (Import) is next.
  Server-side write access was set up (matt joined the `plex` group + setgid on the roots), and
  New Structure Create is verified working on the real server.

### P3 - Rename / Normalize  [CODE-COMPLETE]
- Core: `Naming/SubtitleName` (subtitle extensions + language/flag suffix peeling, curated code set
  so "Two"/"The" are not mistaken for languages), `Planning/RenamePlanner` (movies -> folder-name.ext,
  shows -> Show - S01E01.ext via PlexNamer/EpisodeParser). Statuses: WillRename / AlreadyCorrect /
  Collision (never overwrites) / NoEpisodePattern. Pure + tested (112 Core tests total).
- App: `RenameViewModel` + `RenameRowViewModel` + `RenameView` in the Rename / Normalize tab.
  Location (Server/Local), Library (Movies/Shows), Preview lists every proposed change with per-row
  checkboxes (only actionable rows selectable), Select all/none, Apply renames the checked ones
  in place and re-scans. Nothing writes until Apply.
- Clobber safety hardened at the primitive level in P2 follow-up: `IMediaFileSystem.Move` now throws
  on an existing destination (SFTP, local, and in-memory fake), so renames/moves can never silently
  overwrite; the planner surfaces collisions and skips them.

### P2 - New Structure  [CODE-COMPLETE]
- Core naming toolkit (all pure, tested): `Naming/NameSanitizer` (safe segments, injection-proof),
  `Naming/EpisodeParser` (s01e01 / 1x01 / normalized), `Naming/PlexNamer` (movie/show/season/episode
  + subtitle names, scheme-aware), `Planning/FolderStructurePlanner`. `NamingScheme` moved to Core;
  `MediaKind` added to Core.
- Tests: 96 Core tests total (adds NameSanitizer, EpisodeParser, PlexNamer, FolderStructurePlanner
  suites + an `InMemoryMediaFileSystem` fake in TestSupport).
- App: `NewStructureViewModel` + `NewStructureView` wired into the New Structure tab. Location
  (Server via SSH / Local), Movie(Name+Year) or Show(Name+Seasons), Preview (shows exists vs will-
  create), Create (makes only the missing folders). Preview-before-apply; nothing writes until Create.

### Older state
- **Build status**: `dotnet build` clean (0 warnings), `dotnet run` launches with a working
  Settings tab. No Core tests yet (first ones land in P2 with the naming logic).
- **What runs today**: the tabbed window with a **functional, annotated Settings tab** - SSH
  connection (host/port/user, key or password, host-key trust-on-first-use, Test), remote
  Movies/Shows paths, split/unified topology with prefix mapping, staging path (remote), Plex URL +
  token with "Test / load libraries" and Movies/Shows mapping, naming scheme, cleanup defaults.
  Tooltips on every field, a copyable "get your Plex token" command, a sticky (own-Grid-row) Save
  footer, and auto-save on a successful test. Save persists non-secret settings to `settings.json`
  and secrets to the DPAPI-encrypted `secrets.dat`. The update banner still works.
- **Tests**: `dotnet test` green - 53 `PosixPathTests` (whitespace, trailing slashes, prefix-boundary,
  traversal/injection) plus 4 `SettingsLayoutTests` (headless UI, one per window size). `Core/Paths/PosixPath.cs`
  is the pure, tested path layer; `AppSettings.ToPlexPath` delegates to it (fixed a prefix-boundary bug
  where `/srv/plex-media` matched `/srv/plex-media-extra`).
- **Settings footer overlap - fixed.** The Save footer hid the last field by a constant ~44.5px at every
  window size. Root cause: `ScrollViewer.Padding` is NOT counted in the scrollable extent, so the bottom
  padding worth of content was unreachable. Fix: moved padding off the ScrollViewer onto the inner content
  (`Margin` on the StackPanel). `SettingsLayoutTests` now scrolls to the end and asserts the last field is
  visible above the footer, at 4 window sizes - a permanent regression guard for this whole class of bug.
- **Next**: P2 - New Structure + the Core naming/parser logic (sanitizer producing IsSafeSegment-safe
  names) and its unit tests.

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

### P1 - Settings + connections  [CODE-COMPLETE]
- [x] Settings tab: SSH (host/port/user, key path or password, auth toggle), remote Movies/Shows
      paths, Plex URL + token, local staging path, naming scheme, cleanup defaults
- [x] Secrets entered here, redacted (dots, "Show secrets" toggle), stored via SecretStore
- [x] `IMediaFileSystem` (Core) + `SftpMediaFileSystem` + `LocalMediaFileSystem` (App)
- [x] `SshService` "Test connection" with host-key trust-on-first-use (SHA-256 fingerprint)
- [x] `PlexClient`: "Test / load libraries" + list sections + map Movies/Shows
- [ ] Live smoke (needs the user's server): Test SSH connects; Test Plex lists libraries
      (build + UI launch verified; live connection is the user's to confirm)

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
- **Split vs unified topology (configurable):** SSH/SFTP targets the STORAGE box (where files
  physically live); the Plex API (HTTP + token) targets the PLEX box. In the user's setup these are
  two Ubuntu boxes: a file server exposing the media (native path e.g. `/srv/plex-media`) and a
  Plex box that mounts it (e.g. `/mnt/media`). A "Plex runs on a separate box" toggle
  (`PlexStorageIsSeparate`) plus a storage-prefix -> plex-mount-prefix pair drives
  `AppSettings.ToPlexPath()`, so path-scoped Plex scans use the mount path while writes use the
  native path. Unified setups turn the toggle off (no translation). Key correction that drove
  this: Plex library scans are the HTTP API + token, NOT an SSH command - so you never SSH into
  the Plex box, and it is always one SSH credential (storage) + one Plex token, never two SSH creds.
  Writing to the file server natively (one hop) beats writing through the Plex box's CIFS mount
  (double hop + permission quirks).
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

- **Staging is REMOTE, on the storage box** (e.g. `/srv/plex-media/downloads`), not a local
  Windows folder - the user's downloads land on the file server next to movies/shows. Consequence:
  import is a fast server-side move (SFTP rename within one filesystem), NOT a network upload, so
  the P4 "upload with progress" model only applies if a source is ever off-box. `AppSettings.StagingPath`
  (was `LocalStagingPath`) holds this remote path.
- **Settings persistence UX** (P1 fix): Save was a single button buried at the bottom of a long
  scroll, so users tested a connection and never saved -> blank after restart. Fixed by (a) a sticky
  docked Save footer always visible, and (b) auto-save after a successful Test connection / Test
  load-libraries. Also fixed a bug where saving before (re)loading Plex libraries could wipe the
  stored section mapping - BuildConfig now falls back to the previously saved section ids.

## Backlog (next phase)

- **Unify xunit versions across all test projects.** `PlexTool.App.Tests` uses xunit **v3** (required
  by Avalonia.Headless.XUnit 12.0.5), while `PlexTool.Core.Tests` is still xunit **v2** (2.9.3). The
  user does not want version mixing - migrate `PlexTool.Core.Tests` to xunit v3 next phase so the
  whole solution is on one version.

## Known edges / TODO

- Plex re-match risk on normalize for manually-matched items - test a small batch first.
- SSH.NET clients are not thread-safe - serialize access in ConnectionManager.
- Host-key TOFU: decide the UX for a fingerprint-changed refusal (P1).
- Large uploads: resume/retry behavior on a dropped SFTP connection (P4 polish).
- Watch mode "file settled" detection needs a debounce/min-age like the cleanup gate (P4).
