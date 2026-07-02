using PlexTool.Core.Naming;

namespace PlexTool.Core.Planning;

/// <summary>One folder in a structure plan, and whether it already exists on the target.</summary>
public sealed record FolderPlanItem(string Path, bool AlreadyExists);

/// <summary>
/// Computes the folders needed for a movie or a show, checking each against the target
/// <see cref="IMediaFileSystem"/> so the UI can preview "will create" vs "already exists" before
/// anything is written. Pure: it reads through the filesystem abstraction but performs no writes.
/// </summary>
public static class FolderStructurePlanner
{
    /// <summary>Plans the single folder for a movie: <c>&lt;moviesRoot&gt;/Name (Year)</c>.</summary>
    public static IReadOnlyList<FolderPlanItem> PlanMovie(
        IMediaFileSystem fs, string moviesRoot, PlexNamer namer, string name, int year)
    {
        string folder = fs.Combine(moviesRoot, namer.MovieFolder(name, year));
        return [new FolderPlanItem(folder, fs.DirectoryExists(folder))];
    }

    /// <summary>
    /// Plans the show folder plus <paramref name="seasons"/> season folders:
    /// <c>&lt;showsRoot&gt;/Show</c>, then <c>Show/Season 01 .. Season NN</c>.
    /// </summary>
    public static IReadOnlyList<FolderPlanItem> PlanShow(
        IMediaFileSystem fs, string showsRoot, PlexNamer namer, string name, int seasons)
    {
        if (seasons < 1)
            throw new ArgumentOutOfRangeException(nameof(seasons), "A show needs at least one season.");

        var items = new List<FolderPlanItem>(seasons + 1);

        string showFolder = fs.Combine(showsRoot, namer.ShowFolder(name));
        items.Add(new FolderPlanItem(showFolder, fs.DirectoryExists(showFolder)));

        for (int season = 1; season <= seasons; season++)
        {
            string seasonFolder = fs.Combine(showFolder, namer.SeasonFolder(season));
            items.Add(new FolderPlanItem(seasonFolder, fs.DirectoryExists(seasonFolder)));
        }

        return items;
    }
}
