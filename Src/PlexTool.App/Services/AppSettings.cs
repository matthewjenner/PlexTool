using PlexTool.Core.Naming;

namespace PlexTool.App.Services;

/// <summary>How PlexTool authenticates the SSH connection to the media server.</summary>
public enum SshAuthMethod
{
    /// <summary>Private key file (recommended). No password stored; a key passphrase, if any, is a secret.</summary>
    PrivateKey,

    /// <summary>Username + password. The password is a secret (DPAPI-encrypted, never in settings.json).</summary>
    Password,
}

/// <summary>
/// App-wide, NON-SECRET settings. Persisted to <c>settings.json</c> as plain JSON.
/// Secrets (SSH password / key passphrase, Plex token) never live here - they go through
/// <see cref="SecretStore"/> into the DPAPI-encrypted <c>secrets.dat</c>. Everything on this
/// record is safe to read in a text editor and safe (though pointless) to leak.
/// </summary>
public sealed record AppSettings
{
    // ---- SSH / media server connection (non-secret parts) ----

    /// <summary>Server hostname or IP (e.g. "10.10.0.220"). Empty until configured.</summary>
    public string SshHost { get; init; } = "";

    /// <summary>SSH port. Defaults to 22.</summary>
    public int SshPort { get; init; } = 22;

    /// <summary>SSH username. Empty until configured.</summary>
    public string SshUsername { get; init; } = "";

    /// <summary>Auth method. Key-based is recommended so no password need be stored.</summary>
    public SshAuthMethod SshAuthMethod { get; init; } = SshAuthMethod.PrivateKey;

    /// <summary>Path to the private key file (used when <see cref="SshAuthMethod"/> is PrivateKey).</summary>
    public string SshPrivateKeyPath { get; init; } = "";

    /// <summary>
    /// The server's SSH host-key fingerprint, remembered on first successful connect
    /// (trust-on-first-use). A later mismatch means a possible MITM and is refused.
    /// </summary>
    public string? SshHostKeyFingerprint { get; init; }

    // ---- Media library layout (paths on the STORAGE box, where files are written) ----

    /// <summary>Absolute path on the storage box to the Movies library root (e.g. "/srv/plex-media/movies").</summary>
    public string RemoteMoviesPath { get; init; } = "";

    /// <summary>Absolute path on the storage box to the TV Shows library root (e.g. "/srv/plex-media/shows").</summary>
    public string RemoteShowsPath { get; init; } = "";

    // ---- Topology: is Plex a separate box from the media storage? ----

    /// <summary>
    /// True when Plex runs on a different box than the media storage, mounting the storage at a
    /// different path (split setup). When true, <see cref="StoragePathPrefix"/> is translated to
    /// <see cref="PlexMountPrefix"/> for Plex path-scoped scans. False = unified (same paths).
    /// </summary>
    public bool PlexStorageIsSeparate { get; init; } = true;

    /// <summary>The path prefix on the storage box we write to, e.g. "/srv/plex-media" (split only).</summary>
    public string StoragePathPrefix { get; init; } = "";

    /// <summary>The path prefix the same media appears at on the Plex box, e.g. "/mnt/media" (split only).</summary>
    public string PlexMountPrefix { get; init; } = "";

    // ---- Plex server (for scan triggers) ----

    /// <summary>Plex base URL (e.g. "http://10.10.0.220:32400"). Empty until configured.</summary>
    public string PlexBaseUrl { get; init; } = "";

    /// <summary>The Plex library section id mapped to Movies (from /library/sections). Null until mapped.</summary>
    public string? PlexMoviesSectionId { get; init; }

    /// <summary>The Plex library section id mapped to TV Shows. Null until mapped.</summary>
    public string? PlexShowsSectionId { get; init; }

    // ---- Staging ----

    /// <summary>
    /// The folder where freshly acquired media lands before import, as a path ON THE STORAGE BOX
    /// (e.g. "/srv/plex-media/downloads"). Because it sits on the same filesystem as the library,
    /// import is a fast server-side move (rename), not a network transfer. Empty until configured.
    /// </summary>
    public string StagingPath { get; init; } = "";

    // ---- Naming ----

    /// <summary>Naming convention for created and normalized files. Defaults to Plex recommended.</summary>
    public NamingScheme NamingScheme { get; init; } = NamingScheme.PlexRecommended;

    // ---- Cleanup defaults (mirror the Clean-EmptyFolders.ps1 knobs) ----

    /// <summary>An empty folder must be untouched this many minutes before it is a removal candidate.</summary>
    public int CleanupMinAgeMinutes { get; init; } = 2;

    /// <summary>Folder names skipped anywhere in the tree during cleanup.</summary>
    public IReadOnlyList<string> CleanupExclusions { get; init; } =
        new[] { "System Volume Information", "$RECYCLE.BIN", ".stfolder", "@eaDir" };

    /// <summary>After removing an empty folder, also prune now-empty parents up to (not incl.) the root.</summary>
    public bool CleanupPruneParents { get; init; } = true;

    // ---- Media file classification ----

    /// <summary>Extensions treated as renameable media (video + subtitles). Lower-case, dot-prefixed.</summary>
    public IReadOnlyList<string> MediaExtensions { get; init; } =
        new[] { ".mkv", ".mp4", ".avi", ".mov", ".wmv", ".m4v", ".flv", ".srt", ".ass", ".sub" };

    // ---- Updates ----

    /// <summary>
    /// The semver of a release the user explicitly clicked "skip" on. While that version is the
    /// latest, the update banner stays hidden; a newer release re-arms it.
    /// </summary>
    public string? SkippedUpdateVersion { get; init; }

    /// <summary>
    /// Translates a path on the storage box into the path Plex sees, for path-scoped library
    /// scans. In a unified setup the path is returned unchanged; in a split setup the storage
    /// prefix is swapped for the Plex mount prefix (segment-boundary safe, trailing-slash tolerant).
    /// </summary>
    public string ToPlexPath(string storagePath) =>
        PlexStorageIsSeparate
            ? Core.Paths.PosixPath.TranslatePrefix(storagePath, StoragePathPrefix, PlexMountPrefix)
            : storagePath;
}
