namespace PlexTool.Core.Naming;

/// <summary>
/// Subtitle-aware filename handling: knows which extensions are subtitles and can peel off a
/// trailing language/flag suffix (e.g. "...en.forced.srt") so it can be preserved when renaming.
/// </summary>
public static class SubtitleName
{
    /// <summary>Extensions treated as subtitles (case-insensitive).</summary>
    public static readonly IReadOnlySet<string> SubtitleExtensions = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
    {
        ".srt", ".ass", ".ssa", ".sub", ".vtt", ".idx", ".smi",
    };

    // A curated set of common language codes (ISO 639-1/2) and subtitle flags. Using a fixed set
    // rather than a "any 2-3 letters" rule avoids mistaking ordinary title words ("Two", "The")
    // for a language. An unrecognized token is simply left in place (the file may then not rename
    // rather than being renamed into a collision - safe either way).
    private static readonly IReadOnlySet<string> Tags = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
    {
        // flags
        "forced", "sdh", "cc", "hi", "foreign", "default",
        // 2-letter codes
        "en", "es", "fr", "de", "it", "pt", "nl", "ru", "ja", "zh", "ko", "ar", "pl", "sv",
        "da", "no", "fi", "cs", "tr", "el", "he", "th", "vi", "id", "uk", "ro", "hu", "hr", "bg",
        // 3-letter codes
        "eng", "spa", "fre", "fra", "ger", "deu", "ita", "por", "dut", "nld", "rus", "jpn", "chi",
        "zho", "kor", "ara", "pol", "swe", "dan", "nor", "fin", "cze", "ces", "tur", "ell", "heb",
        "tha", "vie", "ind", "ukr", "rum", "ron", "hun", "hrv", "bul",
    };

    /// <summary>True if <paramref name="extension"/> (with leading dot) is a subtitle extension.</summary>
    public static bool IsSubtitle(string extension) => SubtitleExtensions.Contains(extension);

    /// <summary>
    /// Splits a file name into its language suffix (for subtitles) and its extension. For a video
    /// or a subtitle with no recognizable language token, <c>Language</c> is null. The extension is
    /// returned with its leading dot and original case, e.g. (".en.forced" -&gt; "en.forced", ".srt").
    /// </summary>
    public static (string? Language, string Extension) Split(string fileName)
    {
        int lastDot = fileName.LastIndexOf('.');
        if (lastDot < 0)
            return (null, "");

        string extension = fileName[lastDot..];
        if (!IsSubtitle(extension))
            return (null, extension);

        string rest = fileName[..lastDot];
        var tags = new List<string>();
        while (true)
        {
            int dot = rest.LastIndexOf('.');
            if (dot < 0)
                break;
            string token = rest[(dot + 1)..];
            if (!Tags.Contains(token))
                break;
            tags.Insert(0, token.ToLowerInvariant());
            rest = rest[..dot];
        }

        return (tags.Count == 0 ? null : string.Join('.', tags), extension);
    }
}
