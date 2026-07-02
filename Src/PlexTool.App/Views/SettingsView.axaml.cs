using Avalonia.Controls;
using Avalonia.Input.Platform;
using Avalonia.Interactivity;
using Avalonia.Platform.Storage;
using PlexTool.App.ViewModels;

namespace PlexTool.App.Views;

public partial class SettingsView : UserControl
{
    public SettingsView() => InitializeComponent();

    // The OS file picker needs a TopLevel, which the View has and the ViewModel does not.
    // The View opens the dialog and writes the chosen path back into the bound VM property.
    private async void BrowseKey(object? sender, RoutedEventArgs e)
    {
        if (DataContext is not SettingsViewModel vm)
            return;
        TopLevel? top = TopLevel.GetTopLevel(this);
        if (top is null)
            return;

        IReadOnlyList<IStorageFile> files = await top.StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions
        {
            Title = "Select SSH private key",
            AllowMultiple = false,
        });

        if (files.Count > 0)
            vm.SshPrivateKeyPath = files[0].Path.LocalPath;
    }

    // Copies the "read the Plex token from Preferences.xml" command to the clipboard so the user
    // can paste it into an SSH session on the Plex server.
    private async void CopyTokenCommand(object? sender, RoutedEventArgs e)
    {
        TopLevel? top = TopLevel.GetTopLevel(this);
        if (top?.Clipboard is null)
            return;
        if (this.FindControl<TextBox>("TokenCommandBox") is { Text: { } command })
            await top.Clipboard.SetTextAsync(command);
    }
}
