# PlexTool - Technical Architecture

## Stack

- **Language**: C# on .NET 10
- **UI**: Avalonia 12 (pinned to the 12.x family; no stable 13 yet)
- **MVVM**: CommunityToolkit.Mvvm (`[ObservableProperty]`, `[RelayCommand]` source generators)
- **Remote I/O**: SSH.NET - SFTP for file transfer with byte progress, SSH for remote shell
  commands (mkdir -p, rename, rmdir, chown) against the Ubuntu server
- **Secrets at rest**: System.Security.Cryptography.ProtectedData (Windows DPAPI)
- **Updates / packaging**: Velopack (`VelopackApp` bootstrap in-process; `vpk` CLI in CI)
- **Serialization**: System.Text.Json (settings + secrets)
- **Tests**: xUnit + FluentAssertions (7.x - last Apache-2.0 line)

## Project structure

```
PlexTool/
  Src/
    PlexTool.Core/                 # Pure logic, no UI/IO/platform deps
      MediaEntry.cs / MediaKind.cs / IMediaFileSystem.cs
      Paths/PosixPath.cs           # Normalize / Combine / TranslatePrefix / IsSafeSegment / ContainsTraversal
      Naming/                      # NamingScheme, NameSanitizer, EpisodeParser, PlexNamer, SubtitleName
      Planning/                    # RenamePlanner, ImportPlanner
      Cleanup/EmptyFolderScanner.cs
    PlexTool.App/                  # Avalonia + all I/O
      Backends/                    # LocalMediaFileSystem, SftpMediaFileSystem, SshService
      Services/                    # AppHost, AppPaths, AppSettings, SettingsStore, Secrets, SecretStore,
                                   #   PlexClient, UpdateService, AppVersion
      ViewModels/                  # MainWindowViewModel + one per tab (Settings/Rename/Import/Cleanup/Tools),
                                   #   OperationTarget, ViewModelBase
      Views/                       # MainWindow + one View per tab
      App.axaml / Program.cs / app.manifest
  Tests/
    PlexTool.Core.Tests/           # xUnit v2; InMemoryMediaFileSystem fake
    PlexTool.App.Tests/            # xUnit v3; headless Avalonia layout tests
```

Execution lives in the per-tab ViewModels (each connects via `SshService.ConnectAsync` or
`LocalMediaFileSystem`, runs the Core planner off the UI thread, then applies the plan), rather than
in separate "runner" classes - the plan objects are the seam, so no extra layer was needed.

## The core abstraction: IMediaFileSystem

`PlexTool.Core` never performs I/O. It defines one interface that both a local and a remote
backend satisfy:

```csharp
public interface IMediaFileSystem
{
    string Description { get; }
    char DirectorySeparator { get; }
    string Combine(string basePath, string child);
    bool DirectoryExists(string path);
    bool FileExists(string path);
    IReadOnlyList<MediaEntry> List(string path);        // entries carry name/kind/size/mtime/IsSymbolicLink
    void CreateDirectory(string path);                  // creates parents; idempotent
    void Move(string source, string destination);       // in-place; never copy+delete, never overwrite (throws)
    void Delete(string path);
    Stream OpenRead(string path);
    Stream OpenWrite(string path);
}
```

- `LocalMediaFileSystem` wraps `System.IO`.
- `SftpMediaFileSystem` wraps an SSH.NET `SftpClient`; `Move` uses `RenameFile`, uploads report
  progress via the `UploadFile` callback.
- `InMemoryMediaFileSystem` (test project) lets every planner and the cleanup scanner be unit
  tested with no disk and no server.

Planners (`RenamePlan`, `FolderStructurePlan`, `EmptyFolderScanner`) take an `IMediaFileSystem`,
read through it, and return a list of proposed operations. The App shows that list, then asks the
same backend to execute the confirmed subset. This keeps parsing/planning logic 100% testable and
identical whether the target is local staging or the remote library.

## Data flow (Import, the representative case)

```
User picks a staged item + classifies it
  -> PlexNamer computes remote folder + standardized file name
    -> ImportPlan built (create dirs, upload files, delete-source-after, scan path)
      -> UI shows the plan; user confirms
        -> ImportRunner (off UI thread):
             SftpMediaFileSystem.CreateDirectory(remote season/movie dir)
             SftpClient.UploadFile(local -> remote, progress -> Dispatcher.UIThread.Post)
             verify size
             LocalMediaFileSystem.Delete(source)         # move semantics
             PlexClient.RefreshPath(sectionId, remotePath)
          -> progress + result marshaled back to the ViewModel
```

## Threading

- **UI thread**: Avalonia; owns Views and ViewModels.
- **Work**: SFTP connect, transfers, remote commands, and scans run on background tasks. Progress
  (`IProgress<T>`) and completion are marshaled with `Dispatcher.UIThread.Post(...)` before any
  `PropertyChanged`.
- SSH.NET clients are **not thread-safe**; each operation connects, works, and disposes, so no
  session is shared across threads.

## Topology: which box does what, and why

Media storage and Plex may be the same machine or two machines. PlexTool models this explicitly:

- **SSH/SFTP always targets the STORAGE box** - the machine whose filesystem actually holds the
  media. All file work (mkdir, rename, move, delete) happens there, natively.
- **The Plex API always targets the PLEX box** over **HTTP + `X-Plex-Token`**. Library scans are a
  web request, *not* an SSH command, so PlexTool never needs a shell on the Plex machine.

Two consequences worth stating, because they are easy to get wrong:

1. It is always **one SSH credential (storage) + one Plex token** - never two SSH credentials. The
   token is an HTTP header, not an SSH login.
2. **Write to the storage box directly, not through Plex's mount.** In a split setup the Plex box
   mounts the storage over SMB/NFS; writing through that mount would send every byte across the
   network twice (client -> Plex -> storage) and inherit the mount's ownership quirks. Writing
   natively on the storage box is one hop with real permissions.

In a **split** setup the same media appears at a different path on each box (e.g. `/srv/plex-media`
on storage, `/mnt/media` on Plex). `AppSettings.PlexStorageIsSeparate` plus a storage-prefix /
plex-mount-prefix pair drives `AppSettings.ToPlexPath()`, which rebases a written path into the path
Plex knows for path-scoped scans. In a **unified** setup the toggle is off and no translation occurs.
The prefix swap is segment-boundary safe via `PosixPath.TranslatePrefix` (so `/srv/plex-media` never
matches `/srv/plex-media-extra`).

## Persistence and secrets

Two files under `%APPDATA%\PlexTool` (per-user, ACL'd, outside the repo):

- `settings.json` - non-secret `AppSettings`, camelCase JSON, enums as strings, corrupt-file
  falls back to defaults. Hand-editable.
- `secrets.dat` - `Secrets` serialized to JSON, then DPAPI-encrypted (`CurrentUser` scope + fixed
  entropy) and written as bytes. Decryptable only by the same Windows user on the same machine.

`SettingsStore` and `SecretStore` are the only readers/writers. `AppHost` loads both at startup,
holds the decrypted secrets in memory for the app's lifetime, and exposes `UpdateSettings` /
`UpdateSecrets`. No other layer opens these files, logs their contents, or embeds them in errors.

### SSH host-key trust
On first successful connect the server's host-key fingerprint is saved to
`AppSettings.SshHostKeyFingerprint`. Subsequent connects compare against it; a mismatch is refused
(possible MITM). This is trust-on-first-use, surfaced to the user on the first connection.

## Plex integration

`PlexClient` (an `HttpClient` wrapper) is used only for:
- `GET /library/sections` (+ token) to list libraries so the user can map Movies/Shows section ids.
- `GET /library/sections/{id}/refresh?path=<encoded remote path>` to scan just the imported folder.

The `X-Plex-Token` is a secret (in `secrets.dat`). Nothing else about Plex is stored.

## Updates

`VelopackApp.Build().Run()` is the first call in `Main` (handles install/update hooks).
`UpdateService` polls the public GitHub repo (5s after startup, then hourly) via an unauthenticated
`GithubSource`, and drives the main-window banner (Install / Skip this version / Later). Under
`dotnet run` the app is not "installed", so the banner can show but Install is disabled.

## Testing strategy

- Core is pure - every planner and the cleanup scanner are unit-tested against
  `InMemoryMediaFileSystem`, including: episode/movie parsing (`s01e01`, `1x01`, already-normalized),
  name sanitization, collision detection, deepest-first cleanup ordering, min-age and exclusion
  rules, and idempotent re-runs.
- Backends and UI are validated by manual smoke tests and, in P6, a live end-to-end run against a
  throwaway structure on the real server.

## Dependencies (NuGet, latest as of 2026-07-02)

- `Avalonia`, `Avalonia.Desktop`, `Avalonia.Themes.Fluent`, `Avalonia.Fonts.Inter` 12.0.5
- `AvaloniaUI.DiagnosticsSupport` 2.2.3 (Debug only)
- `CommunityToolkit.Mvvm` 8.4.2
- `SSH.NET` 2025.1.0
- `System.Security.Cryptography.ProtectedData` 10.0.9
- `Velopack` 1.2.0
- Test: `Microsoft.NET.Test.Sdk` 18.7.0, `xunit` 2.9.3, `xunit.runner.visualstudio` 3.1.5,
  `FluentAssertions` 7.2.2 (pinned - 8.x is a paid license), `coverlet.collector` 10.0.1
