using System.Runtime.Versioning;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace PlexTool.App.Services;

/// <summary>
/// Reads and writes <see cref="Secrets"/> as a DPAPI-encrypted blob (<c>secrets.dat</c>).
/// </summary>
/// <remarks>
/// Encryption uses Windows DPAPI with <see cref="DataProtectionScope.CurrentUser"/>: the
/// ciphertext can only be decrypted by the same Windows user account on the same machine.
/// A leaked, synced, or backed-up copy of the file is useless anywhere else. The plaintext
/// secrets exist only transiently in memory while a connection is being made and are never
/// logged, surfaced in exceptions, or written to <c>settings.json</c>.
///
/// PlexTool is Windows-only, so DPAPI is always available; the methods are guarded with
/// <see cref="SupportedOSPlatformAttribute"/> and degrade to empty/no-op off Windows.
/// </remarks>
public sealed class SecretStore(string filePath)
{
    // Extra entropy mixed into DPAPI so another app running as the same user cannot trivially
    // decrypt our blob just by pointing DPAPI at the bytes.
    private static readonly byte[] Entropy = Encoding.UTF8.GetBytes("PlexTool.Secrets.v1");

    private static readonly JsonSerializerOptions Options = new() { WriteIndented = false };

    /// <summary>Decrypts and returns the stored secrets, or an empty record if none/unreadable.</summary>
    public Secrets Load()
    {
        if (!OperatingSystem.IsWindows())
            return new Secrets();

        try
        {
            if (!File.Exists(filePath))
                return new Secrets();

            byte[] cipher = File.ReadAllBytes(filePath);
            byte[] plain = ProtectedData.Unprotect(cipher, Entropy, DataProtectionScope.CurrentUser);
            return JsonSerializer.Deserialize<Secrets>(plain, Options) ?? new Secrets();
        }
        catch
        {
            // Corrupt blob, wrong user, tampered file: treat as "no secrets" rather than crash.
            return new Secrets();
        }
    }

    /// <summary>Encrypts and writes the secrets. No-op off Windows.</summary>
    [SupportedOSPlatform("windows")]
    public void Save(Secrets secrets)
    {
        string? directory = Path.GetDirectoryName(filePath);
        if (!string.IsNullOrEmpty(directory))
            Directory.CreateDirectory(directory);

        byte[] plain = JsonSerializer.SerializeToUtf8Bytes(secrets, Options);
        byte[] cipher = ProtectedData.Protect(plain, Entropy, DataProtectionScope.CurrentUser);
        File.WriteAllBytes(filePath, cipher);

        // Best-effort scrub of the plaintext buffer once it is encrypted.
        Array.Clear(plain);
    }

    /// <summary>Deletes the secret blob entirely.</summary>
    public void Clear()
    {
        try
        {
            if (File.Exists(filePath))
                File.Delete(filePath);
        }
        catch
        {
            // Non-fatal: nothing we can do if the file is locked.
        }
    }
}
