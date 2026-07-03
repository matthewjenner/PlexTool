using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Platform.Storage;
using PlexTool.App.ViewModels;

namespace PlexTool.App.Views;

public partial class RenameView : UserControl
{
    public RenameView() => InitializeComponent();

    private async void BrowseLocalRoot(object? sender, RoutedEventArgs e)
    {
        if (DataContext is not RenameViewModel vm)
            return;
        TopLevel? top = TopLevel.GetTopLevel(this);
        if (top is null)
            return;

        IReadOnlyList<IStorageFolder> folders = await top.StorageProvider.OpenFolderPickerAsync(new FolderPickerOpenOptions
        {
            Title = "Select the library root to scan",
            AllowMultiple = false,
        });

        if (folders.Count > 0)
            vm.LocalRoot = folders[0].Path.LocalPath;
    }
}
