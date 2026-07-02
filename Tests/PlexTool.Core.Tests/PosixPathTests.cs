using FluentAssertions;
using PlexTool.Core.Paths;

namespace PlexTool.Core.Tests;

public class PosixPathTests
{
    // ---- Normalize: whitespace + trailing/duplicate slashes ----

    [Theory]
    [InlineData("/srv/plex-media/", "/srv/plex-media")]   // trailing slash removed
    [InlineData("/srv/plex-media", "/srv/plex-media")]    // already clean
    [InlineData("  /srv/plex-media  ", "/srv/plex-media")] // surrounding whitespace trimmed
    [InlineData("/srv//plex-media///x", "/srv/plex-media/x")] // duplicate slashes collapsed
    [InlineData("/srv/plex-media////", "/srv/plex-media")] // many trailing slashes
    [InlineData("/", "/")]                                 // root preserved
    [InlineData("///", "/")]                               // root from many slashes
    [InlineData("", "")]                                   // empty
    [InlineData("   ", "")]                                // blank -> empty
    [InlineData("relative/path/", "relative/path")]        // relative tolerated
    public void Normalize_handles_whitespace_and_slashes(string input, string expected) =>
        PosixPath.Normalize(input).Should().Be(expected);

    [Fact]
    public void Normalize_null_is_empty() => PosixPath.Normalize(null).Should().Be("");

    [Fact]
    public void Normalize_preserves_internal_spaces_in_names() =>
        PosixPath.Normalize("/srv/Movie Name (2009)/").Should().Be("/srv/Movie Name (2009)");

    // ---- Combine ----

    [Theory]
    [InlineData("/srv/movies", "Foo (2009)", "/srv/movies/Foo (2009)")]
    [InlineData("/srv/movies/", "Foo (2009)", "/srv/movies/Foo (2009)")]  // trailing slash on base
    [InlineData("/srv/movies", "/Foo (2009)", "/srv/movies/Foo (2009)")]  // leading slash on child
    [InlineData("/srv/movies/", "/Foo/", "/srv/movies/Foo")]              // slashes both sides
    [InlineData("", "Foo", "Foo")]                                        // empty base
    [InlineData("/srv/movies", "", "/srv/movies")]                        // empty child
    [InlineData("  /srv/movies  ", "  Foo  ", "/srv/movies/Foo")]         // whitespace both sides
    public void Combine_joins_with_single_slash(string basePath, string child, string expected) =>
        PosixPath.Combine(basePath, child).Should().Be(expected);

    // ---- TranslatePrefix: the split-topology path mapping ----

    [Fact]
    public void TranslatePrefix_rebases_child_path()
    {
        PosixPath.TranslatePrefix("/srv/plex-media/movies/Foo (2009)", "/srv/plex-media", "/mnt/media")
            .Should().Be("/mnt/media/movies/Foo (2009)");
    }

    [Fact]
    public void TranslatePrefix_rebases_exact_prefix()
    {
        PosixPath.TranslatePrefix("/srv/plex-media", "/srv/plex-media", "/mnt/media")
            .Should().Be("/mnt/media");
    }

    [Theory]
    [InlineData("/srv/plex-media/", "/srv/plex-media", "/mnt/media/")]  // trailing slashes everywhere
    [InlineData("/srv/plex-media/x/", "/srv/plex-media/", "/mnt/media")]
    [InlineData("  /srv/plex-media/x  ", "  /srv/plex-media  ", "  /mnt/media  ")]  // whitespace
    public void TranslatePrefix_is_trailing_slash_and_whitespace_tolerant(string path, string from, string to)
    {
        // All of these should land under /mnt/media, cleanly normalized.
        PosixPath.TranslatePrefix(path, from, to).Should().StartWith("/mnt/media");
    }

    [Fact]
    public void TranslatePrefix_does_not_match_across_a_segment_boundary()
    {
        // "/srv/plex-media" must NOT be treated as a prefix of "/srv/plex-media-extra".
        PosixPath.TranslatePrefix("/srv/plex-media-extra/movies", "/srv/plex-media", "/mnt/media")
            .Should().Be("/srv/plex-media-extra/movies");
    }

    [Fact]
    public void TranslatePrefix_leaves_unrelated_paths_unchanged()
    {
        PosixPath.TranslatePrefix("/other/place/x", "/srv/plex-media", "/mnt/media")
            .Should().Be("/other/place/x");
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData(null)]
    public void TranslatePrefix_with_blank_prefix_is_a_noop(string? blank)
    {
        PosixPath.TranslatePrefix("/srv/plex-media/x", blank, "/mnt/media")
            .Should().Be("/srv/plex-media/x");
        PosixPath.TranslatePrefix("/srv/plex-media/x", "/srv/plex-media", blank)
            .Should().Be("/srv/plex-media/x");
    }

    // ---- IsSafeSegment: traversal + injection guard ----

    [Theory]
    [InlineData("Dexter")]
    [InlineData("Movie Name (2009)")]
    [InlineData("Show - S01E01")]
    [InlineData("A.Movie.With.Dots.2020")]
    [InlineData("Amelie")]                 // unicode-ish ordinary title
    [InlineData("Numbers 123 & Symbols +")]
    public void IsSafeSegment_accepts_ordinary_names(string name) =>
        PosixPath.IsSafeSegment(name).Should().BeTrue();

    [Theory]
    [InlineData("")]                       // empty
    [InlineData("   ")]                     // blank
    [InlineData(".")]                       // current dir
    [InlineData("..")]                      // parent - traversal
    [InlineData("../etc")]                  // traversal via slash
    [InlineData("a/b")]                     // embedded separator
    [InlineData("/absolute")]              // leading separator
    [InlineData(" leading")]               // leading whitespace
    [InlineData("trailing ")]              // trailing whitespace
    [InlineData("bad\tname")]              // control char (tab)
    [InlineData("null\0byte")]             // NUL injection
    [InlineData("line\nbreak")]            // newline injection
    public void IsSafeSegment_rejects_traversal_and_injection(string name) =>
        PosixPath.IsSafeSegment(name).Should().BeFalse();

    [Fact]
    public void IsSafeSegment_rejects_null() => PosixPath.IsSafeSegment(null).Should().BeFalse();

    // ---- ContainsTraversal ----

    [Theory]
    [InlineData("/srv/plex-media/movies/Foo", false)]
    [InlineData("/srv/plex-media/../etc/passwd", true)]
    [InlineData("/srv/./plex-media", true)]
    [InlineData("a/b/c", false)]
    [InlineData("", false)]
    public void ContainsTraversal_flags_dot_segments(string path, bool expected) =>
        PosixPath.ContainsTraversal(path).Should().Be(expected);
}
