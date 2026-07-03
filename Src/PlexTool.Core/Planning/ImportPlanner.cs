using PlexTool.Core.Naming;

namespace PlexTool.Core.Planning;

/// <summary>What should happen to a media file during an import.</summary>
public enum ImportStatus
{
    /// <summary>The file will be moved (server-side rename) into the library under its new name.</summary>
    WillMove,

    /// <summary>The target already exists (or two source files map to the same target) - skipped.</summary>
    Collision,

    /// <summary>A show file with no parseable season/episode token - skipped.</summary>
    NoEpisodePattern,
}

/// <summary>One planned file move for an import. Target fields are null for skips.</summary>
public sealed record ImportFileOp(
    string SourcePath,
    string SourceName,
    string? TargetPath,
    string? TargetName,
    ImportStatus Status);

/// <summary>
/// A full import plan: the directories to ensure exist, the per-file moves, and the library path
/// Plex should scan afterward.
/// </summary>
public sealed record ImportPlan(
    IReadOnlyList<string> DirectoriesToCreate,
    IReadOnlyList<ImportFileOp> Files,
    string ScanPath);

/// <summary>
/// Plans importing a staged item into the library. Because staging lives on the same filesystem as
/// the library, each move is a server-side rename (instant, no copy). Movies land in
/// <c>&lt;moviesRoot&gt;/Name (Year)/Name (Year).ext</c>; episodes land in
/// <c>&lt;showsRoot&gt;/Show/Season NN/Show - S01E01.ext</c>. Pure: reads through
/// <see cref="IMediaFileSystem"/>, writes nothing. Never overwrites (collisions are flagged/skipped).
/// </summary>
public static class ImportPlanner
{
    /// <summary>Plans a movie import from <paramref name="source"/> (a folder or a single file).</summary>
    public static ImportPlan PlanMovie(
        IMediaFileSystem fs, string source, string moviesRoot, PlexNamer namer,
        string name, int year, IReadOnlySet<string> mediaExtensions)
    {
        string movieFolder = fs.Combine(moviesRoot, namer.MovieFolder(name, year));
        var files = new List<ImportFileOp>();
        var claimed = new HashSet<string>(StringComparer.Ordinal);

        foreach (MediaEntry media in CollectMedia(fs, source, mediaExtensions))
        {
            (string? language, string extension) = SubtitleName.Split(media.Name);
            string targetName = namer.MovieFile(name, year, extension, language);
            string targetPath = fs.Combine(movieFolder, targetName);
            files.Add(Classify(fs, media, targetName, targetPath, claimed));
        }

        return new ImportPlan([movieFolder], files, movieFolder);
    }

    /// <summary>Plans a show import from <paramref name="source"/>, foldering episodes by parsed season.</summary>
    public static ImportPlan PlanShow(
        IMediaFileSystem fs, string source, string showsRoot, PlexNamer namer,
        string name, IReadOnlySet<string> mediaExtensions)
    {
        string showFolder = fs.Combine(showsRoot, namer.ShowFolder(name));
        var files = new List<ImportFileOp>();
        var seasonDirs = new HashSet<string>(StringComparer.Ordinal);
        var claimed = new HashSet<string>(StringComparer.Ordinal);

        foreach (MediaEntry media in CollectMedia(fs, source, mediaExtensions))
        {
            SeasonEpisode? se = EpisodeParser.Parse(media.Name);
            if (se is null)
            {
                files.Add(new ImportFileOp(media.FullPath, media.Name, null, null, ImportStatus.NoEpisodePattern));
                continue;
            }

            string seasonFolder = fs.Combine(showFolder, namer.SeasonFolder(se.Value.Season));
            seasonDirs.Add(seasonFolder);

            (string? language, string extension) = SubtitleName.Split(media.Name);
            string targetName = namer.EpisodeFile(name, se.Value.Season, se.Value.Episode, extension, language);
            string targetPath = fs.Combine(seasonFolder, targetName);
            files.Add(Classify(fs, media, targetName, targetPath, claimed));
        }

        return new ImportPlan(seasonDirs.ToList(), files, showFolder);
    }

    private static ImportFileOp Classify(
        IMediaFileSystem fs, MediaEntry media, string targetName, string targetPath, HashSet<string> claimed)
    {
        bool collides = fs.FileExists(targetPath) || fs.DirectoryExists(targetPath) || !claimed.Add(targetPath);
        return new ImportFileOp(
            media.FullPath, media.Name, targetPath, targetName,
            collides ? ImportStatus.Collision : ImportStatus.WillMove);
    }

    /// <summary>Collects media files under <paramref name="source"/> (recursing folders; a single file is taken directly).</summary>
    private static List<MediaEntry> CollectMedia(IMediaFileSystem fs, string source, IReadOnlySet<string> mediaExtensions)
    {
        var result = new List<MediaEntry>();

        if (fs.DirectoryExists(source))
            Walk(fs, source, mediaExtensions, result);
        else if (fs.FileExists(source) && IsMedia(Leaf(source), mediaExtensions))
            result.Add(new MediaEntry(Leaf(source), source, MediaEntryKind.File, 0, default));

        return result.OrderBy(e => e.FullPath, StringComparer.OrdinalIgnoreCase).ToList();
    }

    private static void Walk(IMediaFileSystem fs, string dir, IReadOnlySet<string> mediaExtensions, List<MediaEntry> into)
    {
        foreach (MediaEntry entry in fs.List(dir))
        {
            if (entry.IsDirectory)
                Walk(fs, entry.FullPath, mediaExtensions, into);
            else if (IsMedia(entry.Name, mediaExtensions))
                into.Add(entry);
        }
    }

    private static bool IsMedia(string name, IReadOnlySet<string> mediaExtensions)
    {
        int dot = name.LastIndexOf('.');
        return dot >= 0 && mediaExtensions.Contains(name[dot..]);
    }

    private static string Leaf(string path) => path.Split('/', '\\').Last();
}
