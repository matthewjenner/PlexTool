using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using PlexTool.App.Backends;
using PlexTool.App.Services;
using PlexTool.Core;
using PlexTool.Core.Naming;
using PlexTool.Core.Planning;

namespace PlexTool.App.ViewModels;

/// <summary>One file in the rename preview, with a checkbox for whether to apply it.</summary>
public sealed partial class RenameRowViewModel : ViewModelBase
{
    public RenameRowViewModel(RenameOp op)
    {
        SourcePath = op.SourcePath;
        SourceName = op.SourceName;
        TargetName = op.TargetName;
        TargetPath = op.TargetPath;
        Status = op.Status;
        Actionable = op.Status == RenameStatus.WillRename;
        IsSelected = Actionable; // default-select the ones that will actually rename
    }

    public string SourcePath { get; }
    public string SourceName { get; }
    public string? TargetName { get; }
    public string? TargetPath { get; }
    public RenameStatus Status { get; }

    /// <summary>Only WillRename rows can be applied; the rest are informational.</summary>
    public bool Actionable { get; }

    [ObservableProperty] private bool _isSelected;

    public string StatusText => Status switch
    {
        RenameStatus.WillRename => "rename",
        RenameStatus.AlreadyCorrect => "ok",
        RenameStatus.Collision => "collision",
        RenameStatus.NoEpisodePattern => "no S/E",
        _ => "",
    };

    /// <summary>"old -> new" for a rename, or just the name for a skip.</summary>
    public string Change => TargetName is null ? SourceName : $"{SourceName}  ->  {TargetName}";
}

/// <summary>
/// Backs the Rename / Normalize tab: scan a Movies or Shows library and rename media files toward
/// Plex form, in place. Preview shows every proposed change (and skips: already-correct, collision,
/// no season/episode); Apply renames only the checked ones. Nothing is written until Apply.
/// </summary>
public sealed partial class RenameViewModel : ViewModelBase
{
    private readonly AppHost _host;

    public RenameViewModel(AppHost host) => _host = host;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsLocal))]
    private OperationTarget _target = OperationTarget.Server;

    [ObservableProperty] private MediaKind _kind = MediaKind.Movie;
    [ObservableProperty] private string _localRoot = "";
    [ObservableProperty] private string _statusText = "";
    [ObservableProperty] private bool _isBusy;

    public bool IsLocal => Target == OperationTarget.Local;

    public IReadOnlyList<OperationTarget> Targets { get; } = Enum.GetValues<OperationTarget>();
    public IReadOnlyList<MediaKind> Kinds { get; } = Enum.GetValues<MediaKind>();

    public ObservableCollection<RenameRowViewModel> Rows { get; } = [];

    [RelayCommand]
    private void SelectAll()
    {
        foreach (RenameRowViewModel row in Rows)
            if (row.Actionable)
                row.IsSelected = true;
    }

    [RelayCommand]
    private void SelectNone()
    {
        foreach (RenameRowViewModel row in Rows)
            row.IsSelected = false;
    }

    [RelayCommand]
    private async Task PreviewAsync()
    {
        if (IsBusy)
            return;
        IsBusy = true;
        StatusText = "Connecting and scanning...";
        try
        {
            string root = ResolveRoot();
            IReadOnlySet<string> exts = ExtensionSet();
            var namer = new PlexNamer(_host.Settings.NamingScheme);
            MediaKind kind = Kind;

            IMediaFileSystem fs = await CreateFileSystemAsync();
            IReadOnlyList<RenameOp> ops = await Task.Run(() =>
            {
                try { return Plan(fs, root, exts, namer, kind); }
                finally { (fs as IDisposable)?.Dispose(); }
            });

            Populate(ops);
            StatusText = Summarize(ops);
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

    [RelayCommand]
    private async Task ApplyAsync()
    {
        if (IsBusy)
            return;

        var toApply = Rows
            .Where(r => r.IsSelected && r.Actionable && r.TargetPath is not null)
            .Select(r => (r.SourcePath, Target: r.TargetPath!))
            .ToList();

        if (toApply.Count == 0)
        {
            StatusText = "Select at least one file to rename (only 'rename' rows can be applied).";
            return;
        }

        IsBusy = true;
        StatusText = $"Renaming {toApply.Count} file(s)...";
        try
        {
            string root = ResolveRoot();
            IReadOnlySet<string> exts = ExtensionSet();
            var namer = new PlexNamer(_host.Settings.NamingScheme);
            MediaKind kind = Kind;

            IMediaFileSystem fs = await CreateFileSystemAsync();
            (int renamed, int failed, IReadOnlyList<RenameOp> fresh) = await Task.Run(() =>
            {
                try
                {
                    int ok = 0, bad = 0;
                    foreach ((string source, string target) in toApply)
                    {
                        try { fs.Move(source, target); ok++; }
                        catch { bad++; } // e.g. a collision that appeared between preview and apply
                    }
                    return (ok, bad, Plan(fs, root, exts, namer, kind));
                }
                finally
                {
                    (fs as IDisposable)?.Dispose();
                }
            });

            Populate(fresh);
            string failNote = failed > 0 ? $" {failed} failed (likely a collision)." : "";
            StatusText = $"Renamed {renamed} file(s).{failNote} " + Summarize(fresh);
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

    private static IReadOnlyList<RenameOp> Plan(
        IMediaFileSystem fs, string root, IReadOnlySet<string> exts, PlexNamer namer, MediaKind kind) =>
        kind == MediaKind.Movie
            ? RenamePlanner.PlanMovies(fs, root, exts)
            : RenamePlanner.PlanShows(fs, root, exts, namer);

    private void Populate(IReadOnlyList<RenameOp> ops)
    {
        Rows.Clear();
        foreach (RenameOp op in ops)
            Rows.Add(new RenameRowViewModel(op));
    }

    private static string Summarize(IReadOnlyList<RenameOp> ops)
    {
        int rename = ops.Count(o => o.Status == RenameStatus.WillRename);
        int ok = ops.Count(o => o.Status == RenameStatus.AlreadyCorrect);
        int collision = ops.Count(o => o.Status == RenameStatus.Collision);
        int noSe = ops.Count(o => o.Status == RenameStatus.NoEpisodePattern);
        return $"{rename} to rename, {ok} already correct, {collision} collision(s), {noSe} without S/E.";
    }

    private IReadOnlySet<string> ExtensionSet() =>
        new HashSet<string>(_host.Settings.MediaExtensions, StringComparer.OrdinalIgnoreCase);

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
                throw new InvalidOperationException("Pick a local root folder first.");
            if (!Directory.Exists(LocalRoot))
                throw new InvalidOperationException("The local root folder does not exist.");
            return LocalRoot;
        }

        string root = Kind == MediaKind.Movie ? _host.Settings.RemoteMoviesPath : _host.Settings.RemoteShowsPath;
        if (string.IsNullOrWhiteSpace(root))
            throw new InvalidOperationException(
                $"Configure the {(Kind == MediaKind.Movie ? "Movies" : "Shows")} root in Settings first.");
        return root;
    }
}
