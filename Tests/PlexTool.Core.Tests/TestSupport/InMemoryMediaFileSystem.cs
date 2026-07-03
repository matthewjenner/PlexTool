using PlexTool.Core;

namespace PlexTool.Core.Tests.TestSupport;

/// <summary>
/// An in-memory <see cref="IMediaFileSystem"/> for testing planners and the cleanup scanner with
/// no real disk or server. POSIX-style ('/') paths, matching the remote backend.
/// </summary>
public sealed class InMemoryMediaFileSystem : IMediaFileSystem
{
    private readonly HashSet<string> _directories = new(StringComparer.Ordinal);
    private readonly HashSet<string> _symlinks = new(StringComparer.Ordinal);
    private readonly Dictionary<string, byte[]> _files = new(StringComparer.Ordinal);
    private readonly Dictionary<string, DateTimeOffset> _mtimes = new(StringComparer.Ordinal);

    public string Description => "InMemory";
    public char DirectorySeparator => '/';

    public string Combine(string basePath, string child) =>
        basePath.TrimEnd('/') + "/" + child.TrimStart('/');

    /// <summary>Test helper: create a directory (and its parents) directly, with an optional mtime.</summary>
    public InMemoryMediaFileSystem AddDirectory(string path, DateTimeOffset? modified = null)
    {
        CreateDirectory(path);
        if (modified is not null)
            _mtimes[Norm(path)] = modified.Value;
        return this;
    }

    /// <summary>Test helper: add a symlinked directory (cleanup should skip it).</summary>
    public InMemoryMediaFileSystem AddSymlinkDir(string path, DateTimeOffset? modified = null)
    {
        CreateDirectory(path);
        _symlinks.Add(Norm(path));
        if (modified is not null)
            _mtimes[Norm(path)] = modified.Value;
        return this;
    }

    /// <summary>Test helper: add a file (creating parent directories), with an optional mtime.</summary>
    public InMemoryMediaFileSystem AddFile(string path, byte[]? content = null, DateTimeOffset? modified = null)
    {
        string? parent = ParentOf(path);
        if (parent is not null)
            CreateDirectory(parent);
        _files[path] = content ?? [];
        _mtimes[path] = modified ?? DateTimeOffset.UnixEpoch;
        return this;
    }

    public bool DirectoryExists(string path) => _directories.Contains(Norm(path));

    public bool FileExists(string path) => _files.ContainsKey(path);

    public IReadOnlyList<MediaEntry> List(string path)
    {
        string prefix = Norm(path) + "/";
        var entries = new List<MediaEntry>();

        foreach (string dir in _directories)
            if (dir.StartsWith(prefix, StringComparison.Ordinal) && !dir[prefix.Length..].Contains('/'))
                entries.Add(new MediaEntry(Leaf(dir), dir, MediaEntryKind.Directory, 0, MTime(dir))
                {
                    IsSymbolicLink = _symlinks.Contains(dir),
                });

        foreach ((string file, byte[] bytes) in _files)
            if (file.StartsWith(prefix, StringComparison.Ordinal) && !file[prefix.Length..].Contains('/'))
                entries.Add(new MediaEntry(Leaf(file), file, MediaEntryKind.File, bytes.Length, MTime(file)));

        return entries;
    }

    public void CreateDirectory(string path)
    {
        // Add the path and every ancestor.
        string current = "";
        foreach (string part in Norm(path).Split('/', StringSplitOptions.RemoveEmptyEntries))
        {
            current += "/" + part;
            _directories.Add(current);
        }
    }

    public void Move(string source, string destination)
    {
        if (FileExists(destination) || DirectoryExists(destination))
            throw new IOException($"Destination already exists: {destination}");

        if (_files.Remove(source, out byte[]? bytes))
        {
            _mtimes.Remove(source, out DateTimeOffset t);
            _files[destination] = bytes;
            _mtimes[destination] = t;
        }
        else if (_directories.Remove(Norm(source)))
        {
            _directories.Add(Norm(destination));
        }
    }

    public void Delete(string path)
    {
        if (_files.Remove(path))
            _mtimes.Remove(path);
        else
            _directories.Remove(Norm(path));
    }

    public Stream OpenRead(string path) => new MemoryStream(_files[path], writable: false);

    public Stream OpenWrite(string path)
    {
        string? parent = ParentOf(path);
        if (parent is not null)
            CreateDirectory(parent);
        _files[path] = [];
        _mtimes[path] = DateTimeOffset.UnixEpoch;
        return new MemoryStream();
    }

    private DateTimeOffset MTime(string path) =>
        _mtimes.TryGetValue(path, out DateTimeOffset t) ? t : DateTimeOffset.UnixEpoch;

    private static string Norm(string path) => "/" + string.Join('/', path.Split('/', StringSplitOptions.RemoveEmptyEntries));

    private static string Leaf(string path) => path[(path.LastIndexOf('/') + 1)..];

    private static string? ParentOf(string path)
    {
        int i = path.LastIndexOf('/');
        return i <= 0 ? null : path[..i];
    }
}
