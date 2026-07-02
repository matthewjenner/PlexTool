using System.Text;
using PlexTool.Core.Paths;

namespace PlexTool.Core.Naming;

/// <summary>
/// Turns a raw title into a single, safe path segment. Replaces filesystem-hostile characters
/// with spaces, drops control characters, collapses whitespace, and strips leading/trailing dots
/// and spaces. The result is guaranteed to pass <see cref="PosixPath.IsSafeSegment"/>, so a
/// crafted title cannot produce path traversal or an injectable segment.
/// </summary>
public static class NameSanitizer
{
    // Characters illegal in a Windows path and/or awkward on Linux/Plex. Replaced with a space so
    // "Foo: Bar" becomes "Foo Bar" rather than "FooBar".
    private static readonly char[] Illegal = ['\\', '/', ':', '*', '?', '"', '<', '>', '|'];

    /// <summary>
    /// Sanitizes <paramref name="input"/> into a safe segment. Throws <see cref="ArgumentException"/>
    /// if nothing usable remains (e.g. the input was blank or only illegal characters/dots).
    /// </summary>
    public static string Sanitize(string? input)
    {
        if (string.IsNullOrWhiteSpace(input))
            throw new ArgumentException("Name is empty.", nameof(input));

        var sb = new StringBuilder(input.Length);
        foreach (char c in input)
        {
            // Illegal and control characters (incl. NUL/newline) become a space, so word
            // boundaries survive (e.g. "Foo:Bar" -> "Foo Bar", not "FooBar").
            sb.Append(char.IsControl(c) || Illegal.Contains(c) ? ' ' : c);
        }

        // Collapse any run of whitespace to a single space, then trim.
        string collapsed = CollapseWhitespace(sb.ToString()).Trim();

        // Strip leading/trailing dots (a trailing dot is invalid on Windows; a leading dot hides
        // the folder on Linux) and re-trim in case that exposed more whitespace.
        string result = collapsed.Trim('.', ' ').Trim();

        if (result.Length == 0 || result is "." or "..")
            throw new ArgumentException($"Name '{input}' is not usable after removing illegal characters.", nameof(input));

        return result;
    }

    private static string CollapseWhitespace(string value)
    {
        var sb = new StringBuilder(value.Length);
        bool lastWasSpace = false;
        foreach (char c in value)
        {
            bool isSpace = char.IsWhiteSpace(c);
            if (isSpace)
            {
                if (!lastWasSpace)
                    sb.Append(' ');
                lastWasSpace = true;
            }
            else
            {
                sb.Append(c);
                lastWasSpace = false;
            }
        }
        return sb.ToString();
    }
}
