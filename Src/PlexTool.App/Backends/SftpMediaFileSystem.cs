using System.Text;
using PlexTool.Core;
using Renci.SshNet;
using Renci.SshNet.Sftp;

namespace PlexTool.App.Backends;

/// <summary>
/// <see cref="IMediaFileSystem"/> over a connected SSH.NET <see cref="SftpClient"/>. Paths are
/// POSIX (the server is Linux). Not thread-safe - SSH.NET clients must be accessed serially.
/// </summary>
/// <remarks>
/// When <paramref name="ownsClient"/> is true the instance disconnects and disposes the client
/// on <see cref="Dispose"/>; pass false when the caller manages the client's lifetime.
/// </remarks>
public sealed class SftpMediaFileSystem(SftpClient client, string description, bool ownsClient = true)
    : IMediaFileSystem, IDisposable
{
    public string Description => description;

    public char DirectorySeparator => '/';

    public string Combine(string basePath, string child) =>
        basePath.TrimEnd('/') + "/" + child.TrimStart('/');

    public bool DirectoryExists(string path) => client.Exists(path) && client.Get(path).IsDirectory;

    public bool FileExists(string path) => client.Exists(path) && client.Get(path).IsRegularFile;

    public IReadOnlyList<MediaEntry> List(string path)
    {
        var entries = new List<MediaEntry>();
        foreach (ISftpFile f in client.ListDirectory(path))
        {
            if (f.Name is "." or "..")
                continue;

            MediaEntryKind kind = f.IsDirectory ? MediaEntryKind.Directory : MediaEntryKind.File;
            long size = f.IsRegularFile ? f.Length : 0;
            entries.Add(new MediaEntry(
                f.Name, f.FullName, kind, size,
                new DateTimeOffset(f.LastWriteTimeUtc, TimeSpan.Zero)));
        }
        return entries;
    }

    public void CreateDirectory(string path)
    {
        // Build each level in turn (SFTP CreateDirectory is single-level). Library paths are
        // absolute POSIX paths (e.g. /srv/plex/movies), so we accumulate from the root.
        var sb = new StringBuilder();
        foreach (string part in path.Split('/', StringSplitOptions.RemoveEmptyEntries))
        {
            sb.Append('/').Append(part);
            string current = sb.ToString();
            if (!client.Exists(current))
                client.CreateDirectory(current);
        }
    }

    public void Move(string source, string destination) => client.RenameFile(source, destination);

    public void Delete(string path)
    {
        if (client.Get(path).IsDirectory)
            client.DeleteDirectory(path);
        else
            client.DeleteFile(path);
    }

    public Stream OpenRead(string path) => client.OpenRead(path);

    public Stream OpenWrite(string path) => client.Create(path);

    public void Dispose()
    {
        if (!ownsClient)
            return;
        try
        {
            if (client.IsConnected)
                client.Disconnect();
        }
        catch
        {
            // Best-effort teardown.
        }
        client.Dispose();
    }
}
