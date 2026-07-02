using FluentAssertions;
using PlexTool.Core.Naming;
using PlexTool.Core.Planning;
using PlexTool.Core.Tests.TestSupport;

namespace PlexTool.Core.Tests;

public class FolderStructurePlannerTests
{
    private static readonly PlexNamer Namer = new(NamingScheme.PlexRecommended);

    [Fact]
    public void PlanMovie_targets_the_year_folder_under_the_root()
    {
        var fs = new InMemoryMediaFileSystem();

        var plan = FolderStructurePlanner.PlanMovie(fs, "/srv/plex-media/movies", Namer, "Avatar", 2009);

        plan.Should().ContainSingle();
        plan[0].Path.Should().Be("/srv/plex-media/movies/Avatar (2009)");
        plan[0].AlreadyExists.Should().BeFalse();
    }

    [Fact]
    public void PlanMovie_flags_an_existing_folder()
    {
        var fs = new InMemoryMediaFileSystem().AddDirectory("/srv/plex-media/movies/Avatar (2009)");

        var plan = FolderStructurePlanner.PlanMovie(fs, "/srv/plex-media/movies", Namer, "Avatar", 2009);

        plan[0].AlreadyExists.Should().BeTrue();
    }

    [Fact]
    public void PlanShow_creates_the_show_folder_plus_one_folder_per_season()
    {
        var fs = new InMemoryMediaFileSystem();

        var plan = FolderStructurePlanner.PlanShow(fs, "/srv/plex-media/shows", Namer, "Dexter", 3);

        plan.Select(p => p.Path).Should().Equal(
            "/srv/plex-media/shows/Dexter",
            "/srv/plex-media/shows/Dexter/Season 01",
            "/srv/plex-media/shows/Dexter/Season 02",
            "/srv/plex-media/shows/Dexter/Season 03");
        plan.Should().OnlyContain(p => p.AlreadyExists == false);
    }

    [Fact]
    public void PlanShow_flags_the_seasons_that_already_exist()
    {
        var fs = new InMemoryMediaFileSystem()
            .AddDirectory("/srv/plex-media/shows/Dexter")
            .AddDirectory("/srv/plex-media/shows/Dexter/Season 01");

        var plan = FolderStructurePlanner.PlanShow(fs, "/srv/plex-media/shows", Namer, "Dexter", 2);

        plan.Single(p => p.Path.EndsWith("/Dexter")).AlreadyExists.Should().BeTrue();
        plan.Single(p => p.Path.EndsWith("Season 01")).AlreadyExists.Should().BeTrue();
        plan.Single(p => p.Path.EndsWith("Season 02")).AlreadyExists.Should().BeFalse();
    }

    [Fact]
    public void PlanShow_rejects_zero_seasons()
    {
        var fs = new InMemoryMediaFileSystem();
        FluentActions.Invoking(() => FolderStructurePlanner.PlanShow(fs, "/srv/plex-media/shows", Namer, "Dexter", 0))
            .Should().Throw<ArgumentOutOfRangeException>();
    }

    [Fact]
    public void PlanShow_sanitizes_the_show_name()
    {
        var fs = new InMemoryMediaFileSystem();

        var plan = FolderStructurePlanner.PlanShow(fs, "/srv/plex-media/shows", Namer, "Marvel: Agents", 1);

        plan[0].Path.Should().Be("/srv/plex-media/shows/Marvel Agents");
    }
}
