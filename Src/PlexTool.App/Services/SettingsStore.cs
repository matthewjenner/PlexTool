using System.Text.Json;
using System.Text.Json.Serialization;

namespace PlexTool.App.Services;

/// <summary>
/// Loads and saves <see cref="AppSettings"/> as a single hand-editable JSON file. This file
/// holds NO secrets - credentials go through <see cref="SecretStore"/> instead.
/// </summary>
public sealed class SettingsStore(string filePath)
{
    private static readonly JsonSerializerOptions Options = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        Converters = { new JsonStringEnumConverter() },
    };

    /// <summary>Reads the settings file, or returns defaults if it is missing or unreadable.</summary>
    public AppSettings Load()
    {
        try
        {
            if (File.Exists(filePath))
            {
                return JsonSerializer.Deserialize<AppSettings>(File.ReadAllText(filePath), Options)
                       ?? new AppSettings();
            }
        }
        catch
        {
            // A corrupt settings file must not block startup - fall back to defaults.
        }

        return new AppSettings();
    }

    public void Save(AppSettings settings)
    {
        string? directory = Path.GetDirectoryName(filePath);
        if (!string.IsNullOrEmpty(directory))
            Directory.CreateDirectory(directory);

        File.WriteAllText(filePath, JsonSerializer.Serialize(settings, Options));
    }
}
