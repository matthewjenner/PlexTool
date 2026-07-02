namespace PlexTool.App.Services;

/// <summary>
/// Resolves the per-user directory and file paths PlexTool persists to. PlexTool is a
/// Windows-only app (it drives an Ubuntu server remotely over SSH), so this always resolves
/// under <c>%APPDATA%\PlexTool</c> - an ACL'd, per-user location outside the repo.
/// </summary>
public static class AppPaths
{
    /// <summary>Base config directory: <c>%APPDATA%\PlexTool</c>.</summary>
    public static string BaseDirectory
    {
        get
        {
            string appData = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
            return Path.Combine(appData, "PlexTool");
        }
    }

    /// <summary>Non-secret settings: <c>settings.json</c> (hand-editable, no credentials).</summary>
    public static string SettingsFilePath => Path.Combine(BaseDirectory, "settings.json");

    /// <summary>
    /// DPAPI-encrypted secret blob: <c>secrets.dat</c>. Encrypted for the current Windows user,
    /// so a copied or leaked file is undecryptable on any other account or machine.
    /// </summary>
    public static string SecretsFilePath => Path.Combine(BaseDirectory, "secrets.dat");
}
