using FluentAssertions;
using PlexTool.Core.Cleanup;
using PlexTool.Core.Tests.TestSupport;

namespace PlexTool.Core.Tests;

public class EmptyFolderScannerTests
{
    private static readonly DateTimeOffset Now = new(2026, 7, 3, 12, 0, 0, TimeSpan.Zero);
    private static readonly DateTimeOffset Old = Now.AddHours(-1);   // safely past any min-age
    private static readonly TimeSpan MinAge = TimeSpan.FromMinutes(2);
    private static readonly string[] NoExclusions = [];

    private static IReadOnlyList<string> Find(InMemoryMediaFileSystem fs, bool pruneParents = false, string[]? exclusions = null) =>
        EmptyFolderScanner.FindRemovable(fs, "/root", Now, MinAge, exclusions ?? NoExclusions, pruneParents);

    [Fact]
    public void Finds_an_empty_directory()
    {
        var fs = new InMemoryMediaFileSystem().AddDirectory("/root/empty", Old);
        Find(fs).Should().Equal("/root/empty");
    }

    [Fact]
    public void Ignores_a_directory_that_contains_a_file()
    {
        var fs = new InMemoryMediaFileSystem().AddFile("/root/full/movie.mkv", modified: Old);
        Find(fs).Should().BeEmpty();
    }

    [Fact]
    public void Respects_the_min_age_gate()
    {
        var fs = new InMemoryMediaFileSystem().AddDirectory("/root/fresh", Now.AddSeconds(-30)); // 30s < 2min
        Find(fs).Should().BeEmpty();
    }

    [Fact]
    public void Never_removes_the_root()
    {
        var fs = new InMemoryMediaFileSystem().AddDirectory("/root", Old);
        Find(fs).Should().BeEmpty();
    }

    [Fact]
    public void Skips_excluded_names_with_wildcards()
    {
        var fs = new InMemoryMediaFileSystem()
            .AddDirectory("/root/@eaDir", Old)
            .AddDirectory("/root/.stfolder", Old)
            .AddDirectory("/root/keep", Old);

        var result = Find(fs, exclusions: ["@eaDir", ".st*"]);

        result.Should().Equal("/root/keep");
    }

    [Fact]
    public void Skips_symlinked_directories()
    {
        var fs = new InMemoryMediaFileSystem().AddSymlinkDir("/root/link", Old);
        Find(fs).Should().BeEmpty();
    }

    [Fact]
    public void A_symlink_inside_a_folder_keeps_that_folder_from_being_removed()
    {
        var fs = new InMemoryMediaFileSystem()
            .AddDirectory("/root/parent", Old)
            .AddSymlinkDir("/root/parent/link", Old);

        Find(fs, pruneParents: true).Should().BeEmpty();
    }

    [Fact]
    public void Without_prune_parents_only_already_empty_folders_are_removed()
    {
        // parent contains an empty child; parent is not itself empty (has a subdir).
        var fs = new InMemoryMediaFileSystem().AddDirectory("/root/parent/child", Old);

        Find(fs, pruneParents: false).Should().Equal("/root/parent/child");
    }

    [Fact]
    public void With_prune_parents_the_cascade_removes_empty_parents_deepest_first()
    {
        var fs = new InMemoryMediaFileSystem().AddDirectory("/root/parent/child", Old);

        var result = Find(fs, pruneParents: true);

        // Deepest-first: child before parent.
        result.Should().Equal("/root/parent/child", "/root/parent");
    }

    [Fact]
    public void Prune_parents_stops_at_a_non_empty_ancestor()
    {
        var fs = new InMemoryMediaFileSystem()
            .AddFile("/root/keep/movie.mkv", modified: Old)      // keep is non-empty
            .AddDirectory("/root/keep/emptychild", Old);

        var result = Find(fs, pruneParents: true);

        result.Should().Equal("/root/keep/emptychild"); // keep survives (has a file)
    }
}
