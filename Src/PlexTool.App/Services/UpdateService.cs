using Avalonia.Threading;
using Velopack;
using Velopack.Sources;

namespace PlexTool.App.Services;

/// <summary>
/// Polls the PlexTool GitHub releases page for a newer version and exposes a flag the main
/// window banner binds to. Initial check fires shortly after startup; then hourly. Any network
/// failure is swallowed - the banner just stays hidden and the next tick tries again.
/// </summary>
/// <remarks>
/// Dev runs (started via <c>dotnet run</c>) are not "installed" from Velopack's perspective,
/// so <see cref="UpdateManager.IsInstalled"/> is false and <see cref="InstallAndRestartAsync"/>
/// is a no-op. The banner can still appear during dev (handy for UI testing) but the Install
/// button stays disabled. The repo MUST be public for the unauthenticated GithubSource to work.
/// </remarks>
public sealed class UpdateService : IDisposable
{
    private const string GitHubRepoUrl = "https://github.com/matthewjenner/PlexTool";
    private static readonly TimeSpan StartupDelay = TimeSpan.FromSeconds(5);
    private static readonly TimeSpan PollInterval = TimeSpan.FromHours(1);

    private readonly AppHost _host;
    private readonly UpdateManager? _manager;
    private readonly CancellationTokenSource _cts = new();
    private UpdateInfo? _pending;

    public UpdateService(AppHost host)
    {
        _host = host;

        try
        {
            _manager = new UpdateManager(new GithubSource(GitHubRepoUrl, accessToken: null, prerelease: false));
        }
        catch
        {
            // If the GitHub source can't even be constructed (offline / malformed URL),
            // leave the manager null - the rest of the app keeps working without updates.
            _manager = null;
        }

        _ = Task.Run(() => PollLoopAsync(_cts.Token));
    }

    /// <summary>Raised on the UI thread when an update appears, disappears or is dismissed.</summary>
    public event Action<string?>? UpdateAvailableChanged;

    /// <summary>Semver of the available update, or null when none is offered.</summary>
    public string? AvailableVersion { get; private set; }

    /// <summary>True only when Velopack is in installed mode (i.e. not a <c>dotnet run</c>).</summary>
    public bool CanInstall => _manager?.IsInstalled ?? false;

    /// <summary>Downloads the pending update and restarts into the new version.</summary>
    public async Task InstallAndRestartAsync()
    {
        if (_manager is null || _pending is null || !_manager.IsInstalled)
            return;

        try
        {
            await _manager.DownloadUpdatesAsync(_pending);
            _manager.ApplyUpdatesAndRestart(_pending);
        }
        catch
        {
            // Best-effort: a failed apply leaves the banner up so the user can try again.
        }
    }

    /// <summary>Records this version as "skipped" in settings; banner stays hidden until a newer one.</summary>
    public void SkipCurrentVersion()
    {
        if (AvailableVersion is null)
            return;
        _host.UpdateSettings(_host.Settings with { SkippedUpdateVersion = AvailableVersion });
        SetAvailable(null);
    }

    /// <summary>Hides the banner in-memory; the next hourly check re-evaluates.</summary>
    public void DismissForNow() => SetAvailable(null);

    private async Task PollLoopAsync(CancellationToken ct)
    {
        try
        {
            await Task.Delay(StartupDelay, ct);
            while (!ct.IsCancellationRequested)
            {
                await CheckOnceAsync();
                await Task.Delay(PollInterval, ct);
            }
        }
        catch (OperationCanceledException)
        {
            // Normal on shutdown.
        }
    }

    private async Task CheckOnceAsync()
    {
        if (_manager is null)
            return;

        try
        {
            UpdateInfo? info = await _manager.CheckForUpdatesAsync();
            if (info is null)
            {
                SetAvailable(null);
                return;
            }

            string version = info.TargetFullRelease.Version.ToString();
            if (string.Equals(version, _host.Settings.SkippedUpdateVersion, StringComparison.Ordinal))
            {
                SetAvailable(null);
                return;
            }

            _pending = info;
            SetAvailable(version);
        }
        catch
        {
            // Network down, no releases yet, dev mode without an installed app, etc.
            // Stay quiet - the banner is opt-in nice-to-have, not a critical path.
        }
    }

    private void SetAvailable(string? version)
    {
        if (string.Equals(version, AvailableVersion, StringComparison.Ordinal))
            return;

        Dispatcher.UIThread.Post(() =>
        {
            AvailableVersion = version;
            UpdateAvailableChanged?.Invoke(version);
        });
    }

    public void Dispose()
    {
        _cts.Cancel();
        _cts.Dispose();
    }
}
