namespace PlexTool.Core;

/// <summary>
/// The one filesystem abstraction Core plans against. Implemented in PlexTool.App by a local
/// backend (System.IO) and a remote backend (SFTP over SSH.NET), and by an in-memory fake in the
/// test project. Core computes plans by reading through this interface and never performs I/O
/// itself; the App executes the confirmed plan against the same backend.
/// </summary>
/// <remarks>
/// Paths are always in the backend's own convention - Windows (<c>\</c>) for local, POSIX
/// (<c>/</c>) for SFTP. Use <see cref="Combine"/> and <see cref="DirectorySeparator"/> instead of
/// hardcoding a separator so the same planning code builds correct paths on either backend.
/// </remarks>
public interface IMediaFileSystem
{
    /// <summary>A short human label for the backend, e.g. "Local" or "user@10.10.0.220". Never a secret.</summary>
    string Description { get; }

    /// <summary>The path separator this backend uses (<c>\</c> local, <c>/</c> remote).</summary>
    char DirectorySeparator { get; }

    /// <summary>Joins a base path and a child segment with this backend's separator.</summary>
    string Combine(string basePath, string child);

    /// <summary>True if a directory exists at <paramref name="path"/>.</summary>
    bool DirectoryExists(string path);

    /// <summary>True if a file exists at <paramref name="path"/>.</summary>
    bool FileExists(string path);

    /// <summary>Lists the immediate children of a directory (files and subdirectories).</summary>
    IReadOnlyList<MediaEntry> List(string path);

    /// <summary>Creates a directory, including any missing parents. No-op if it already exists.</summary>
    void CreateDirectory(string path);

    /// <summary>
    /// Renames/moves an entry in place. Implementations must not copy-then-delete (an in-place
    /// move keeps the file identity intact so Plex preserves watched state and metadata) and must
    /// not overwrite an existing destination - throw if <paramref name="destination"/> exists, so
    /// callers detect and skip collisions rather than clobbering.
    /// </summary>
    void Move(string source, string destination);

    /// <summary>Deletes a file, or an empty directory.</summary>
    void Delete(string path);

    /// <summary>Opens a stream to read an existing file.</summary>
    Stream OpenRead(string path);

    /// <summary>Opens (creating/truncating) a stream to write a file.</summary>
    Stream OpenWrite(string path);
}
