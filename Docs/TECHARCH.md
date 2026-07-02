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
      Naming/                      # NameSanitizer, EpisodeParser, PlexNamer
      Planning/                    # FolderStructurePlan, RenamePlan, ImportPlan
      Cleanup/                     # EmptyFolderScanner (over IMediaFileSystem)
      IMediaFileSystem.cs          # Interface only - list/stat/mkdir/rename/delete/write
    PlexTool.App/                  # Avalonia + all I/O
      Backends/                    # LocalMediaFileSystem, SftpMediaFileSystem, SshCommandRunner
      Services/                    # AppHost, SettingsStore, AppSettings, Secrets, SecretStore,
                                   #   AppPaths, UpdateService, AppVersion, PlexClient, ConnectionManager
      Execution/                   # ImportRunner, RenameExecutor, StructureCreator, CleanupRunner, WatchService
      ViewModels/                  # One VM per tab + MainWindowViewModel
      Views/                       # MainWindow + one View per tab
      App.axaml / Program.cs / app.manifest
  Tests/
    PlexTool.Core.Tests/           # xUnit; InMemoryMediaFileSystem fake
```

Only the **Services** layer (settings, secrets, updates, host) and the **shell** (Program, App,
MainWindow, update banner) exist as of phase 0. The rest lands phase by phase (see workplan).

## The core abstraction: IMediaFileSystem

`PlexTool.Core` never performs I/O. It defines one interface that both a local and a remote
backend satisfy:

```csharp
public interface IMediaFileSystem
{
    IEnumerable<MediaEntry> List(string path);         // files + dirs, with mtime/size/kind
    bool Exists(string path);
    void CreateDirectory(string path);
    void Move(string source, string destination);      // in-place rename / move, never copy+delete
    void Delete(string path);
    Stream OpenWrite(string path);                      // for uploads
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
- **ConnectionManager** owns the live SSH/SFTP session; SSH.NET clients are not thread-safe, so
  access is serialized.

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
