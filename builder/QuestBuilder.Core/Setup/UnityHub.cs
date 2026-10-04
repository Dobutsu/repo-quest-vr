using System.Text.Json;

namespace RepoQuest.Builder.Setup;

/// <summary>Unity Hub: where it keeps its settings and editors, and its headless command line</summary>
public static partial class UnityHub
{
    /// <summary>The Hub's settings folder (%APPDATA%\UnityHub, ~/Library/Application Support/UnityHub)</summary>
    public static string SettingsDir => Platform.IsMac
        ? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "Library", "Application Support", "UnityHub")
        : Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "UnityHub");

    /// <summary>The Hub's executable if it is installed</summary>
    public static string? Executable()
    {
        var candidates = Platform.IsMac
            ? new[] { "/Applications/Unity Hub.app/Contents/MacOS/Unity Hub" }
            : new[]
            {
                Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), "Unity Hub", "Unity Hub.exe"),
                Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Programs", "Unity Hub", "Unity Hub.exe"),
            };
        return candidates.FirstOrDefault(File.Exists);
    }

    /// <summary>Version folders of editors outside the default location: the Hub's custom install path
    /// (secondaryInstallPath.json) and editors it was told about (editors-v2.json)</summary>
    public static IEnumerable<string> CustomEditorRoots(string version)
    {
        var roots = new List<string>();
        try
        {
            var secondary = Path.Combine(SettingsDir, "secondaryInstallPath.json");
            if (File.Exists(secondary) && JsonSerializer.Deserialize<string>(File.ReadAllText(secondary)) is { Length: > 0 } path)
                roots.Add(Path.Combine(path, version));

            var editors = Path.Combine(SettingsDir, "editors-v2.json");
            if (File.Exists(editors))
            {
                using var json = JsonDocument.Parse(File.ReadAllText(editors));
                roots.AddRange(Strings(json.RootElement).Where(s => s.Contains(version, StringComparison.Ordinal))
                    .Select(s => EditorRootOf(s, version)).OfType<string>());
            }
        }
        catch (Exception e) when (e is IOException or JsonException or UnauthorizedAccessException)
        {
            Log.Debug($"Unity Hub settings: {e.Message}");
        }
        return roots.Distinct(StringComparer.OrdinalIgnoreCase);
    }

    /// <summary>The version folder in a path that points at or into an editor install</summary>
    private static string? EditorRootOf(string path, string version)
    {
        var index = path.IndexOf(version, StringComparison.Ordinal);
        return index < 0 ? null : path[..(index + version.Length)];
    }

    private static IEnumerable<string> Strings(JsonElement element) => element.ValueKind switch
    {
        JsonValueKind.String => [element.GetString()!],
        JsonValueKind.Array => element.EnumerateArray().SelectMany(Strings),
        JsonValueKind.Object => element.EnumerateObject().SelectMany(p => Strings(p.Value)),
        _ => [],
    };
}
