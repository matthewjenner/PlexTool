namespace PlexTool.Core.Paths;

/// <summary>
/// Pure helpers for POSIX (Linux) path handling on the remote storage box. Kept in Core and free
/// of I/O so the tricky bits - trailing slashes, prefix-boundary matching, and segment safety
/// (traversal / injection) - are fully unit-tested. The server is always Linux, so the separator
/// is always '/'. Local Windows paths are handled by System.IO in the App layer, not here.
/// </summary>
public static class PosixPath
{
    /// <summary>
    /// Trims surrounding whitespace, collapses runs of '/', and removes a trailing slash (keeping
    /// the root "/"). Whitespace inside a segment is preserved (it is legal in a filename).
    /// Returns "" for null/blank input.
    /// </summary>
    public static string Normalize(string? path)
    {
        if (string.IsNullOrWhiteSpace(path))
            return "";

        string p = path.Trim();
        while (p.Contains("//"))
            p = p.Replace("//", "/");

        if (p.Length > 1)
            p = p.TrimEnd('/');

        return p;
    }

    /// <summary>Joins a base path and a child segment with a single '/', tolerant of stray slashes and whitespace.</summary>
    public static string Combine(string? basePath, string? child)
    {
        string b = Normalize(basePath);
        string c = Normalize(child).TrimStart('/');

        if (b.Length == 0) return c;
        if (c.Length == 0) return b;
        return b + "/" + c;
    }

    /// <summary>
    /// Rebases <paramref name="path"/> from under <paramref name="fromPrefix"/> to under
    /// <paramref name="toPrefix"/>. Matching is on a full path-segment boundary, so
    /// "/srv/plex-media" rebases "/srv/plex-media/x" but NOT "/srv/plex-media-extra/x". If either
    /// prefix is blank or the path is not under <paramref name="fromPrefix"/>, the normalized path
    /// is returned unchanged.
    /// </summary>
    public static string TranslatePrefix(string? path, string? fromPrefix, string? toPrefix)
    {
        string p = Normalize(path);
        string from = Normalize(fromPrefix);
        string to = Normalize(toPrefix);

        if (from.Length == 0 || to.Length == 0)
            return p;

        if (string.Equals(p, from, StringComparison.Ordinal))
            return to;

        if (p.StartsWith(from + "/", StringComparison.Ordinal))
            return to + p[from.Length..];

        return p;
    }

    /// <summary>
    /// True if <paramref name="name"/> is safe to use as a single path segment (a folder or file
    /// name). Rejects the traversal names "." and "..", anything containing a '/' separator, control
    /// characters (including NUL), leading/trailing whitespace, and blank input. This is the guard
    /// against path traversal and path/command injection via crafted media titles - callers build
    /// remote paths only from names that pass this check.
    /// </summary>
    public static bool IsSafeSegment(string? name)
    {
        if (string.IsNullOrWhiteSpace(name))
            return false;
        if (name is "." or "..")
            return false;
        if (name.Contains('/'))
            return false;
        if (!string.Equals(name, name.Trim(), StringComparison.Ordinal))
            return false;
        foreach (char c in name)
            if (char.IsControl(c))
                return false;
        return true;
    }

    /// <summary>True if any segment of the path is a "." or ".." traversal token.</summary>
    public static bool ContainsTraversal(string? path)
    {
        string p = Normalize(path);
        if (p.Length == 0)
            return false;
        foreach (string segment in p.Split('/', StringSplitOptions.RemoveEmptyEntries))
            if (segment is "." or "..")
                return true;
        return false;
    }
}
