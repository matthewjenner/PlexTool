using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using PlexTool.App.Services;

namespace PlexTool.App.ViewModels;

/// <summary>
/// Backs the main window: the update banner state plus the window title. Tab content view
/// models are added here as each phase lands (Settings first in P1).
/// </summary>
public sealed partial class MainWindowViewModel : ViewModelBase
{
    private readonly AppHost _host;

    public MainWindowViewModel(AppHost host)
    {
        _host = host;
        Settings = new SettingsViewModel(host);
        NewStructure = new NewStructureViewModel(host);

        _host.Updates.UpdateAvailableChanged += OnUpdateAvailableChanged;
        AvailableUpdateVersion = _host.Updates.AvailableVersion;
    }

    /// <summary>Window title, e.g. "PlexTool v0.1.0".</summary>
    public string Title => $"PlexTool v{AppVersion.Display}";

    /// <summary>Backs the Settings tab (SSH, Plex, paths, naming, cleanup defaults, secrets).</summary>
    public SettingsViewModel Settings { get; }

    /// <summary>Backs the New Structure tab (create movie/show folders locally or on the server).</summary>
    public NewStructureViewModel NewStructure { get; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsUpdateBannerVisible))]
    [NotifyPropertyChangedFor(nameof(UpdateBannerText))]
    private string? _availableUpdateVersion;

    /// <summary>Whether the "an update is available" banner shows.</summary>
    public bool IsUpdateBannerVisible => AvailableUpdateVersion is not null;

    /// <summary>Banner copy naming the available version.</summary>
    public string UpdateBannerText =>
        AvailableUpdateVersion is null ? "" : $"PlexTool {AvailableUpdateVersion} is available.";

    /// <summary>
    /// The Install button is enabled only for installed (Velopack) builds. Under <c>dotnet run</c>
    /// the banner still shows for UI testing, but installing is a no-op so the button is disabled.
    /// </summary>
    public bool CanInstallUpdate => _host.Updates.CanInstall;

    [RelayCommand]
    private async Task InstallUpdateAsync() => await _host.Updates.InstallAndRestartAsync();

    [RelayCommand]
    private void SkipUpdate() => _host.Updates.SkipCurrentVersion();

    [RelayCommand]
    private void DismissUpdate() => _host.Updates.DismissForNow();

    private void OnUpdateAvailableChanged(string? version)
    {
        AvailableUpdateVersion = version;
        OnPropertyChanged(nameof(CanInstallUpdate));
    }
}
