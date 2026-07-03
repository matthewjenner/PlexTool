using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using PlexTool.App.Services;
using PlexTool.Core;
using PlexTool.Core.Naming;
using PlexTool.Core.Planning;

namespace PlexTool.App.ViewModels;

/// <summary>One entry in the staging folder listing.</summary>
public sealed record StagingItem(string Name, string FullPath, bool IsDirectory)
{
    public string Display => IsDirectory ? Name + "/" : Name;
}

/// <summary>One line in the import preview.</summary>
public sealed record ImportRow(string StatusText, string Change);

/// <summary>
/// Backs the Import tab: move a staged item from the storage box's staging folder into the library,
/// renamed to Plex form, then trigger a Plex scan. Because staging and the library share a
/// filesystem, each move is an instant server-side rename (no upload). Server-only by nature.
/// Nothing is written until Import.
/// </summary>
public sealed partial class ImportViewModel : ViewModelBase
{
    private readonly AppHost _host;

    public ImportViewModel(AppHost host)
    {
        _host = host;
        Year = DateTime.Now.Year;
    }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsMovie))]
    private MediaKind _kind = MediaKind.Movie;

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(PreviewCommand))]
    [NotifyCanExecuteChangedFor(nameof(ImportCommand))]
    private StagingItem? _selectedItem;

    [ObservableProperty] private string _name = "";
    [ObservableProperty] private int _year;
    [ObservableProperty] private bool _removeSourceWhenEmpty = true;
    [ObservableProperty] private bool _triggerPlexScan = true;
    [ObservableProperty] private string _statusText = "";
    [ObservableProperty] private bool _isBusy;

    public bool IsMovie => Kind == MediaKind.Movie;

    public IReadOnlyList<MediaKind> Kinds { get; } = Enum.GetValues<MediaKind>();
    public ObservableCollection<StagingItem> StagingItems { get; } = [];
    public ObservableCollection<ImportRow> Rows { get; } = [];

    partial void OnSelectedItemChanged(StagingItem? value)
    {
        // Prefill the name from the picked item as a starting point (the user cleans it up).
        if (value is null)
            return;
        Name = value.IsDirectory ? value.Name : StripExtension(value.Name);
    }

    [RelayCommand]
    private async Task LoadStagingAsync()
    {
        if (IsBusy)
            return;
        IsBusy = true;
        StatusText = "Listing staging folder...";
        try
        {
            string staging = RequireStagingPath();
            List<StagingItem> items = await Task.Run(() =>
            {
                using var fs = _host.Ssh.ConnectAsync(_host.Settings, _host.Secrets).GetAwaiter().GetResult();
                return fs.List(staging)
                    .OrderByDescending(e => e.IsDirectory)
                    .ThenBy(e => e.Name, StringComparer.OrdinalIgnoreCase)
                    .Select(e => new StagingItem(e.Name, e.FullPath, e.IsDirectory))
                    .ToList();
            });

            StagingItems.Clear();
            foreach (StagingItem item in items)
                StagingItems.Add(item);

            StatusText = $"Staging: {items.Count} item(s). Pick one, set the type/name, then Preview.";
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

    [RelayCommand(CanExecute = nameof(HasSelection))]
    private async Task PreviewAsync()
    {
        await RunAsync(execute: false);
    }

    [RelayCommand(CanExecute = nameof(HasSelection))]
    private async Task ImportAsync()
    {
        await RunAsync(execute: true);
    }

    private bool HasSelection() => SelectedItem is not null;

    private async Task RunAsync(bool execute)
    {
        if (IsBusy || SelectedItem is null)
            return;

        IsBusy = true;
        StatusText = execute ? "Importing..." : "Building preview...";
        try
        {
            ValidateInputs();
            string source = SelectedItem.FullPath;
            bool sourceIsDir = SelectedItem.IsDirectory;
            string root = LibraryRoot();
            var namer = new PlexNamer(_host.Settings.NamingScheme);
            IReadOnlySet<string> exts = ExtensionSet();
            MediaKind kind = Kind;
            string name = Name.Trim();
            int year = Year;

            (List<ImportRow> rows, int moved, int failed, bool sourceRemoved, ImportPlan plan) = await Task.Run(() =>
            {
                using var fs = _host.Ssh.ConnectAsync(_host.Settings, _host.Secrets).GetAwaiter().GetResult();

                ImportPlan p = kind == MediaKind.Movie
                    ? ImportPlanner.PlanMovie(fs, source, root, namer, name, year, exts)
                    : ImportPlanner.PlanShow(fs, source, root, namer, name, exts);

                int movedCount = 0, failedCount = 0;
                bool removed = false;

                if (execute)
                {
                    foreach (string dir in p.DirectoriesToCreate)
                        fs.CreateDirectory(dir);

                    foreach (ImportFileOp file in p.Files.Where(f => f.Status == ImportStatus.WillMove))
                    {
                        try { fs.Move(file.SourcePath, file.TargetPath!); movedCount++; }
                        catch { failedCount++; }
                    }

                    if (RemoveSourceWhenEmpty && sourceIsDir && fs.DirectoryExists(source) && fs.List(source).Count == 0)
                    {
                        fs.Delete(source);
                        removed = true;
                    }
                }

                var built = p.Files.Select(f => new ImportRow(StatusText: StatusLabel(f.Status), Change: ChangeText(f))).ToList();
                return (built, movedCount, failedCount, removed, p);
            });

            Rows.Clear();
            foreach (ImportRow row in rows)
                Rows.Add(row);

            int willMove = plan.Files.Count(f => f.Status == ImportStatus.WillMove);
            int collisions = plan.Files.Count(f => f.Status == ImportStatus.Collision);
            int noSe = plan.Files.Count(f => f.Status == ImportStatus.NoEpisodePattern);

            if (!execute)
            {
                StatusText = $"Preview: {willMove} to move, {collisions} collision(s), {noSe} without S/E. Target: {plan.ScanPath}";
            }
            else
            {
                string failNote = failed > 0 ? $" {failed} failed (collision)." : "";
                string removedNote = sourceRemoved ? " Source folder removed (was empty)." : "";
                string scanNote = await MaybeScanAsync(execute && moved > 0, kind, plan.ScanPath);
                StatusText = $"Imported {moved} file(s).{failNote}{removedNote}{scanNote}";
            }
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

    private async Task<string> MaybeScanAsync(bool shouldScan, MediaKind kind, string libraryPath)
    {
        if (!shouldScan || !TriggerPlexScan)
            return "";

        string? sectionId = kind == MediaKind.Movie
            ? _host.Settings.PlexMoviesSectionId
            : _host.Settings.PlexShowsSectionId;
        string plexPath = _host.Settings.ToPlexPath(libraryPath);

        PlexResult result = await _host.Plex.RefreshAsync(
            _host.Settings.PlexBaseUrl, _host.Secrets.PlexToken, sectionId, plexPath);
        return " " + result.Message;
    }

    private void ValidateInputs()
    {
        if (string.IsNullOrWhiteSpace(Name))
            throw new InvalidOperationException("Enter a name for the item.");
        if (Kind == MediaKind.Movie && Year is < 1888 or > 2100)
            throw new InvalidOperationException("Year must be between 1888 and 2100.");
    }

    private string RequireStagingPath()
    {
        string staging = _host.Settings.StagingPath;
        if (string.IsNullOrWhiteSpace(staging))
            throw new InvalidOperationException("Configure the Staging folder in Settings first.");
        return staging;
    }

    private string LibraryRoot()
    {
        string root = Kind == MediaKind.Movie ? _host.Settings.RemoteMoviesPath : _host.Settings.RemoteShowsPath;
        if (string.IsNullOrWhiteSpace(root))
            throw new InvalidOperationException(
                $"Configure the {(Kind == MediaKind.Movie ? "Movies" : "Shows")} root in Settings first.");
        return root;
    }

    private IReadOnlySet<string> ExtensionSet() =>
        new HashSet<string>(_host.Settings.MediaExtensions, StringComparer.OrdinalIgnoreCase);

    private static string StatusLabel(ImportStatus status) => status switch
    {
        ImportStatus.WillMove => "move",
        ImportStatus.Collision => "collision",
        ImportStatus.NoEpisodePattern => "no S/E",
        _ => "",
    };

    private static string ChangeText(ImportFileOp file) =>
        file.TargetName is null ? file.SourceName : $"{file.SourceName}  ->  {file.TargetName}";

    private static string StripExtension(string fileName)
    {
        int dot = fileName.LastIndexOf('.');
        return dot > 0 ? fileName[..dot] : fileName;
    }
}
