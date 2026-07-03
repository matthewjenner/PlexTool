using System.Net;
using System.Net.Http.Json;
using System.Text.Json.Serialization;

namespace PlexTool.App.Services;

/// <summary>A Plex library section (a "library" in the Plex UI).</summary>
/// <param name="Key">The section id used in API paths, e.g. "1".</param>
/// <param name="Type">"movie", "show", "artist", etc.</param>
/// <param name="Title">The display name, e.g. "Movies".</param>
public sealed record PlexSection(string Key, string Type, string Title)
{
    /// <summary>What the section dropdowns show, e.g. "Movies (movie)".</summary>
    public string Display => $"{Title} ({Type})";
}

/// <summary>Outcome of a Plex "test / load sections" call.</summary>
public sealed record PlexResult(bool Ok, string Message, IReadOnlyList<PlexSection> Sections);

/// <summary>
/// Minimal Plex Media Server client. For now it only lists library sections (so the user can map
/// which is Movies and which is Shows) and doubles as the connection test. Scan triggers are added
/// in phase 4. The X-Plex-Token is a secret and is only ever sent as a request header.
/// </summary>
public sealed class PlexClient
{
    private static readonly HttpClient Http = new() { Timeout = TimeSpan.FromSeconds(15) };

    /// <summary>Lists the server's library sections. Also serves as the "Test Plex" action.</summary>
    public async Task<PlexResult> GetSectionsAsync(string baseUrl, string? token, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(baseUrl))
            return new PlexResult(false, "Enter the Plex base URL first (e.g. http://10.10.0.220:32400).", []);

        try
        {
            string url = baseUrl.TrimEnd('/') + "/library/sections";
            using var request = new HttpRequestMessage(HttpMethod.Get, url);
            request.Headers.Add("Accept", "application/json");
            if (!string.IsNullOrWhiteSpace(token))
                request.Headers.Add("X-Plex-Token", token);

            using HttpResponseMessage response = await Http.SendAsync(request, ct);

            if (response.StatusCode is HttpStatusCode.Unauthorized or HttpStatusCode.Forbidden)
                return new PlexResult(false, "Plex rejected the token - check the X-Plex-Token.", []);

            response.EnsureSuccessStatusCode();

            SectionsResponse? payload = await response.Content.ReadFromJsonAsync<SectionsResponse>(ct);
            List<PlexSection> sections = payload?.MediaContainer?.Directory?
                .Select(d => new PlexSection(d.Key ?? "", d.Type ?? "", d.Title ?? "(untitled)"))
                .ToList() ?? [];

            return sections.Count == 0
                ? new PlexResult(true, "Connected, but no library sections were returned.", sections)
                : new PlexResult(true, $"Connected - found {sections.Count} librarie(s).", sections);
        }
        catch (TaskCanceledException)
        {
            return new PlexResult(false, "Plex did not respond in time - check the URL and that the server is up.", []);
        }
        catch (HttpRequestException ex)
        {
            return new PlexResult(false, "Could not reach Plex: " + ex.Message, []);
        }
        catch (Exception ex)
        {
            return new PlexResult(false, "Plex request failed: " + ex.Message, []);
        }
    }

    /// <summary>
    /// Triggers a library scan. If <paramref name="scanPath"/> is given, Plex scans just that path
    /// (fast); otherwise it refreshes the whole section. <paramref name="scanPath"/> must be a path
    /// as PLEX sees it (already translated through <c>AppSettings.ToPlexPath</c> for split setups).
    /// </summary>
    public async Task<PlexResult> RefreshAsync(
        string baseUrl, string? token, string? sectionId, string? scanPath, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(baseUrl))
            return new PlexResult(false, "No Plex base URL configured - skipped the library scan.", []);
        if (string.IsNullOrWhiteSpace(sectionId))
            return new PlexResult(false, "No Plex library mapped for this type - skipped the scan (set it in Settings).", []);

        try
        {
            string url = $"{baseUrl.TrimEnd('/')}/library/sections/{sectionId}/refresh";
            if (!string.IsNullOrWhiteSpace(scanPath))
                url += "?path=" + Uri.EscapeDataString(scanPath);

            using var request = new HttpRequestMessage(HttpMethod.Get, url);
            request.Headers.Add("Accept", "application/json");
            if (!string.IsNullOrWhiteSpace(token))
                request.Headers.Add("X-Plex-Token", token);

            using HttpResponseMessage response = await Http.SendAsync(request, ct);

            if (response.StatusCode is HttpStatusCode.Unauthorized or HttpStatusCode.Forbidden)
                return new PlexResult(false, "Plex rejected the token - scan not triggered.", []);

            response.EnsureSuccessStatusCode();
            return new PlexResult(true,
                scanPath is null ? "Plex library scan triggered." : $"Plex scan triggered for {scanPath}.", []);
        }
        catch (Exception ex)
        {
            return new PlexResult(false, "Could not trigger the Plex scan: " + ex.Message, []);
        }
    }

    // ---- JSON shapes (Plex returns MediaContainer.Directory[] when Accept: application/json) ----

    private sealed record SectionsResponse
    {
        [JsonPropertyName("MediaContainer")] public MediaContainerDto? MediaContainer { get; init; }
    }

    private sealed record MediaContainerDto
    {
        [JsonPropertyName("Directory")] public List<DirectoryDto>? Directory { get; init; }
    }

    private sealed record DirectoryDto
    {
        [JsonPropertyName("key")] public string? Key { get; init; }
        [JsonPropertyName("type")] public string? Type { get; init; }
        [JsonPropertyName("title")] public string? Title { get; init; }
    }
}
