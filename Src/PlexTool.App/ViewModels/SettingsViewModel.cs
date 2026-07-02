using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using PlexTool.App.Backends;
using PlexTool.App.Services;

namespace PlexTool.App.ViewModels;

/// <summary>
/// Backs the Settings tab: SSH connection, remote library paths, Plex connection, local staging,
/// naming, and cleanup defaults. Secrets (SSH password / key passphrase, Plex token) are edited
/// here and saved to the DPAPI-encrypted store; everything else goes to settings.json. The Save
/// button is the single commit point - typing does not persist until Save is pressed.
/// </summary>
public sealed partial class SettingsViewModel : ViewModelBase
{
    private readonly AppHost _host;

    public SettingsViewModel(AppHost host)
    {
        _host = host;
        LoadFrom(host.Settings, host.Secrets);
    }

    // ---- SSH ----
    [ObservableProperty] private string _sshHost = "";
    [ObservableProperty] private int _sshPort = 22;
    [ObservableProperty] private string _sshUsername = "";

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(UsesPrivateKey))]
    [NotifyPropertyChangedFor(nameof(UsesPassword))]
    private SshAuthMethod _sshAuthMethod = SshAuthMethod.PrivateKey;

    [ObservableProperty] private string _sshPrivateKeyPath = "";
    [ObservableProperty] private string _sshKeyPassphrase = "";
    [ObservableProperty] private string _sshPassword = "";
    [ObservableProperty] private string? _sshHostKeyFingerprint;
    [ObservableProperty] private string _sshStatus = "";
    [ObservableProperty] private bool _isSshBusy;

    public bool UsesPrivateKey => SshAuthMethod == SshAuthMethod.PrivateKey;
    public bool UsesPassword => SshAuthMethod == SshAuthMethod.Password;
    public IReadOnlyList<SshAuthMethod> AuthMethods { get; } = Enum.GetValues<SshAuthMethod>();

    // ---- Media library paths (on the storage box) ----
    [ObservableProperty] private string _remoteMoviesPath = "";
    [ObservableProperty] private string _remoteShowsPath = "";

    // ---- Topology: split (Plex separate from storage) vs unified ----
    [ObservableProperty] private bool _plexStorageIsSeparate = true;
    [ObservableProperty] private string _storagePathPrefix = "";
    [ObservableProperty] private string _plexMountPrefix = "";

    // ---- Plex ----
    [ObservableProperty] private string _plexBaseUrl = "";
    [ObservableProperty] private string _plexToken = "";
    [ObservableProperty] private PlexSection? _selectedMoviesSection;
    [ObservableProperty] private PlexSection? _selectedShowsSection;
    [ObservableProperty] private string _plexStatus = "";
    [ObservableProperty] private bool _isPlexBusy;
    public ObservableCollection<PlexSection> PlexSections { get; } = [];

    // ---- Staging (a folder on the storage box) ----
    [ObservableProperty] private string _stagingPath = "";

    // ---- Naming ----
    [ObservableProperty] private NamingScheme _namingScheme = NamingScheme.PlexRecommended;
    public IReadOnlyList<NamingScheme> NamingSchemes { get; } = Enum.GetValues<NamingScheme>();

    // ---- Cleanup defaults ----
    [ObservableProperty] private int _cleanupMinAgeMinutes = 2;
    [ObservableProperty] private bool _cleanupPruneParents = true;
    [ObservableProperty] private string _cleanupExclusionsText = "";
    [ObservableProperty] private string _mediaExtensionsText = "";

    // ---- Save ----
    [ObservableProperty] private string _saveStatus = "";

    /// <summary>When true, secret fields reveal their text instead of showing dots.</summary>
    [ObservableProperty] private bool _showSecrets;

    [RelayCommand]
    private async Task TestSshAsync()
    {
        IsSshBusy = true;
        SshStatus = "Connecting...";
        try
        {
            (AppSettings settings, Secrets secrets) = BuildConfig();
            SshTestResult result = await _host.Ssh.TestAsync(settings, secrets);

            // On a first-ever connect, adopt the learned host key so it is persisted (TOFU).
            if (result.Ok && result.LearnedFingerprint is not null
                && string.IsNullOrWhiteSpace(SshHostKeyFingerprint))
            {
                SshHostKeyFingerprint = result.LearnedFingerprint;
            }

            // Auto-save on a successful connection so the entered values (and the learned host key)
            // survive a restart even if the user never clicks Save.
            if (result.Ok)
            {
                Persist();
                SshStatus = result.Message + " Settings saved.";
            }
            else
            {
                SshStatus = result.Message;
            }
        }
        finally
        {
            IsSshBusy = false;
        }
    }

    /// <summary>Forgets the trusted host key so the next connect re-trusts (use only if the server legitimately changed).</summary>
    [RelayCommand]
    private void ClearHostKey()
    {
        SshHostKeyFingerprint = null;
        SshStatus = "Trusted host key cleared - the next test will trust on first use again.";
    }

    [RelayCommand]
    private async Task LoadPlexSectionsAsync()
    {
        IsPlexBusy = true;
        PlexStatus = "Contacting Plex...";
        try
        {
            PlexResult result = await _host.Plex.GetSectionsAsync(PlexBaseUrl, NullIfBlank(PlexToken));

            PlexSections.Clear();
            foreach (PlexSection section in result.Sections)
                PlexSections.Add(section);

            // Re-select whatever was previously mapped, matching by section key.
            SelectedMoviesSection = Find(_host.Settings.PlexMoviesSectionId);
            SelectedShowsSection = Find(_host.Settings.PlexShowsSectionId);

            // Best-effort auto-map by type when nothing is selected yet.
            SelectedMoviesSection ??= PlexSections.FirstOrDefault(s => s.Type == "movie");
            SelectedShowsSection ??= PlexSections.FirstOrDefault(s => s.Type == "show");

            // Auto-save on a successful load so the Plex config + mapping survive a restart.
            if (result.Ok)
            {
                Persist();
                PlexStatus = result.Message + " Settings saved.";
            }
            else
            {
                PlexStatus = result.Message;
            }
        }
        finally
        {
            IsPlexBusy = false;
        }

        PlexSection? Find(string? key) =>
            key is null ? null : PlexSections.FirstOrDefault(s => s.Key == key);
    }

    [RelayCommand]
    private void Save()
    {
        Persist();
        SaveStatus = $"Saved at {DateTimeOffset.Now:HH:mm:ss}. Secrets are DPAPI-encrypted; settings are in settings.json.";
    }

    /// <summary>Commits the current form to the settings + secret stores. Used by Save and by the auto-save on a successful test.</summary>
    private void Persist()
    {
        (AppSettings settings, Secrets secrets) = BuildConfig();
        _host.UpdateSettings(settings);
        _host.UpdateSecrets(secrets);
    }

    /// <summary>Reads the current form state into a settings + secrets pair.</summary>
    private (AppSettings, Secrets) BuildConfig()
    {
        var settings = new AppSettings
        {
            SshHost = SshHost.Trim(),
            SshPort = SshPort,
            SshUsername = SshUsername.Trim(),
            SshAuthMethod = SshAuthMethod,
            SshPrivateKeyPath = SshPrivateKeyPath.Trim(),
            SshHostKeyFingerprint = string.IsNullOrWhiteSpace(SshHostKeyFingerprint) ? null : SshHostKeyFingerprint,
            RemoteMoviesPath = RemoteMoviesPath.Trim(),
            RemoteShowsPath = RemoteShowsPath.Trim(),
            PlexStorageIsSeparate = PlexStorageIsSeparate,
            StoragePathPrefix = StoragePathPrefix.Trim(),
            PlexMountPrefix = PlexMountPrefix.Trim(),
            PlexBaseUrl = PlexBaseUrl.Trim(),
            // Preserve a previously-saved mapping if the libraries have not been (re)loaded this session.
            PlexMoviesSectionId = SelectedMoviesSection?.Key ?? _host.Settings.PlexMoviesSectionId,
            PlexShowsSectionId = SelectedShowsSection?.Key ?? _host.Settings.PlexShowsSectionId,
            StagingPath = StagingPath.Trim(),
            NamingScheme = NamingScheme,
            CleanupMinAgeMinutes = CleanupMinAgeMinutes,
            CleanupPruneParents = CleanupPruneParents,
            CleanupExclusions = SplitLines(CleanupExclusionsText),
            MediaExtensions = NormalizeExtensions(MediaExtensionsText),
            // Preserve fields the Settings tab does not edit.
            SkippedUpdateVersion = _host.Settings.SkippedUpdateVersion,
        };

        var secrets = new Secrets
        {
            SshPassword = NullIfBlank(SshPassword),
            SshKeyPassphrase = NullIfBlank(SshKeyPassphrase),
            PlexToken = NullIfBlank(PlexToken),
        };

        return (settings, secrets);
    }

    private void LoadFrom(AppSettings s, Secrets sec)
    {
        SshHost = s.SshHost;
        SshPort = s.SshPort;
        SshUsername = s.SshUsername;
        SshAuthMethod = s.SshAuthMethod;
        SshPrivateKeyPath = s.SshPrivateKeyPath;
        SshHostKeyFingerprint = s.SshHostKeyFingerprint;
        RemoteMoviesPath = s.RemoteMoviesPath;
        RemoteShowsPath = s.RemoteShowsPath;
        PlexStorageIsSeparate = s.PlexStorageIsSeparate;
        StoragePathPrefix = s.StoragePathPrefix;
        PlexMountPrefix = s.PlexMountPrefix;
        PlexBaseUrl = s.PlexBaseUrl;
        StagingPath = s.StagingPath;
        NamingScheme = s.NamingScheme;
        CleanupMinAgeMinutes = s.CleanupMinAgeMinutes;
        CleanupPruneParents = s.CleanupPruneParents;
        CleanupExclusionsText = string.Join(Environment.NewLine, s.CleanupExclusions);
        MediaExtensionsText = string.Join(", ", s.MediaExtensions);

        SshPassword = sec.SshPassword ?? "";
        SshKeyPassphrase = sec.SshKeyPassphrase ?? "";
        PlexToken = sec.PlexToken ?? "";
    }

    private static string? NullIfBlank(string? value) =>
        string.IsNullOrWhiteSpace(value) ? null : value;

    private static IReadOnlyList<string> SplitLines(string text) =>
        text.Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

    private static IReadOnlyList<string> NormalizeExtensions(string text)
    {
        return text
            .Split([',', ';', ' ', '\r', '\n'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Select(e => e.StartsWith('.') ? e.ToLowerInvariant() : "." + e.ToLowerInvariant())
            .Distinct()
            .ToList();
    }
}
