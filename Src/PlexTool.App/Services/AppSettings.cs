namespace PlexTool.App.Services;

/// <summary>How PlexTool authenticates the SSH connection to the media server.</summary>
public enum SshAuthMethod
{
    /// <summary>Private key file (recommended). No password stored; a key passphrase, if any, is a secret.</summary>
    PrivateKey,

    /// <summary>Username + password. The password is a secret (DPAPI-encrypted, never in settings.json).</summary>
    Password,
}

/// <summary>How episode/movie files are named when created or normalized.</summary>
public enum NamingScheme
{
    /// <summary>Plex recommended: "Show Name - S01E01" / "Movie Name (Year)". Uppercase, dash-separated.</summary>
    PlexRecommended,

    /// <summary>The legacy PowerShell-script form: "Show Name s01e01" / "Movie Name (Year)". Lowercase, space.</summary>
    ScriptLegacy,
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

    // ---- Remote library layout ----

    /// <summary>Absolute path on the server to the Movies library root (e.g. "/srv/plex/movies").</summary>
    public string RemoteMoviesPath { get; init; } = "";

    /// <summary>Absolute path on the server to the TV Shows library root (e.g. "/srv/plex/shows").</summary>
    public string RemoteShowsPath { get; init; } = "";

    // ---- Plex server (for scan triggers) ----

    /// <summary>Plex base URL (e.g. "http://10.10.0.220:32400"). Empty until configured.</summary>
    public string PlexBaseUrl { get; init; } = "";

    /// <summary>The Plex library section id mapped to Movies (from /library/sections). Null until mapped.</summary>
    public string? PlexMoviesSectionId { get; init; }

    /// <summary>The Plex library section id mapped to TV Shows. Null until mapped.</summary>
    public string? PlexShowsSectionId { get; init; }

    // ---- Local staging ----

    /// <summary>Local folder new media lands in before import (downloads / staging). Empty until configured.</summary>
    public string LocalStagingPath { get; init; } = "";

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
}
