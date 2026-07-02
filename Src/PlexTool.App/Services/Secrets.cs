namespace PlexTool.App.Services;

/// <summary>
/// The sensitive half of the configuration - the values that must never be written in the
/// clear. Persisted only via <see cref="SecretStore"/> (DPAPI-encrypted). Kept separate from
/// <see cref="AppSettings"/> so there is exactly one place secrets can live on disk.
/// </summary>
public sealed record Secrets
{
    /// <summary>SSH password, when password auth is used. Prefer key auth and leave this null.</summary>
    public string? SshPassword { get; init; }

    /// <summary>Passphrase protecting the SSH private key, if the key has one.</summary>
    public string? SshKeyPassphrase { get; init; }

    /// <summary>The Plex "X-Plex-Token" used to trigger library scans.</summary>
    public string? PlexToken { get; init; }
}
