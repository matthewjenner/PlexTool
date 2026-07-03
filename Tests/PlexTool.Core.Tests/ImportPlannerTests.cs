using FluentAssertions;
using PlexTool.Core.Naming;
using PlexTool.Core.Planning;
using PlexTool.Core.Tests.TestSupport;

namespace PlexTool.Core.Tests;

public class ImportPlannerTests
{
    private static readonly PlexNamer Namer = new(NamingScheme.PlexRecommended);
    private static readonly IReadOnlySet<string> MediaExts =
        new HashSet<string>(StringComparer.OrdinalIgnoreCase) { ".mkv", ".mp4", ".srt", ".avi" };

    [Fact]
    public void Movie_import_moves_media_into_the_year_folder_renamed()
    {
        var fs = new InMemoryMediaFileSystem()
            .AddFile("/downloads/Enemy.2013.1080p/Enemy.2013.1080p.BluRay.mkv")
            .AddFile("/downloads/Enemy.2013.1080p/Enemy.2013.en.srt")
            .AddFile("/downloads/Enemy.2013.1080p/readme.nfo");

        ImportPlan plan = ImportPlanner.PlanMovie(
            fs, "/downloads/Enemy.2013.1080p", "/srv/movies", Namer, "Enemy", 2013, MediaExts);

        plan.ScanPath.Should().Be("/srv/movies/Enemy (2013)");
        plan.DirectoriesToCreate.Should().ContainSingle().Which.Should().Be("/srv/movies/Enemy (2013)");
        plan.Files.Should().HaveCount(2); // .nfo ignored
        plan.Files.Single(f => f.SourceName.EndsWith(".mkv")).TargetPath.Should().Be("/srv/movies/Enemy (2013)/Enemy (2013).mkv");
        plan.Files.Single(f => f.SourceName.EndsWith(".srt")).TargetPath.Should().Be("/srv/movies/Enemy (2013)/Enemy (2013).en.srt");
        plan.Files.Should().OnlyContain(f => f.Status == ImportStatus.WillMove);
    }

    [Fact]
    public void Movie_import_of_a_single_loose_file_works()
    {
        var fs = new InMemoryMediaFileSystem().AddFile("/downloads/Avatar.2009.mkv");

        ImportPlan plan = ImportPlanner.PlanMovie(
            fs, "/downloads/Avatar.2009.mkv", "/srv/movies", Namer, "Avatar", 2009, MediaExts);

        plan.Files.Should().ContainSingle()
            .Which.TargetPath.Should().Be("/srv/movies/Avatar (2009)/Avatar (2009).mkv");
    }

    [Fact]
    public void Movie_import_flags_a_second_video_as_a_collision()
    {
        var fs = new InMemoryMediaFileSystem()
            .AddFile("/downloads/M/movie.mkv")
            .AddFile("/downloads/M/sample.mkv"); // both would target "Enemy (2013).mkv"

        ImportPlan plan = ImportPlanner.PlanMovie(fs, "/downloads/M", "/srv/movies", Namer, "Enemy", 2013, MediaExts);

        plan.Files.Count(f => f.Status == ImportStatus.WillMove).Should().Be(1);
        plan.Files.Count(f => f.Status == ImportStatus.Collision).Should().Be(1);
    }

    [Fact]
    public void Movie_import_flags_collision_with_an_existing_library_file()
    {
        var fs = new InMemoryMediaFileSystem()
            .AddFile("/downloads/Enemy/Enemy.2013.mkv")
            .AddFile("/srv/movies/Enemy (2013)/Enemy (2013).mkv"); // already there

        ImportPlan plan = ImportPlanner.PlanMovie(fs, "/downloads/Enemy", "/srv/movies", Namer, "Enemy", 2013, MediaExts);

        plan.Files.Should().ContainSingle().Which.Status.Should().Be(ImportStatus.Collision);
    }

    [Fact]
    public void Show_import_folders_episodes_by_parsed_season()
    {
        var fs = new InMemoryMediaFileSystem()
            .AddFile("/downloads/Dexter.S01/Dexter.S01E01.mkv")
            .AddFile("/downloads/Dexter.S01/Dexter.S01E02.mkv")
            .AddFile("/downloads/Dexter.S01/Dexter.S01E01.en.srt");

        ImportPlan plan = ImportPlanner.PlanShow(fs, "/downloads/Dexter.S01", "/srv/shows", Namer, "Dexter", MediaExts);

        plan.ScanPath.Should().Be("/srv/shows/Dexter");
        plan.DirectoriesToCreate.Should().ContainSingle().Which.Should().Be("/srv/shows/Dexter/Season 01");
        plan.Files.Single(f => f.SourceName == "Dexter.S01E01.mkv").TargetPath
            .Should().Be("/srv/shows/Dexter/Season 01/Dexter - S01E01.mkv");
        plan.Files.Single(f => f.SourceName == "Dexter.S01E01.en.srt").TargetPath
            .Should().Be("/srv/shows/Dexter/Season 01/Dexter - S01E01.en.srt");
    }

    [Fact]
    public void Show_import_spans_multiple_seasons()
    {
        var fs = new InMemoryMediaFileSystem()
            .AddFile("/downloads/Dexter/Dexter.S01E01.mkv")
            .AddFile("/downloads/Dexter/Dexter.S02E05.mkv");

        ImportPlan plan = ImportPlanner.PlanShow(fs, "/downloads/Dexter", "/srv/shows", Namer, "Dexter", MediaExts);

        plan.DirectoriesToCreate.Should().BeEquivalentTo(
            ["/srv/shows/Dexter/Season 01", "/srv/shows/Dexter/Season 02"]);
    }

    [Fact]
    public void Show_import_skips_files_without_a_season_episode()
    {
        var fs = new InMemoryMediaFileSystem().AddFile("/downloads/Dexter/featurette.mkv");

        ImportPlan plan = ImportPlanner.PlanShow(fs, "/downloads/Dexter", "/srv/shows", Namer, "Dexter", MediaExts);

        plan.Files.Should().ContainSingle().Which.Status.Should().Be(ImportStatus.NoEpisodePattern);
        plan.DirectoriesToCreate.Should().BeEmpty();
    }
}
