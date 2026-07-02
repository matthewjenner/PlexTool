namespace PlexTool.Core;

/// <summary>Whether a <see cref="MediaEntry"/> is a file or a directory.</summary>
public enum MediaEntryKind
{
    File,
    Directory,
}

/// <summary>
/// One entry (file or directory) as seen through an <see cref="IMediaFileSystem"/>. Deliberately
/// backend-neutral: the same shape describes a local path and a remote SFTP path, so Core logic
/// (planners, the cleanup scanner) never needs to know which backend produced it.
/// </summary>
/// <param name="Name">The leaf name, e.g. "Dexter s01e01.mkv".</param>
/// <param name="FullPath">The full path in the backend's own convention (Windows or POSIX).</param>
/// <param name="Kind">File or directory.</param>
/// <param name="Size">Size in bytes for files; 0 for directories.</param>
/// <param name="LastModifiedUtc">Last-write time in UTC (used by the cleanup min-age gate).</param>
public sealed record MediaEntry(
    string Name,
    string FullPath,
    MediaEntryKind Kind,
    long Size,
    DateTimeOffset LastModifiedUtc)
{
    public bool IsDirectory => Kind == MediaEntryKind.Directory;
    public bool IsFile => Kind == MediaEntryKind.File;
}
