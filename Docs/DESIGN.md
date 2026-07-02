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
Point at a staged item, classify it (Movie with year, or Show with season/episode - auto-parsed
from the existing name where possible), and PlexTool: creates the correct remote folder, uploads
with a real progress bar, verifies the size, deletes the local source (move), optionally fixes
ownership/permissions, then asks Plex to scan just that path. A **watch mode** watches the staging
folder and imports new arrivals automatically once they stop changing.

### New Structure
Create `Movie (Year)` or `Show/Season NN` scaffolding, on the server or locally, without moving
any files. Useful for pre-creating a show's seasons.

### Rename / Normalize
Rename media in a chosen root to the Plex form. Two uses: tidy a freshly added item, or bulk
**normalize an existing library** to the recommended naming. Always a dry-run preview first;
applies as in-place renames.

### Cleanup
Sweep empty folders (local staging or a server library root) deepest-first, with the script's
safety knobs: min-age, name exclusions, symlink skip, optional prune-empty-parents. Once or a
continuous watch.

### Settings
The single place to configure the SSH connection (host/port/user, key or password, Test button),
remote Movies/Shows base paths, the Plex URL + token (Test, list sections, map which is Movies and
which is Shows), the local staging path, the naming scheme, and cleanup defaults. Secrets are
entered here and stored encrypted.

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
