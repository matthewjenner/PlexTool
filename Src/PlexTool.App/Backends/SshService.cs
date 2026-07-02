using System.Net.Sockets;
using System.Security.Cryptography;
using PlexTool.App.Services;
using Renci.SshNet;
using Renci.SshNet.Common;

namespace PlexTool.App.Backends;

/// <summary>Outcome of an SSH connection test.</summary>
/// <param name="Ok">True if the connection (and auth, and host-key check) succeeded.</param>
/// <param name="Message">A user-facing, secret-free status line.</param>
/// <param name="LearnedFingerprint">
/// The server's SHA-256 host-key fingerprint observed during the attempt. On a first-ever connect
/// this should be saved to settings so later connects can detect a change.
/// </param>
/// <param name="FingerprintMismatch">True if the server's host key differs from the trusted one.</param>
public sealed record SshTestResult(bool Ok, string Message, string? LearnedFingerprint, bool FingerprintMismatch);

/// <summary>
/// Connects to the media server over SSH/SFTP. Owns the auth wiring (key or password) and the
/// host-key trust-on-first-use check. Takes settings and secrets as explicit arguments so the
/// UI can test values the user has typed but not yet saved. Never logs or returns secrets.
/// </summary>
public sealed class SshService
{
    /// <summary>
    /// Attempts to connect with the given settings/secrets, verifies the host key against
    /// <see cref="AppSettings.SshHostKeyFingerprint"/> (trust-on-first-use), and optionally counts
    /// entries in the remote Movies path as a liveness check. Runs off the UI thread.
    /// </summary>
    public Task<SshTestResult> TestAsync(AppSettings settings, Secrets secrets, CancellationToken ct = default)
        => Task.Run(() =>
        {
            string? learned = null;
            bool mismatch = false;

            try
            {
                ConnectionInfo info = BuildConnectionInfo(settings, secrets);
                using var client = new SftpClient(info);

                client.HostKeyReceived += (_, e) =>
                {
                    learned = Fingerprint(e.HostKey);
                    string? trusted = string.IsNullOrWhiteSpace(settings.SshHostKeyFingerprint)
                        ? null
                        : settings.SshHostKeyFingerprint;

                    if (trusted is null || string.Equals(trusted, learned, StringComparison.Ordinal))
                    {
                        e.CanTrust = true;
                    }
                    else
                    {
                        e.CanTrust = false;
                        mismatch = true;
                    }
                };

                client.Connect();

                string extra = "";
                if (!string.IsNullOrWhiteSpace(settings.RemoteMoviesPath) && client.Exists(settings.RemoteMoviesPath))
                {
                    int count = 0;
                    foreach (var f in client.ListDirectory(settings.RemoteMoviesPath))
                        if (f.Name is not ("." or ".."))
                            count++;
                    extra = $" Movies path reachable ({count} entries).";
                }

                client.Disconnect();

                string trustNote = string.IsNullOrWhiteSpace(settings.SshHostKeyFingerprint)
                    ? " Host key trusted on first use."
                    : "";
                return new SshTestResult(true, "Connected successfully." + trustNote + extra, learned, false);
            }
            catch (Exception) when (mismatch)
            {
                return new SshTestResult(
                    false,
                    "Host key does NOT match the trusted fingerprint - connection refused. This can mean the "
                    + "server changed, or a possible man-in-the-middle. Clear the saved fingerprint in settings "
                    + "only if you are certain the server legitimately changed.",
                    learned,
                    true);
            }
            catch (SshAuthenticationException)
            {
                return new SshTestResult(false, "Authentication failed - check the username, key, or password.", null, false);
            }
            catch (SocketException)
            {
                return new SshTestResult(false, "Could not reach the server - check the host and port.", null, false);
            }
            catch (SshConnectionException)
            {
                return new SshTestResult(false, "Could not establish an SSH connection - check the host and port.", null, false);
            }
            catch (Exception ex)
            {
                // Fallback: exception messages from SSH.NET do not contain the password/key, so this
                // is safe to surface, but keep it terse.
                return new SshTestResult(false, "Connection failed: " + ex.Message, null, false);
            }
        }, ct);

    /// <summary>
    /// Connects and returns an <see cref="SftpMediaFileSystem"/> backed by a live client. The
    /// caller owns the returned backend and must dispose it (which disconnects). Used by later
    /// phases (structure, rename, import, cleanup) once the connection is configured and trusted.
    /// </summary>
    public Task<SftpMediaFileSystem> ConnectAsync(AppSettings settings, Secrets secrets, CancellationToken ct = default)
        => Task.Run(() =>
        {
            ConnectionInfo info = BuildConnectionInfo(settings, secrets);
            var client = new SftpClient(info);

            client.HostKeyReceived += (_, e) =>
            {
                string actual = Fingerprint(e.HostKey);
                string? trusted = string.IsNullOrWhiteSpace(settings.SshHostKeyFingerprint)
                    ? null
                    : settings.SshHostKeyFingerprint;
                e.CanTrust = trusted is null || string.Equals(trusted, actual, StringComparison.Ordinal);
            };

            client.Connect();
            return new SftpMediaFileSystem(client, $"{settings.SshUsername}@{settings.SshHost}");
        }, ct);

    private static ConnectionInfo BuildConnectionInfo(AppSettings settings, Secrets secrets)
    {
        AuthenticationMethod auth;
        if (settings.SshAuthMethod == SshAuthMethod.PrivateKey)
        {
            PrivateKeyFile key = string.IsNullOrEmpty(secrets.SshKeyPassphrase)
                ? new PrivateKeyFile(settings.SshPrivateKeyPath)
                : new PrivateKeyFile(settings.SshPrivateKeyPath, secrets.SshKeyPassphrase);
            auth = new PrivateKeyAuthenticationMethod(settings.SshUsername, key);
        }
        else
        {
            auth = new PasswordAuthenticationMethod(settings.SshUsername, secrets.SshPassword ?? "");
        }

        return new ConnectionInfo(settings.SshHost, settings.SshPort, settings.SshUsername, auth);
    }

    /// <summary>SHA-256 host-key fingerprint in the familiar "SHA256:base64" form (no padding).</summary>
    private static string Fingerprint(byte[] hostKey) =>
        "SHA256:" + Convert.ToBase64String(SHA256.HashData(hostKey)).TrimEnd('=');
}
