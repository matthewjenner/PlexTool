using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.Threading;
using Avalonia.VisualTree;
using FluentAssertions;
using PlexTool.App.Services;
using PlexTool.App.ViewModels;
using PlexTool.App.Views;

namespace PlexTool.App.Tests;

/// <summary>
/// Verifies the Settings tab layout: the sticky Save footer must never overlap the scrolling
/// content above it, at a window small enough that the content overflows and must scroll.
/// </summary>
public class SettingsLayoutTests(ITestOutputHelper output)
{
    [AvaloniaTheory]
    [InlineData(760, 500)]
    [InlineData(960, 640)]
    [InlineData(950, 1040)]
    [InlineData(1280, 720)]
    public void Save_footer_does_not_overlap_scroll_content(double width, double height)
    {
        using var host = new AppHost();
        var window = new MainWindow
        {
            DataContext = new MainWindowViewModel(host),
            Width = width,
            Height = height,
        };
        window.Show();

        // Select the Settings tab so its content is realized (by header, not a brittle index).
        TabControl tabs = window.GetVisualDescendants().OfType<TabControl>().First();
        tabs.SelectedItem = tabs.Items.OfType<TabItem>().First(t => (t.Header as string) == "Settings");
        Dispatcher.UIThread.RunJobs();

        ScrollViewer scroll = window.GetVisualDescendants().OfType<ScrollViewer>()
            .First(s => s.Name == "SettingsScroll");
        Border footer = window.GetVisualDescendants().OfType<Border>()
            .First(b => b.Name == "SaveFooter");
        TextBox lastField = window.GetVisualDescendants().OfType<TextBox>()
            .First(t => t.Name == "MediaExtensionsBox");

        // Scroll to the very bottom, exactly like the user does.
        scroll.Offset = scroll.Offset.WithY(scroll.Extent.Height - scroll.Viewport.Height);
        Dispatcher.UIThread.RunJobs();

        // Where does the last field's bottom land in window coordinates, and where is the footer top?
        double lastFieldBottom = lastField.TranslatePoint(new Point(0, lastField.Bounds.Height), window)!.Value.Y;
        double footerTop = footer.TranslatePoint(new Point(0, 0), window)!.Value.Y;

        output.WriteLine($"window {window.Bounds} scroll {scroll.Bounds} viewport {scroll.Viewport} extent {scroll.Extent} offset {scroll.Offset}");
        output.WriteLine($"last field bottom (window Y): {lastFieldBottom}   footer top (window Y): {footerTop}");

        // Bounds sanity: footer sits below the scroll area.
        footer.Bounds.Top.Should().BeGreaterThanOrEqualTo(scroll.Bounds.Bottom - 0.5);
        scroll.Extent.Height.Should().BeGreaterThan(scroll.Viewport.Height);

        // The real check: after scrolling to the end, the last field must be fully visible ABOVE the
        // footer - its bottom edge must not be hidden behind (below) the footer's top edge.
        lastFieldBottom.Should().BeLessThanOrEqualTo(footerTop + 0.5,
            "after scrolling to the bottom, the last field must be visible above the footer, not hidden behind it");
    }
}
