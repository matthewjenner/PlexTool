namespace PlexTool.App.Services;

/// <summary>
/// Composition root. Owns the persisted configuration (non-secret <see cref="AppSettings"/>
/// plus DPAPI-encrypted <see cref="Secrets"/>) and the update poller. ViewModels take an
/// <see cref="AppHost"/> and read/update state through it - they never touch the stores directly.
/// </summary>
public sealed class AppHost : IDisposable
{
    private readonly SettingsStore _settingsStore;
    private readonly SecretStore _secretStore;
    private readonly UpdateService _updates;

    public AppHost()
    {
        _settingsStore = new SettingsStore(AppPaths.SettingsFilePath);
        _secretStore = new SecretStore(AppPaths.SecretsFilePath);

        Settings = _settingsStore.Load();
        Secrets = _secretStore.Load();

        // Built last so it can read Settings.SkippedUpdateVersion and call back into UpdateSettings.
        _updates = new UpdateService(this);
    }

    /// <summary>App-wide, non-secret settings. Update via <see cref="UpdateSettings"/>.</summary>
    public AppSettings Settings { get; private set; }

    /// <summary>
    /// The decrypted secrets, held in memory for the app's lifetime. Update via
    /// <see cref="UpdateSecrets"/>. Never bind these directly into logs or error text.
    /// </summary>
    public Secrets Secrets { get; private set; }

    /// <summary>Raised after <see cref="Settings"/> changes - fired on the caller's thread.</summary>
    public event Action<AppSettings>? SettingsChanged;

    /// <summary>Tracks the latest GitHub release and backs the main window's update banner.</summary>
    public UpdateService Updates => _updates;

    /// <summary>Persists new non-secret settings and notifies listeners.</summary>
    public void UpdateSettings(AppSettings settings)
    {
        Settings = settings;
        _settingsStore.Save(settings);
        SettingsChanged?.Invoke(settings);
    }

    /// <summary>Persists new secrets to the DPAPI-encrypted store and updates the in-memory copy.</summary>
    public void UpdateSecrets(Secrets secrets)
    {
        Secrets = secrets;
        if (OperatingSystem.IsWindows())
            _secretStore.Save(secrets);
    }

    public void Dispose() => _updates.Dispose();
}
