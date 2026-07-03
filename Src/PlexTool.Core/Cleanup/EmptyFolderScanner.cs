using System.Text.RegularExpressions;

namespace PlexTool.Core.Cleanup;

/// <summary>
/// Finds empty directories to remove, mirroring Clean-EmptyFolders.ps1. Pure: it reads through
/// <see cref="IMediaFileSystem"/> from a single snapshot and returns the directories to delete,
/// deepest-first, so the caller can delete them in order (children before parents). Rules:
/// <list type="bullet">
///   <item>The root itself is never removed.</item>
///   <item>Symlinked directories are never entered or removed, and their presence keeps a parent
///   from counting as empty.</item>
///   <item>Excluded names (wildcards allowed, case-insensitive) are never removed and block their
///   parent from being removed.</item>
///   <item>A directory must have been untouched for at least <c>minAge</c> to qualify.</item>
///   <item>With <c>pruneParents</c>, a directory whose only contents are removable empty subfolders
///   is itself removable (the cascade up the tree); without it, only already-empty folders qualify.</item>
/// </list>
/// </summary>
public static class EmptyFolderScanner
{
    /// <summary>Returns the directories under <paramref name="root"/> to delete, deepest-first.</summary>
    public static IReadOnlyList<string> FindRemovable(
        IMediaFileSystem fs,
        string root,
        DateTimeOffset nowUtc,
        TimeSpan minAge,
        IReadOnlyCollection<string> exclusions,
        bool pruneParents)
    {
        var toRemove = new List<string>();

        foreach (MediaEntry child in fs.List(root))
            if (child.IsDirectory && !child.IsSymbolicLink)
                Visit(child);

        return toRemove;

        // Post-order walk: a directory is added to toRemove after its removable children, so the
        // list is naturally deepest-first. Returns whether `dir` is itself removable.
        bool Visit(MediaEntry dir)
        {
            IReadOnlyList<MediaEntry> entries = fs.List(dir.FullPath);
            bool hasFiles = entries.Any(e => e.IsFile);
            bool hasSymlink = entries.Any(e => e.IsDirectory && e.IsSymbolicLink);
            List<MediaEntry> subdirs = entries.Where(e => e.IsDirectory && !e.IsSymbolicLink).ToList();

            bool allSubdirsRemovable = true;
            foreach (MediaEntry sub in subdirs)
                if (!Visit(sub))
                    allSubdirsRemovable = false;

            if (IsExcluded(dir.Name, exclusions))
                return false;
            if (hasFiles || hasSymlink)
                return false;

            bool contentGone = pruneParents ? allSubdirsRemovable : subdirs.Count == 0;
            if (!contentGone)
                return false;
            if (nowUtc - dir.LastModifiedUtc < minAge)
                return false;

            toRemove.Add(dir.FullPath);
            return true;
        }
    }

    private static bool IsExcluded(string name, IReadOnlyCollection<string> exclusions) =>
        exclusions.Any(pattern => WildcardMatch(name, pattern));

    /// <summary>Case-insensitive wildcard match supporting <c>*</c> and <c>?</c> (like PowerShell -like).</summary>
    private static bool WildcardMatch(string name, string pattern)
    {
        string regex = "^" + Regex.Escape(pattern).Replace("\\*", ".*").Replace("\\?", ".") + "$";
        return Regex.IsMatch(name, regex, RegexOptions.IgnoreCase);
    }
}
