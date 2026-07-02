using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Markup.Xaml;
using PlexTool.App.Services;
using PlexTool.App.ViewModels;
using PlexTool.App.Views;

namespace PlexTool.App;

public partial class App : Application
{
    private AppHost? _host;

    public override void Initialize() => AvaloniaXamlLoader.Load(this);

    public override void OnFrameworkInitializationCompleted()
    {
        if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
        {
            _host = new AppHost();

            desktop.MainWindow = new MainWindow
            {
                DataContext = new MainWindowViewModel(_host),
            };

            desktop.ShutdownRequested += (_, _) => _host?.Dispose();
        }

        base.OnFrameworkInitializationCompleted();
    }
}
