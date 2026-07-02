namespace PlexTool.Core.Naming;

/// <summary>
/// Builds Plex-conforming folder and file names from titles and season/episode numbers, honoring
/// the chosen <see cref="NamingScheme"/>. All names are sanitized through
/// <see cref="NameSanitizer"/>, so every result is a safe path segment.
/// </summary>
public sealed class PlexNamer(NamingScheme scheme)
{
    /// <summary>Movie folder name, e.g. "Avatar (2009)".</summary>
    public string MovieFolder(string name, int year) => $"{NameSanitizer.Sanitize(name)} ({year})";

    /// <summary>Movie file name: the folder name plus the extension, e.g. "Avatar (2009).mkv".</summary>
    public string MovieFile(string name, int year, string extension) =>
        MovieFolder(name, year) + NormalizeExtension(extension);

    /// <summary>Show folder name, e.g. "Dexter".</summary>
    public string ShowFolder(string name) => NameSanitizer.Sanitize(name);

    /// <summary>Season folder name, zero-padded to two digits, e.g. "Season 01".</summary>
    public string SeasonFolder(int season) => $"Season {season:00}";

    /// <summary>
    /// Episode file name. PlexRecommended: "Dexter - S01E01.mkv". ScriptLegacy: "Dexter s01e01.mkv".
    /// A non-empty <paramref name="languageSuffix"/> (e.g. "en") is inserted before the extension
    /// for subtitles, e.g. "Dexter - S01E01.en.srt".
    /// </summary>
    public string EpisodeFile(string showName, int season, int episode, string extension, string? languageSuffix = null)
    {
        string show = NameSanitizer.Sanitize(showName);
        string token = scheme == NamingScheme.PlexRecommended
            ? $"S{season:00}E{episode:00}"
            : $"s{season:00}e{episode:00}";
        string separator = scheme == NamingScheme.PlexRecommended ? " - " : " ";

        string language = string.IsNullOrWhiteSpace(languageSuffix) ? "" : "." + languageSuffix.Trim();
        return $"{show}{separator}{token}{language}{NormalizeExtension(extension)}";
    }

    /// <summary>Ensures the extension has exactly one leading dot; preserves its original case.</summary>
    private static string NormalizeExtension(string extension)
    {
        if (string.IsNullOrWhiteSpace(extension))
            return "";
        string trimmed = extension.Trim();
        return trimmed.StartsWith('.') ? trimmed : "." + trimmed;
    }
}
