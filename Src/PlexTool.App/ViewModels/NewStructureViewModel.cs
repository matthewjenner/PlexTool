using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using PlexTool.App.Backends;
using PlexTool.App.Services;
using PlexTool.Core;
using PlexTool.Core.Naming;
using PlexTool.Core.Planning;

namespace PlexTool.App.ViewModels;

/// <summary>Where a structure is created: the remote storage box, or the local filesystem.</summary>
public enum StructureTarget
{
    Server,
    Local,
}

/// <summary>One row in the New Structure preview: a folder path and its state.</summary>
public sealed record FolderPreviewRow(string Path, string Status);

/// <summary>
/// Backs the New Structure tab: create the standardized folders for a movie
/// (<c>Name (Year)</c>) or a show (<c>Show/Season NN</c>), on the server or locally. Preview
/// builds the plan and shows which folders exist vs will be created; Create makes the missing ones.
/// Nothing is written until Create is pressed.
/// </summary>
public sealed partial class NewStructureViewModel : ViewModelBase
{
    private readonly AppHost _host;

    public NewStructureViewModel(AppHost host)
    {
        _host = host;
        Year = DateTime.Now.Year;
    }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsLocal))]
    private StructureTarget _target = StructureTarget.Server;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsMovie))]
    [NotifyPropertyChangedFor(nameof(IsShow))]
    private MediaKind _kind = MediaKind.Movie;

    [ObservableProperty] private string _name = "";
    [ObservableProperty] private int _year;
    [ObservableProperty] private int _seasons = 1;
    [ObservableProperty] private string _localRoot = "";
    [ObservableProperty] private string _statusText = "";
    [ObservableProperty] private bool _isBusy;

    public bool IsMovie => Kind == MediaKind.Movie;
    public bool IsShow => Kind == MediaKind.Show;
    public bool IsLocal => Target == StructureTarget.Local;

    public IReadOnlyList<StructureTarget> Targets { get; } = Enum.GetValues<StructureTarget>();
    public IReadOnlyList<MediaKind> Kinds { get; } = Enum.GetValues<MediaKind>();

    public ObservableCollection<FolderPreviewRow> Preview { get; } = [];

    [RelayCommand]
    private Task PreviewAsync() => RunAsync(execute: false);

    [RelayCommand]
    private Task CreateAsync() => RunAsync(execute: true);

    private async Task RunAsync(bool execute)
    {
        if (IsBusy)
            return;

        IsBusy = true;
        StatusText = execute ? "Creating..." : "Connecting and building preview...";

        try
        {
            string root = ResolveRoot();
            ValidateInputs();

            var namer = new PlexNamer(_host.Settings.NamingScheme);
            string name = Name.Trim();
            int year = Year, seasons = Seasons;
            MediaKind kind = Kind;

            IMediaFileSystem fs = await CreateFileSystemAsync();
            (List<FolderPreviewRow> rows, int created, int alreadyExisted, int total) = await Task.Run(() =>
            {
                try
                {
                    // Plan once. Each folder's status and the counts come from this single snapshot,
                    // so a just-created folder is counted as "created", never double-counted as
                    // "existing". Existing folders are skipped, never touched (no clobber).
                    IReadOnlyList<FolderPlanItem> plan = BuildPlan(fs, root, namer, kind, name, year, seasons);
                    var built = new List<FolderPreviewRow>(plan.Count);
                    int createdCount = 0;

                    foreach (FolderPlanItem item in plan)
                    {
                        string status;
                        if (item.AlreadyExists)
                        {
                            status = "exists";
                        }
                        else if (execute)
                        {
                            fs.CreateDirectory(item.Path);
                            createdCount++;
                            status = "created";
                        }
                        else
                        {
                            status = "will create";
                        }
                        built.Add(new FolderPreviewRow(item.Path, status));
                    }

                    return (built, createdCount, plan.Count(i => i.AlreadyExists), plan.Count);
                }
                finally
                {
                    (fs as IDisposable)?.Dispose();
                }
            });

            Preview.Clear();
            foreach (FolderPreviewRow row in rows)
                Preview.Add(row);

            StatusText = execute
                ? $"Created {created} folder(s); {alreadyExisted} already existed."
                : $"Preview: {total - alreadyExisted} to create, {alreadyExisted} already exist.";
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

    private static IReadOnlyList<FolderPlanItem> BuildPlan(
        IMediaFileSystem fs, string root, PlexNamer namer, MediaKind kind, string name, int year, int seasons) =>
        kind == MediaKind.Movie
            ? FolderStructurePlanner.PlanMovie(fs, root, namer, name, year)
            : FolderStructurePlanner.PlanShow(fs, root, namer, name, seasons);

    private async Task<IMediaFileSystem> CreateFileSystemAsync()
    {
        if (Target == StructureTarget.Local)
            return new LocalMediaFileSystem();
        return await _host.Ssh.ConnectAsync(_host.Settings, _host.Secrets);
    }

    private string ResolveRoot()
    {
        if (Target == StructureTarget.Local)
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

    private void ValidateInputs()
    {
        if (string.IsNullOrWhiteSpace(Name))
            throw new InvalidOperationException("Enter a name.");
        if (Kind == MediaKind.Movie && Year is < 1888 or > 2100)
            throw new InvalidOperationException("Year must be between 1888 and 2100.");
        if (Kind == MediaKind.Show && Seasons < 1)
            throw new InvalidOperationException("Seasons must be at least 1.");
    }
}
