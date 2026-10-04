using System.Runtime.Versioning;
using Microsoft.Win32;

namespace RepoQuest.Builder.Game;

/// <summary>Finds R.E.P.O. without asking: Steam libraries on Windows, CrossOver/Whisky bottles on a Mac</summary>
public static class GameLocator
{
    public static string? Find() => Candidates().FirstOrDefault(GameInstall.IsValid);

    public static IEnumerable<string> Candidates()
    {
        foreach (var steam in SteamRoots().Distinct(StringComparer.OrdinalIgnoreCase))
        foreach (var game in GamesInSteam(steam))
            yield return game;
    }

    /// <summary>The REPO folder in every library of one Steam installation</summary>
    public static IEnumerable<string> GamesInSteam(string steamRoot)
    {
        var libraries = new List<string> { steamRoot };
        var vdf = Path.Combine(steamRoot, "steamapps", "libraryfolders.vdf");
        if (File.Exists(vdf))
        {
            try
            {
                var folders = VdfNode.Parse(File.ReadAllText(vdf)).Child("libraryfolders");
                foreach (var folder in folders?.Children.Values ?? Enumerable.Empty<VdfNode>())
                {
                    if (folder.Value("path") is { } path)
                        libraries.Add(MapPath(steamRoot, path));
                }
            }
            catch (FormatException e)
            {
                Log.Debug($"{vdf}: {e.Message}");
            }
        }

        foreach (var library in libraries.Distinct(StringComparer.OrdinalIgnoreCase))
        {
            var steamapps = Path.Combine(library, "steamapps");
            var manifest = Path.Combine(steamapps, $"appmanifest_{GameInstall.SteamAppId}.acf");
            var installDir = "REPO";
            if (File.Exists(manifest))
            {
                try
                {
                    installDir = VdfNode.Parse(File.ReadAllText(manifest)).Child("AppState")?.Value("installdir") ?? installDir;
                }
                catch (FormatException)
                {
                }
            }
            yield return Path.Combine(steamapps, "common", installDir);
        }
    }

    private static IEnumerable<string> SteamRoots()
    {
        if (OperatingSystem.IsWindows())
        {
            foreach (var root in WindowsSteamRoots())
                yield return root;
            yield return @"C:\Program Files (x86)\Steam";
            yield return @"C:\Program Files\Steam";
        }
        else if (OperatingSystem.IsMacOS())
        {
            var home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
            var bottles = new[]
            {
                Path.Combine(home, "Library", "Application Support", "CrossOver", "Bottles"),
                Path.Combine(home, "Library", "Containers", "com.isaacmarovitz.Whisky", "Bottles"),
            };
            foreach (var bottle in bottles.Where(Directory.Exists).SelectMany(Directory.EnumerateDirectories))
            {
                yield return Path.Combine(bottle, "drive_c", "Program Files (x86)", "Steam");
                yield return Path.Combine(bottle, "drive_c", "Program Files", "Steam");
            }
        }
    }

    [SupportedOSPlatform("windows")]
    private static IEnumerable<string> WindowsSteamRoots()
    {
        var roots = new List<string>();
        try
        {
            if (Registry.GetValue(@"HKEY_CURRENT_USER\Software\Valve\Steam", "SteamPath", null) is string user)
                roots.Add(user.Replace('/', '\\'));
            if (Registry.GetValue(@"HKEY_LOCAL_MACHINE\SOFTWARE\WOW6432Node\Valve\Steam", "InstallPath", null) is string machine)
                roots.Add(machine);
        }
        catch (Exception e)
        {
            Log.Debug($"Steam registry keys: {e.Message}");
        }
        return roots;
    }

    /// <summary>Library paths in a Steam that runs under CrossOver/Whisky are Windows paths inside the bottle</summary>
    private static string MapPath(string steamRoot, string path)
    {
        var bottle = steamRoot.IndexOf("drive_c", StringComparison.Ordinal);
        if (OperatingSystem.IsWindows() || bottle < 0 || !(path.Length > 2 && path[1] == ':'))
            return path;
        var driveC = steamRoot[..(bottle + "drive_c".Length)];
        var drive = Path.Combine(Path.GetDirectoryName(driveC)!, $"drive_{char.ToLowerInvariant(path[0])}");
        return Path.Combine(drive, path[3..].Replace('\\', '/'));
    }
}
