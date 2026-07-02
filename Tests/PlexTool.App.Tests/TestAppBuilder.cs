using Avalonia;
using Avalonia.Headless;
using PlexTool.App.Tests;

[assembly: AvaloniaTestApplication(typeof(TestAppBuilder))]

namespace PlexTool.App.Tests;

/// <summary>Headless Avalonia app used by [AvaloniaFact] layout tests.</summary>
public static class TestAppBuilder
{
    public static AppBuilder BuildAvaloniaApp() =>
        AppBuilder.Configure<PlexTool.App.App>()
            .UseHeadless(new AvaloniaHeadlessPlatformOptions());
}
