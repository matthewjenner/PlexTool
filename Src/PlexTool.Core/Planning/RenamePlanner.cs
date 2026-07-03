using PlexTool.Core.Naming;

namespace PlexTool.Core.Planning;

/// <summary>What should happen to a media file during a rename pass.</summary>
public enum RenameStatus
{
    /// <summary>The file will be renamed to a new, non-colliding target.</summary>
    WillRename,

    /// <summary>The file already has the correct name - nothing to do.</summary>
    AlreadyCorrect,

    /// <summary>A different file already occupies the target name - skipped to avoid clobbering.</summary>
    Collision,

    /// <summary>A show episode file with no parseable season/episode token - skipped.</summary>
    NoEpisodePattern,
}

/// <summary>One planned rename (or skip). <see cref="TargetName"/>/<see cref="TargetPath"/> are null for skips.</summary>
public sealed record RenameOp(
    string SourcePath,
    string SourceName,
    string? TargetName,
    string? TargetPath,
    RenameStatus Status);

/// <summary>
/// Plans in-place renames toward Plex naming, mirroring Rename-MediaFiles.ps1. Movies: each media
/// file in a movie folder becomes <c>&lt;FolderName&gt;.ext</c> (the folder is the source of truth).
/// Shows: each episode file becomes <c>&lt;Show&gt; - S01E01.ext</c> (via <see cref="PlexNamer"/>),
/// with the season/episode parsed from the existing name. Subtitle language suffixes are preserved.
/// Pure: reads through <see cref="IMediaFileSystem"/>, writes nothing.
/// </summary>
public static class RenamePlanner
{
    /// <summary>Plans renames for every movie folder directly under <paramref name="moviesRoot"/>.</summary>
    public static IReadOnlyList<RenameOp> PlanMovies(
        IMediaFileSystem fs, string moviesRoot, IReadOnlySet<string> mediaExtensions)
    {
        var ops = new List<RenameOp>();

        foreach (MediaEntry movie in Directories(fs, moviesRoot))
        {
            foreach (MediaEntry file in MediaFiles(fs, movie.FullPath, mediaExtensions))
            {
                (string? language, string extension) = SubtitleName.Split(file.Name);
                string suffix = string.IsNullOrEmpty(language) ? "" : "." + language;
                string targetName = movie.Name + suffix + extension;
                ops.Add(Classify(fs, movie.FullPath, file, targetName));
            }
        }

        return ops;
    }

    /// <summary>
    /// Plans renames for every episode file under <paramref name="showsRoot"/> (show folder ->
    /// season folder -> file). Files with no season/episode token are reported as skipped.
    /// </summary>
    public static IReadOnlyList<RenameOp> PlanShows(
        IMediaFileSystem fs, string showsRoot, IReadOnlySet<string> mediaExtensions, PlexNamer namer)
    {
        var ops = new List<RenameOp>();

        foreach (MediaEntry show in Directories(fs, showsRoot))
        {
            foreach (MediaEntry season in Directories(fs, show.FullPath))
            {
                foreach (MediaEntry file in MediaFiles(fs, season.FullPath, mediaExtensions))
                {
                    SeasonEpisode? se = EpisodeParser.Parse(file.Name);
                    if (se is null)
                    {
                        ops.Add(new RenameOp(file.FullPath, file.Name, null, null, RenameStatus.NoEpisodePattern));
                        continue;
                    }

                    (string? language, string extension) = SubtitleName.Split(file.Name);
                    string targetName = namer.EpisodeFile(show.Name, se.Value.Season, se.Value.Episode, extension, language);
                    ops.Add(Classify(fs, season.FullPath, file, targetName));
                }
            }
        }

        return ops;
    }

    private static RenameOp Classify(IMediaFileSystem fs, string directory, MediaEntry file, string targetName)
    {
        if (string.Equals(file.Name, targetName, StringComparison.Ordinal))
            return new RenameOp(file.FullPath, file.Name, targetName, null, RenameStatus.AlreadyCorrect);

        string targetPath = fs.Combine(directory, targetName);
        if (fs.FileExists(targetPath) || fs.DirectoryExists(targetPath))
            return new RenameOp(file.FullPath, file.Name, targetName, targetPath, RenameStatus.Collision);

        return new RenameOp(file.FullPath, file.Name, targetName, targetPath, RenameStatus.WillRename);
    }

    private static IEnumerable<MediaEntry> Directories(IMediaFileSystem fs, string path) =>
        fs.List(path).Where(e => e.IsDirectory).OrderBy(e => e.Name, StringComparer.OrdinalIgnoreCase);

    private static IEnumerable<MediaEntry> MediaFiles(IMediaFileSystem fs, string path, IReadOnlySet<string> mediaExtensions) =>
        fs.List(path)
            .Where(e => e.IsFile && IsMedia(e.Name, mediaExtensions))
            .OrderBy(e => e.Name, StringComparer.OrdinalIgnoreCase);

    private static bool IsMedia(string name, IReadOnlySet<string> mediaExtensions)
    {
        int dot = name.LastIndexOf('.');
        return dot >= 0 && mediaExtensions.Contains(name[dot..]);
    }
}
