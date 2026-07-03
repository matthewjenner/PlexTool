using PlexTool.Core;

namespace PlexTool.App.Backends;

/// <summary>
/// <see cref="IMediaFileSystem"/> over the local Windows filesystem via System.IO. Used for the
/// staging folder and for operating on locally-mounted paths.
/// </summary>
public sealed class LocalMediaFileSystem : IMediaFileSystem
{
    public string Description => "Local";

    public char DirectorySeparator => Path.DirectorySeparatorChar;

    public string Combine(string basePath, string child) => Path.Combine(basePath, child);

    public bool DirectoryExists(string path) => Directory.Exists(path);

    public bool FileExists(string path) => File.Exists(path);

    public IReadOnlyList<MediaEntry> List(string path)
    {
        var entries = new List<MediaEntry>();

        foreach (string dir in Directory.EnumerateDirectories(path))
        {
            var info = new DirectoryInfo(dir);
            entries.Add(new MediaEntry(
                info.Name, info.FullName, MediaEntryKind.Directory, 0,
                new DateTimeOffset(info.LastWriteTimeUtc, TimeSpan.Zero))
            {
                IsSymbolicLink = info.Attributes.HasFlag(FileAttributes.ReparsePoint),
            });
        }

        foreach (string file in Directory.EnumerateFiles(path))
        {
            var info = new FileInfo(file);
            entries.Add(new MediaEntry(
                info.Name, info.FullName, MediaEntryKind.File, info.Length,
                new DateTimeOffset(info.LastWriteTimeUtc, TimeSpan.Zero))
            {
                IsSymbolicLink = info.Attributes.HasFlag(FileAttributes.ReparsePoint),
            });
        }

        return entries;
    }

    public void CreateDirectory(string path) => Directory.CreateDirectory(path);

    public void Move(string source, string destination)
    {
        if (Directory.Exists(source))
            Directory.Move(source, destination);
        else
            File.Move(source, destination, overwrite: false);
    }

    public void Delete(string path)
    {
        if (Directory.Exists(path))
            Directory.Delete(path, recursive: false);
        else
            File.Delete(path);
    }

    public Stream OpenRead(string path) => File.OpenRead(path);

    public Stream OpenWrite(string path) => File.Create(path);
}
