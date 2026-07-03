using FluentAssertions;
using PlexTool.Core.Naming;
using PlexTool.Core.Planning;
using PlexTool.Core.Tests.TestSupport;

namespace PlexTool.Core.Tests;

public class SubtitleNameTests
{
    [Theory]
    [InlineData("Movie.mkv", null, ".mkv")]
    [InlineData("Movie.srt", null, ".srt")]
    [InlineData("Movie.en.srt", "en", ".srt")]
    [InlineData("Movie.eng.srt", "eng", ".srt")]
    [InlineData("Movie.en.forced.srt", "en.forced", ".srt")]
    [InlineData("Movie.forced.srt", "forced", ".srt")]
    [InlineData("Dune.Part.Two.srt", null, ".srt")]        // "Two" must NOT be read as a language
    [InlineData("Dune.Part.Two.en.srt", "en", ".srt")]     // only the real code is peeled off
    [InlineData("Movie.EN.SRT", "en", ".SRT")]             // case-insensitive tag, extension case kept
    public void Split_extracts_language_and_extension(string fileName, string? language, string extension)
    {
        (string? lang, string ext) = SubtitleName.Split(fileName);
        lang.Should().Be(language);
        ext.Should().Be(extension);
    }
}

public class RenamePlannerTests
{
    private static readonly PlexNamer Namer = new(NamingScheme.PlexRecommended);
    private static readonly IReadOnlySet<string> MediaExts =
        new HashSet<string>(StringComparer.OrdinalIgnoreCase) { ".mkv", ".mp4", ".srt", ".avi" };

    [Fact]
    public void Movies_rename_media_to_the_folder_name()
    {
        var fs = new InMemoryMediaFileSystem()
            .AddFile("/movies/Enemy (2013)/Enemy.2013.1080p.mkv")
            .AddFile("/movies/Enemy (2013)/Enemy.2013.en.srt");

        var ops = RenamePlanner.PlanMovies(fs, "/movies", MediaExts);

        ops.Should().HaveCount(2);
        ops.Single(o => o.SourceName.EndsWith(".mkv")).TargetName.Should().Be("Enemy (2013).mkv");
        ops.Single(o => o.SourceName.EndsWith(".srt")).TargetName.Should().Be("Enemy (2013).en.srt");
        ops.Should().OnlyContain(o => o.Status == RenameStatus.WillRename);
    }

    [Fact]
    public void Movies_skip_files_already_named_correctly()
    {
        var fs = new InMemoryMediaFileSystem().AddFile("/movies/Enemy (2013)/Enemy (2013).mkv");

        var ops = RenamePlanner.PlanMovies(fs, "/movies", MediaExts);

        ops.Should().ContainSingle();
        ops[0].Status.Should().Be(RenameStatus.AlreadyCorrect);
        ops[0].TargetPath.Should().BeNull();
    }

    [Fact]
    public void Movies_flag_a_collision_instead_of_overwriting()
    {
        var fs = new InMemoryMediaFileSystem()
            .AddFile("/movies/Enemy (2013)/Enemy.2013.mkv")     // would rename to "Enemy (2013).mkv"
            .AddFile("/movies/Enemy (2013)/Enemy (2013).mkv");  // ...but that already exists (different file)

        var ops = RenamePlanner.PlanMovies(fs, "/movies", MediaExts);

        ops.Single(o => o.SourceName == "Enemy.2013.mkv").Status.Should().Be(RenameStatus.Collision);
        ops.Single(o => o.SourceName == "Enemy (2013).mkv").Status.Should().Be(RenameStatus.AlreadyCorrect);
    }

    [Fact]
    public void Movies_ignore_non_media_files()
    {
        var fs = new InMemoryMediaFileSystem()
            .AddFile("/movies/Enemy (2013)/Enemy.2013.mkv")
            .AddFile("/movies/Enemy (2013)/poster.jpg")
            .AddFile("/movies/Enemy (2013)/notes.txt");

        var ops = RenamePlanner.PlanMovies(fs, "/movies", MediaExts);

        ops.Should().ContainSingle().Which.SourceName.Should().Be("Enemy.2013.mkv");
    }

    [Fact]
    public void Shows_rename_episodes_to_recommended_form()
    {
        var fs = new InMemoryMediaFileSystem()
            .AddFile("/shows/Dexter/Season 01/Dexter.S01E01.1080p.mkv")
            .AddFile("/shows/Dexter/Season 01/Dexter.S01E01.en.srt")
            .AddFile("/shows/Dexter/Season 01/dexter 1x02 hdtv.mkv");

        var ops = RenamePlanner.PlanShows(fs, "/shows", MediaExts, Namer);

        ops.Single(o => o.SourceName.EndsWith("E01.1080p.mkv")).TargetName.Should().Be("Dexter - S01E01.mkv");
        ops.Single(o => o.SourceName.EndsWith("E01.en.srt")).TargetName.Should().Be("Dexter - S01E01.en.srt");
        ops.Single(o => o.SourceName.Contains("1x02")).TargetName.Should().Be("Dexter - S01E02.mkv");
    }

    [Fact]
    public void Shows_skip_files_without_a_season_episode_token()
    {
        var fs = new InMemoryMediaFileSystem()
            .AddFile("/shows/Dexter/Season 01/behind the scenes.mkv");

        var ops = RenamePlanner.PlanShows(fs, "/shows", MediaExts, Namer);

        ops.Should().ContainSingle();
        ops[0].Status.Should().Be(RenameStatus.NoEpisodePattern);
        ops[0].TargetName.Should().BeNull();
    }

    [Fact]
    public void Shows_legacy_scheme_uses_lowercase_token()
    {
        var fs = new InMemoryMediaFileSystem().AddFile("/shows/Dexter/Season 01/Dexter.S01E01.mkv");
        var legacy = new PlexNamer(NamingScheme.ScriptLegacy);

        var ops = RenamePlanner.PlanShows(fs, "/shows", MediaExts, legacy);

        ops[0].TargetName.Should().Be("Dexter s01e01.mkv");
    }
}
