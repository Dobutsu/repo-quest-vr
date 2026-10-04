using System.Text.Json;
using System.Text.RegularExpressions;

namespace RepoQuest.Builder;

/// <summary>
/// The builder's own settings, outside the workspace (%LOCALAPPDATA%\RepoQuestBuilder\settings.json): which workspace
/// to use and the UI language. Everything about builds lives in the workspace's state.json.
/// </summary>
public sealed partial class AppSettings
{
    public string? WorkspacePath { get; set; }
    /// <summary>"ru" or "en"; null: the system's language</summary>
    public string? Language { get; set; }

    public static string SettingsDir => Platform.IsWindows
        ? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "RepoQuestBuilder")
        : Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "Library", "Application Support", "RepoQuestBuilder");

    private static string FilePath => Path.Combine(SettingsDir, "settings.json");

    public static AppSettings Load()
    {
        try
        {
            if (File.Exists(FilePath))
                return JsonSerializer.Deserialize<AppSettings>(File.ReadAllText(FilePath), KitManifest.Json) ?? new AppSettings();
        }
        catch (JsonException)
        {
        }
        return new AppSettings();
    }

    public void Save()
    {
        Directory.CreateDirectory(SettingsDir);
        FileOps.WriteTextAtomic(FilePath, JsonSerializer.Serialize(this, KitManifest.Json));
    }

    /// <summary>
    /// A short ASCII path on the fixed drive with the most free space (Windows): Unity, Gradle and the NDK break on
    /// non-ASCII user names and on very long paths, and the build needs ~40 GB
    /// </summary>
    public static string DefaultWorkspace()
    {
        if (!Platform.IsWindows)
            return Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "RepoQuest");
        var drive = DriveInfo.GetDrives()
            .Where(d => d is { DriveType: DriveType.Fixed, IsReady: true })
            .MaxBy(d => d.AvailableFreeSpace);
        return Path.Combine(drive?.RootDirectory.FullName ?? @"C:\", "RepoQuest");
    }

    /// <summary>Why a folder cannot be the workspace (a code the UI explains), or null</summary>
    public static string? WorkspaceProblem(string path, string? gamePath)
    {
        string full;
        try
        {
            full = Path.GetFullPath(path);
        }
        catch (Exception)
        {
            return "workspace-invalid";
        }
        if (!AsciiPathRegex().IsMatch(full))
            return "workspace-not-ascii";
        if (Platform.IsWindows && full.Length > 40)
            return "workspace-too-long";
        if (gamePath is not null && IsInside(full, Path.GetFullPath(gamePath)))
            return "workspace-in-game";
        var programFiles = Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles);
        if (Platform.IsWindows && programFiles.Length > 0 && IsInside(full, programFiles))
            return "workspace-program-files";
        return null;
    }

    /// <summary>The path is the folder or below it (not just sharing a prefix: D:\RepoQuest is not in D:\REPO)</summary>
    private static bool IsInside(string path, string folder)
    {
        var separators = new[] { Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar };
        return (path.TrimEnd(separators) + Path.DirectorySeparatorChar).StartsWith(
            folder.TrimEnd(separators) + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase);
    }

    [GeneratedRegex(@"^[\x20-\x7E]+$")]
    private static partial Regex AsciiPathRegex();
}
