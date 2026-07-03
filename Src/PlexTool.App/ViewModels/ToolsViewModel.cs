using System.Diagnostics;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using PlexTool.App.Backends;
using PlexTool.App.Services;
using PlexTool.Core;

namespace PlexTool.App.ViewModels;

/// <summary>
/// Home for quick one-off utility actions (manual Plex scans, connection tests, open config folder).
/// Surfaced as the Tools tab and bound to window keybindings. To add a utility: write a
/// <c>[RelayCommand]</c> method here, add a button in ToolsView, and optionally a KeyBinding in
/// MainWindow.axaml. Keep each action self-contained and set <see cref="Result"/> with the outcome.
/// </summary>
public sealed partial class ToolsViewModel : ViewModelBase
{
    private readonly AppHost _host;

    public ToolsViewModel(AppHost host) => _host = host;

    /// <summary>Last action's outcome, shown in the Tools tab.</summary>
    [ObservableProperty] private string _result = "";

    [ObservableProperty] private bool _isBusy;

    /// <summary>Trigger a full scan of the Movies library (Ctrl+M).</summary>
    [RelayCommand]
    private Task ScanMovies() => ScanAsync(MediaKind.Movie);

    /// <summary>Trigger a full scan of the TV Shows library (Ctrl+T).</summary>
    [RelayCommand]
    private Task ScanShows() => ScanAsync(MediaKind.Show);

    private async Task ScanAsync(MediaKind kind)
    {
        if (IsBusy)
            return;
        IsBusy = true;
        Result = $"Requesting {(kind == MediaKind.Movie ? "Movies" : "Shows")} scan...";
        try
        {
            string? sectionId = kind == MediaKind.Movie
                ? _host.Settings.PlexMoviesSectionId
                : _host.Settings.PlexShowsSectionId;

            // No path -> full-section refresh.
            PlexResult result = await _host.Plex.RefreshAsync(
                _host.Settings.PlexBaseUrl, _host.Secrets.PlexToken, sectionId, scanPath: null);
            Result = result.Message;
        }
        finally
        {
            IsBusy = false;
        }
    }

    /// <summary>Verify the SSH connection to the storage box.</summary>
    [RelayCommand]
    private async Task TestSsh()
    {
        if (IsBusy)
            return;
        IsBusy = true;
        Result = "Testing SSH...";
        try
        {
            SshTestResult result = await _host.Ssh.TestAsync(_host.Settings, _host.Secrets);
            Result = result.Message;
        }
        finally
        {
            IsBusy = false;
        }
    }

    /// <summary>Verify Plex and report how many libraries it sees.</summary>
    [RelayCommand]
    private async Task TestPlex()
    {
        if (IsBusy)
            return;
        IsBusy = true;
        Result = "Testing Plex...";
        try
        {
            PlexResult result = await _host.Plex.GetSectionsAsync(_host.Settings.PlexBaseUrl, _host.Secrets.PlexToken);
            Result = result.Message;
        }
        finally
        {
            IsBusy = false;
        }
    }

    /// <summary>Open the per-user config folder (%APPDATA%\PlexTool) in Explorer.</summary>
    [RelayCommand]
    private void OpenConfigFolder()
    {
        try
        {
            Directory.CreateDirectory(AppPaths.BaseDirectory);
            Process.Start(new ProcessStartInfo { FileName = AppPaths.BaseDirectory, UseShellExecute = true });
            Result = $"Opened {AppPaths.BaseDirectory}";
        }
        catch (Exception ex)
        {
            Result = "Could not open the config folder: " + ex.Message;
        }
    }
}
