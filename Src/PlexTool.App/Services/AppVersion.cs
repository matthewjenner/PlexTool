using System.Reflection;

namespace PlexTool.App.Services;

/// <summary>
/// The running app's version, read once from the entry assembly. Source: the
/// <c>&lt;VersionPrefix&gt;</c> in <c>Directory.Build.props</c> (3-part SemVer, e.g. "0.1.0").
/// </summary>
public static class AppVersion
{
    /// <summary>3-part SemVer (e.g. "0.1.0"). "?" if the assembly cannot be inspected.</summary>
    public static string Display { get; } = Assembly.GetEntryAssembly()
        ?.GetName().Version?.ToString(3) ?? "?";
}
