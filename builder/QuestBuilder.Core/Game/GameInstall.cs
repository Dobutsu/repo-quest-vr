using System.Text;

namespace RepoQuest.Builder.Game;

/// <summary>The player's R.E.P.O. installation (the folder with REPO.exe and REPO_Data)</summary>
public sealed class GameInstall
{
    public const uint SteamAppId = 3241660;

    public GameInstall(string path)
    {
        Path = System.IO.Path.GetFullPath(path);
    }

    public string Path { get; }
    public string Data => System.IO.Path.Combine(Path, "REPO_Data");
    public string Managed => System.IO.Path.Combine(Data, "Managed");
    public string AssemblyCSharp => System.IO.Path.Combine(Managed, "Assembly-CSharp.dll");
    public string AddressablesCatalog => System.IO.Path.Combine(Data, "StreamingAssets", "aa", "catalog.json");

    public static bool IsValid(string? path) =>
        !string.IsNullOrEmpty(path) && File.Exists(System.IO.Path.Combine(path, "REPO_Data", "Managed", "Assembly-CSharp.dll"));

    /// <summary>What is wrong with a folder the player picked, or null if it is a game install</summary>
    public static string? Problem(string path)
    {
        if (!Directory.Exists(path))
            return "folder-missing";
        if (IsValid(path))
            return null;
        // A common mistake: the Steam library or steamapps folder instead of the game's own folder
        var nested = new[] { "REPO", System.IO.Path.Combine("common", "REPO"), System.IO.Path.Combine("steamapps", "common", "REPO") }
            .Select(sub => System.IO.Path.Combine(path, sub)).FirstOrDefault(IsValid);
        return nested is not null ? "folder-parent:" + nested : "folder-not-game";
    }

    /// <summary>SHA-256 of Assembly-CSharp.dll: identifies the game version (kit.json lists the tested ones)</summary>
    public string AssemblyHash() => Hashing.Sha256File(AssemblyCSharp);

    /// <summary>Hash of the game's code (every DLL in Managed): the input of baking</summary>
    public string CodeFingerprint() =>
        Hashing.Directory(Managed, rel => rel.EndsWith(".dll", StringComparison.OrdinalIgnoreCase));

    /// <summary>
    /// Cheap fingerprint of all game files (relative path, size, modification time): the input of the export. Hashing
    /// 1.4 GB of data on every start would take too long; Steam updates change sizes and times.
    /// </summary>
    public string DataFingerprint()
    {
        var text = new StringBuilder();
        foreach (var file in Directory.EnumerateFiles(Path, "*", SearchOption.AllDirectories)
                     .Select(f => (Rel: System.IO.Path.GetRelativePath(Path, f).Replace('\\', '/'), Full: f))
                     .Where(f => !f.Rel.StartsWith("BepInEx/", StringComparison.OrdinalIgnoreCase))
                     .OrderBy(f => f.Rel, StringComparer.Ordinal))
        {
            var info = new FileInfo(file.Full);
            text.Append(file.Rel).Append(' ').Append(info.Length).Append(' ')
                .Append(info.LastWriteTimeUtc.Ticks).Append('\n');
        }
        return Hashing.Sha256(text.ToString());
    }

    /// <summary>Steam's build id of the installed version, if the game sits in a Steam library</summary>
    public string? SteamBuildId()
    {
        var manifest = System.IO.Path.Combine(Path, "..", "..", $"appmanifest_{SteamAppId}.acf");
        try
        {
            return File.Exists(manifest) ? VdfNode.Parse(File.ReadAllText(manifest)).Child("AppState")?.Value("buildid") : null;
        }
        catch (FormatException)
        {
            return null;
        }
    }

    /// <summary>The game's version as players see it (BuildName, e.g. v0.4.4.3), or null if it cannot be read</summary>
    public string? Version()
    {
        // The version asset ("Version - RELEASE") stores the name next to its own name in sharedassets0.assets
        var file = System.IO.Path.Combine(Data, "sharedassets0.assets");
        try
        {
            var marker = Encoding.ASCII.GetBytes("Version - RELEASE");
            using var stream = File.OpenRead(file);
            var buffer = new byte[1 << 20];
            var carry = 0;
            int read;
            while ((read = stream.Read(buffer, carry, buffer.Length - carry)) > 0)
            {
                var length = carry + read;
                var index = buffer.AsSpan(0, length).IndexOf(marker);
                if (index >= 0)
                {
                    var after = Encoding.ASCII.GetString(buffer, index, Math.Min(96, length - index));
                    var match = System.Text.RegularExpressions.Regex.Match(after, @"v\d+(?:\.\d+){1,3}");
                    return match.Success ? match.Value : null;
                }
                // Keep the end of this chunk: the marker and the value may straddle chunks
                carry = Math.Min(128, length);
                Array.Copy(buffer, length - carry, buffer, 0, carry);
            }
        }
        catch (IOException)
        {
        }
        return null;
    }
}
