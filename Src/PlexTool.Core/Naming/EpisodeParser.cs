using System.Text.RegularExpressions;

namespace PlexTool.Core.Naming;

/// <summary>A parsed season/episode number.</summary>
public readonly record struct SeasonEpisode(int Season, int Episode);

/// <summary>
/// Extracts a season/episode number from a file's base name. Understands the same tokens the
/// original PowerShell script did - "s01e01" / "S01E01" and "1x01" / "8x21" (case-insensitive) -
/// which also covers already-normalized "Show Name - S01E01" names.
/// </summary>
public static partial class EpisodeParser
{
    // s01e01 / S1E1 (1-2 digit season, 1-3 digit episode for long-running shows).
    [GeneratedRegex(@"s(?<s>\d{1,2})e(?<e>\d{1,3})", RegexOptions.IgnoreCase)]
    private static partial Regex SxxEyyPattern();

    // 1x01 / 8x21.
    [GeneratedRegex(@"(?<s>\d{1,2})x(?<e>\d{1,3})", RegexOptions.IgnoreCase)]
    private static partial Regex NxNPattern();

    /// <summary>Returns the season/episode found in <paramref name="baseName"/>, or null if none matches.</summary>
    public static SeasonEpisode? Parse(string? baseName)
    {
        if (string.IsNullOrWhiteSpace(baseName))
            return null;

        Match m = SxxEyyPattern().Match(baseName);
        if (m.Success)
            return new SeasonEpisode(int.Parse(m.Groups["s"].Value), int.Parse(m.Groups["e"].Value));

        m = NxNPattern().Match(baseName);
        if (m.Success)
            return new SeasonEpisode(int.Parse(m.Groups["s"].Value), int.Parse(m.Groups["e"].Value));

        return null;
    }
}
