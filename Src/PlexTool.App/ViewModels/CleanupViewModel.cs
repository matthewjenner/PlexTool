using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using PlexTool.App.Backends;
using PlexTool.App.Services;
using PlexTool.Core;
using PlexTool.Core.Cleanup;

namespace PlexTool.App.ViewModels;

/// <summary>Which server folder to sweep for empty directories.</summary>
public enum CleanupFolder
{
    Movies,
    Shows,
    Staging,
    Custom,
}

/// <summary>One row in the cleanup preview/result.</summary>
public sealed record CleanupRow(string Path, string Status);

/// <summary>
/// Backs the Cleanup tab: sweep a folder for empty directories, mirroring Clean-EmptyFolders.ps1.
/// Removes empty folders only (never files), deepest-first, with a min-age gate, name exclusions,
/// symlink skipping, and optional prune-empty-parents. Preview lists what would be removed; Delete
/// applies it. Nothing is removed until Delete.
/// </summary>
public sealed partial class CleanupViewModel : ViewModelBase
{
    private readonly AppHost _host;

    public CleanupViewModel(AppHost host)
    {
        _host = host;
        MinAgeMinutes = host.Settings.CleanupMinAgeMinutes;
        PruneParents = host.Settings.CleanupPruneParents;
        ExclusionsText = string.Join(Environment.NewLine, host.Settings.CleanupExclusions);
    }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsLocal))]
    [NotifyPropertyChangedFor(nameof(ShowCustomPath))]
    private OperationTarget _target = OperationTarget.Server;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ShowCustomPath))]
    private CleanupFolder _serverFolder = CleanupFolder.Staging;

    [ObservableProperty] private string _customPath = "";
    [ObservableProperty] private string _localRoot = "";
    [ObservableProperty] private int _minAgeMinutes;
    [ObservableProperty] private bool _pruneParents;
    [ObservableProperty] private string _exclusionsText = "";
    [ObservableProperty] private string _statusText = "";
    [ObservableProperty] private bool _isBusy;

    public bool IsLocal => Target == OperationTarget.Local;
    public bool ShowCustomPath => Target == OperationTarget.Server && ServerFolder == CleanupFolder.Custom;

    public IReadOnlyList<OperationTarget> Targets { get; } = Enum.GetValues<OperationTarget>();
    public IReadOnlyList<CleanupFolder> ServerFolders { get; } = Enum.GetValues<CleanupFolder>();

    public ObservableCollection<CleanupRow> Rows { get; } = [];

    [RelayCommand]
    private Task PreviewAsync() => RunAsync(execute: false);

    [RelayCommand]
    private Task DeleteAsync() => RunAsync(execute: true);

    private async Task RunAsync(bool execute)
    {
        if (IsBusy)
            return;
        IsBusy = true;
        StatusText = execute ? "Removing empty folders..." : "Scanning for empty folders...";
        try
        {
            string root = ResolveRoot();
            var minAge = TimeSpan.FromMinutes(Math.Max(0, MinAgeMinutes));
            List<string> exclusions = SplitLines(ExclusionsText);
            bool prune = PruneParents;

            IMediaFileSystem fs = await CreateFileSystemAsync();
            (List<CleanupRow> rows, int removed, int failed) = await Task.Run(() =>
            {
                try
                {
                    IReadOnlyList<string> removable = EmptyFolderScanner.FindRemovable(
                        fs, root, DateTimeOffset.UtcNow, minAge, exclusions, prune);

                    var built = new List<CleanupRow>(removable.Count);
                    int ok = 0, bad = 0;

                    if (execute)
                    {
                        foreach (string path in removable) // already deepest-first
                        {
                            try { fs.Delete(path); ok++; built.Add(new CleanupRow(path, "removed")); }
                            catch { bad++; built.Add(new CleanupRow(path, "skipped (in use / perms)")); }
                        }
                    }
                    else
                    {
                        foreach (string path in removable)
                            built.Add(new CleanupRow(path, "will remove"));
                    }

                    return (built, ok, bad);
                }
                finally
                {
                    (fs as IDisposable)?.Dispose();
                }
            });

            Rows.Clear();
            foreach (CleanupRow row in rows)
                Rows.Add(row);

            StatusText = execute
                ? $"Removed {removed} empty folder(s)" + (failed > 0 ? $"; {failed} skipped." : ".")
                : rows.Count == 0
                    ? "No empty folders to remove."
                    : $"{rows.Count} empty folder(s) would be removed.";
        }
        catch (Exception ex)
        {
            StatusText = "Error: " + ex.Message;
        }
        finally
        {
            IsBusy = false;
        }
    }

    private async Task<IMediaFileSystem> CreateFileSystemAsync()
    {
        if (Target == OperationTarget.Local)
            return new LocalMediaFileSystem();
        return await _host.Ssh.ConnectAsync(_host.Settings, _host.Secrets);
    }

    private string ResolveRoot()
    {
        if (Target == OperationTarget.Local)
        {
            if (string.IsNullOrWhiteSpace(LocalRoot))
                throw new InvalidOperationException("Pick a local folder to sweep first.");
            if (!Directory.Exists(LocalRoot))
                throw new InvalidOperationException("The local folder does not exist.");
            return LocalRoot;
        }

        string root = ServerFolder switch
        {
            CleanupFolder.Movies => _host.Settings.RemoteMoviesPath,
            CleanupFolder.Shows => _host.Settings.RemoteShowsPath,
            CleanupFolder.Staging => _host.Settings.StagingPath,
            CleanupFolder.Custom => CustomPath.Trim(),
            _ => "",
        };

        if (string.IsNullOrWhiteSpace(root))
            throw new InvalidOperationException(
                ServerFolder == CleanupFolder.Custom
                    ? "Enter a custom path to sweep."
                    : $"Configure the {ServerFolder} folder in Settings first.");
        return root;
    }

    private static List<string> SplitLines(string text) =>
        text.Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).ToList();
}
