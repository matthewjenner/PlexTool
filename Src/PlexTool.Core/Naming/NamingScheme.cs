namespace PlexTool.Core.Naming;

/// <summary>How episode/movie files are named when created or normalized.</summary>
public enum NamingScheme
{
    /// <summary>Plex recommended: "Show Name - S01E01" / "Movie Name (Year)". Uppercase, dash-separated.</summary>
    PlexRecommended,

    /// <summary>The legacy PowerShell-script form: "Show Name s01e01" / "Movie Name (Year)". Lowercase, space.</summary>
    ScriptLegacy,
}
