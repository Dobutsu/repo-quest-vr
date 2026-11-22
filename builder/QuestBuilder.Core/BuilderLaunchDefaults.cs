using System.Text.Json;

namespace RepoQuest.Builder;

/// <summary>Optional defaults beside a packaged builder. Remembered choices and command-line options take priority.</summary>
public sealed class BuilderLaunchDefaults
{
    public string? GamePath { get; set; }
    public string? WorkspacePath { get; set; }
    public string? Language { get; set; }

    public static BuilderLaunchDefaults Load(string? directory = null)
    {
        var path = Path.Combine(directory ?? AppContext.BaseDirectory, "builder-startup.json");
        try
        {
            return File.Exists(path)
                ? JsonSerializer.Deserialize<BuilderLaunchDefaults>(File.ReadAllText(path), KitManifest.Json) ?? new()
                : new();
        }
        catch (Exception error) when (error is IOException or JsonException or UnauthorizedAccessException)
        {
            Log.Warn($"Ignoring invalid builder-startup.json: {error.Message}");
            return new();
        }
    }
}
