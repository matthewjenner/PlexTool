using FluentAssertions;
using PlexTool.Core.Naming;
using PlexTool.Core.Paths;

namespace PlexTool.Core.Tests;

public class NameSanitizerTests
{
    [Theory]
    [InlineData("Avatar", "Avatar")]
    [InlineData("Foo: Bar", "Foo Bar")]                 // illegal ':' -> space
    [InlineData("A / B \\ C", "A B C")]                 // slashes -> spaces, collapsed
    [InlineData("  Padded   Name  ", "Padded Name")]    // whitespace collapsed + trimmed
    [InlineData("Trailing dot.", "Trailing dot")]       // trailing dot stripped
    [InlineData(".hidden", "hidden")]                   // leading dot stripped
    [InlineData("Movie Name (2009)", "Movie Name (2009)")]
    [InlineData("Numbers 123 & Plus +", "Numbers 123 & Plus +")]
    [InlineData("Quote \"X\" <Y> |Z|", "Quote X Y Z")]  // all illegal -> spaces, collapsed
    public void Sanitize_produces_expected_segment(string input, string expected) =>
        NameSanitizer.Sanitize(input).Should().Be(expected);

    [Fact]
    public void Sanitize_replaces_control_chars_with_space()
    {
        NameSanitizer.Sanitize("Bad\tName\nHere").Should().Be("Bad Name Here");
        NameSanitizer.Sanitize("null\0byte").Should().Be("null byte");
    }

    [Fact]
    public void Sanitize_neutralizes_traversal_attempts()
    {
        // "../etc" -> slash becomes space -> ".. etc" -> leading dots/space trimmed -> "etc".
        NameSanitizer.Sanitize("../etc").Should().Be("etc");
        // Interior ".." survives as a harmless word (no '/', so it is not a traversal segment).
        NameSanitizer.Sanitize("a/../b").Should().Be("a .. b");
    }

    [Theory]
    [InlineData("Avatar (2009)")]
    [InlineData("Foo: Bar / Baz")]
    [InlineData("  weird\t..name..  ")]
    [InlineData("../../escape")]
    public void Sanitize_output_always_passes_IsSafeSegment(string input) =>
        PosixPath.IsSafeSegment(NameSanitizer.Sanitize(input)).Should().BeTrue();

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("...")]
    [InlineData("///")]
    [InlineData("\t\n")]
    public void Sanitize_throws_when_nothing_usable_remains(string input) =>
        FluentActions.Invoking(() => NameSanitizer.Sanitize(input)).Should().Throw<ArgumentException>();

    [Fact]
    public void Sanitize_throws_on_null() =>
        FluentActions.Invoking(() => NameSanitizer.Sanitize(null)).Should().Throw<ArgumentException>();
}

public class EpisodeParserTests
{
    [Theory]
    [InlineData("Dexter.S01E01.1080p", 1, 1)]
    [InlineData("Dexter S1E5", 1, 5)]
    [InlineData("Show s08e21", 8, 21)]
    [InlineData("Show 1x01", 1, 1)]
    [InlineData("Show 8x21", 8, 21)]
    [InlineData("Dexter - S01E01", 1, 1)]              // already-normalized
    [InlineData("The 100 S07E100", 7, 100)]            // 3-digit episode
    public void Parse_finds_season_and_episode(string name, int season, int episode)
    {
        SeasonEpisode? result = EpisodeParser.Parse(name);
        result.Should().Be(new SeasonEpisode(season, episode));
    }

    [Theory]
    [InlineData("Just A Movie (2009)")]
    [InlineData("no markers here")]
    [InlineData("")]
    [InlineData(null)]
    public void Parse_returns_null_when_no_pattern(string? name) =>
        EpisodeParser.Parse(name).Should().BeNull();
}

public class PlexNamerTests
{
    private static readonly PlexNamer Recommended = new(NamingScheme.PlexRecommended);
    private static readonly PlexNamer Legacy = new(NamingScheme.ScriptLegacy);

    [Fact]
    public void Movie_folder_and_file()
    {
        Recommended.MovieFolder("Avatar", 2009).Should().Be("Avatar (2009)");
        Recommended.MovieFile("Avatar", 2009, "mkv").Should().Be("Avatar (2009).mkv");
        Recommended.MovieFile("Avatar", 2009, ".MKV").Should().Be("Avatar (2009).MKV");  // ext case preserved
    }

    [Fact]
    public void Show_and_season_folders()
    {
        Recommended.ShowFolder("Dexter").Should().Be("Dexter");
        Recommended.SeasonFolder(1).Should().Be("Season 01");
        Recommended.SeasonFolder(12).Should().Be("Season 12");
    }

    [Fact]
    public void Episode_file_respects_scheme()
    {
        Recommended.EpisodeFile("Dexter", 1, 1, "mkv").Should().Be("Dexter - S01E01.mkv");
        Legacy.EpisodeFile("Dexter", 1, 1, "mkv").Should().Be("Dexter s01e01.mkv");
    }

    [Fact]
    public void Episode_file_keeps_subtitle_language_suffix()
    {
        Recommended.EpisodeFile("Dexter", 1, 1, "srt", "en").Should().Be("Dexter - S01E01.en.srt");
        Legacy.EpisodeFile("Dexter", 1, 1, "srt", "en").Should().Be("Dexter s01e01.en.srt");
    }

    [Fact]
    public void Names_are_sanitized()
    {
        Recommended.MovieFolder("Face/Off", 1997).Should().Be("Face Off (1997)");
        Recommended.EpisodeFile("Marvel: Agents", 1, 1, "mkv").Should().Be("Marvel Agents - S01E01.mkv");
    }
}
