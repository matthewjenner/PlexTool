# PlexTool - Design

## Purpose

A single Windows app for the whole "I have new media, get it onto the Plex server correctly"
workflow, plus ongoing library tidying. It replaces four PowerShell scripts that were quick to
write but awkward to run: they take long typed UNC paths and flags, have no shared preview, and
force elevation for a robocopy backup mode that this app does not need.

The Plex server is an Ubuntu box. PlexTool runs on the Windows desktop and drives the server
remotely over one SSH/SFTP connection, so there is no Samba dependency, no drive mapping, and no
UAC prompt.

## Origin: the four scripts and what carries over

| Script | Operation | Essence preserved |
|--------|-----------|-------------------|
| `New-MediaFolderStructure.ps1` | New Structure | `Movie (Year)` folder; `Show/Season NN` folders; name sanitization; dry-run |
| `Rename-MediaFiles.ps1` | Rename / Normalize | Movies -> `<Folder>.ext`; Shows -> `<Show> - S01E01.ext`; parse `s01e01`/`1x01`; video + subtitles; collision skip; dry-run |
| `Invoke-RobocopyMove.ps1` | Import (move) | Move semantics (source removed after verified transfer); live progress log; exit handling. Robocopy itself is dropped in favor of SFTP. |
| `Clean-EmptyFolders.ps1` | Cleanup | Deepest-first empty-folder removal; min-age gate; name exclusions; skip symlinks; prune empty parents; list-only preview; once/monitor modes |

## Product principles

1. **Preview before apply.** Every operation computes a plan and shows it (old -> new, or "would
   create", or "would remove") before touching anything. This is the universal `-WhatIf` the
   scripts each had, made consistent and visual.
2. **Safe by construction.** Renames happen in place so Plex keeps watched state and artwork.
   Collisions are detected and skipped, not overwritten. Cleanup honors a min-age gate so an
   in-progress transfer is never swept.
3. **No leaked secrets.** All credentials are user-entered, DPAPI-encrypted at rest, redacted in
   the UI, and never logged or committed. See TECHARCH.
4. **Configured, not hardcoded.** The server address, paths, Plex URL, and naming rules all live
   in settings. Nothing about one person's setup is baked into the binary.

## The tabs

### Import (the main flow)
Load the staging folder, pick a staged item, classify it (Movie with year, or Show - episodes
auto-parsed from filenames), and PlexTool builds the correct target folders, renames each file to
Plex form, moves them into the library as an **instant server-side rename** (staging and library
share the filesystem, so no upload), optionally removes the emptied source folder, then asks Plex
to scan just that path. Preview shows every move first. **Import also does what the old "New
Structure" step did** - it creates the folder tree as part of importing, which is why standalone
folder creation is not a separate tab.

### Rename / Normalize
Rename media in a chosen root to the Plex form. Two uses: tidy a freshly added item, or bulk
**normalize an existing library** to the recommended naming. Always a preview first, with a
per-file checkbox to choose what to apply; applies as in-place renames. Server or local.

### Cleanup
Sweep empty folders (a server library root, staging, a custom path, or a local folder) deepest-first,
with the script's safety knobs: min-age, wildcard name exclusions, symlink skip, optional
prune-empty-parents. Empty folders only, never files. Preview first.

### Tools
Quick one-off actions: manual Plex scans (Movies via Ctrl+M, Shows via Ctrl+T), test the SSH and
Plex connections, and open the config folder. This is the home for keypress utilities.

### Settings
The single place to configure the SSH connection (host/port/user, key or password, Test button),
the split/unified topology and the storage->mount path mapping, remote Movies/Shows/staging paths,
the Plex URL + token (Test, list sections, map Movies/Shows), the naming scheme, and cleanup
defaults. Secrets are entered here and stored DPAPI-encrypted.

## Naming

Default is the Plex recommended form:
- Movie: `Movie Name (Year)/Movie Name (Year).ext`
- Episode: `Show Name/Season NN/Show Name - S01E01.ext`
- Subtitles keep their language suffix: `Show Name - S01E01.en.srt`

The season/episode parser accepts `s01e01`, `S01E01`, and `1x01`, and recognizes already-normalized
names so re-running is a no-op. A settings toggle can switch to the legacy script form
(`Show Name s01e01`) if ever needed.

## Non-goals (for now)

- Full Plex library browsing / duplicate detection (only the post-import scan trigger is in scope).
- Downloading or sourcing media. PlexTool starts from media you already have staged.
- Cross-platform desktop support. It is a Windows app talking to a Linux server.
