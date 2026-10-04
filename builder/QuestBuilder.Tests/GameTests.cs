using RepoQuest.Builder.Game;

namespace RepoQuest.Builder.Tests;

public sealed class GameTests : IDisposable
{
    private readonly string temp = Directory.CreateTempSubdirectory("repoquest-tests").FullName;

    public void Dispose() => Directory.Delete(temp, recursive: true);

    [Fact]
    public void Vdf_ParsesLibraryFoldersWithEscapedPaths()
    {
        const string text = """
            "libraryfolders"
            {
                "0"
                {
                    "path"		"C:\\Program Files (x86)\\Steam"
                    "apps"
                    {
                        "228980"		"397498489"
                    }
                }
                "1"
                {
                    "path"		"D:\\SteamLibrary"
                    "apps"
                    {
                        "3241660"		"1449236416"
                    }
                }
            }
            """;
        var folders = VdfNode.Parse(text).Child("LIBRARYFOLDERS")!;  // keys are case-insensitive, as in Steam
        Assert.Equal(@"C:\Program Files (x86)\Steam", folders.Child("0")!.Value("path"));
        Assert.Equal(@"D:\SteamLibrary", folders.Child("1")!.Value("path"));
        Assert.Equal("1449236416", folders.Child("1")!.Child("apps")!.Value("3241660"));
    }

    [Fact]
    public void Vdf_RejectsUnbalancedBraces() =>
        Assert.Throws<FormatException>(() => VdfNode.Parse("\"a\" { \"b\" \"c\""));

    [Fact]
    public void GamesInSteam_FollowsLibrariesAndInstallDir()
    {
        var steam = Path.Combine(temp, "Steam");
        var library = Path.Combine(temp, "Library2");
        Write(Path.Combine(steam, "steamapps", "libraryfolders.vdf"),
            $"\"libraryfolders\" {{ \"0\" {{ \"path\" \"{Escape(steam)}\" }} \"1\" {{ \"path\" \"{Escape(library)}\" }} }}");
        Write(Path.Combine(library, "steamapps", "appmanifest_3241660.acf"),
            "\"AppState\" { \"appid\" \"3241660\" \"installdir\" \"REPO Game\" \"buildid\" \"123\" }");
        var game = MakeGame(Path.Combine(library, "steamapps", "common", "REPO Game"));

        var found = GameLocator.GamesInSteam(steam).Where(GameInstall.IsValid).ToList();

        Assert.Equal([game], found);
        Assert.Equal("123", new GameInstall(game).SteamBuildId());
    }

    [Fact]
    public void GamesInSteam_MapsWindowsLibraryPathsInsideABottle()
    {
        // Steam under CrossOver: libraryfolders.vdf names Windows paths; D: is the bottle's drive_d
        var bottle = Path.Combine(temp, "Bottles", "Steam");
        var steam = Path.Combine(bottle, "drive_c", "Program Files (x86)", "Steam");
        Write(Path.Combine(steam, "steamapps", "libraryfolders.vdf"),
            "\"libraryfolders\" { \"1\" { \"path\" \"D:\\\\Games\\\\Steam\" } }");
        var game = MakeGame(Path.Combine(bottle, "drive_d", "Games", "Steam", "steamapps", "common", "REPO"));

        var found = GameLocator.GamesInSteam(steam).Where(GameInstall.IsValid).ToList();

        Assert.Contains(game, found);
    }

    [Fact]
    public void Problem_FindsTheGameBelowAPickedSteamFolder()
    {
        var steam = Path.Combine(temp, "SteamLibrary");
        var game = MakeGame(Path.Combine(steam, "steamapps", "common", "REPO"));

        Assert.Equal("folder-parent:" + game, GameInstall.Problem(steam));
        Assert.Null(GameInstall.Problem(game));
        Assert.Equal("folder-not-game", GameInstall.Problem(temp));
        Assert.Equal("folder-missing", GameInstall.Problem(Path.Combine(temp, "nope")));
    }

    [Fact]
    public void Fingerprints_ChangeWhenTheGameChanges()
    {
        var game = new GameInstall(MakeGame(Path.Combine(temp, "REPO")));
        var code = game.CodeFingerprint();
        var data = game.DataFingerprint();

        File.WriteAllText(game.AssemblyCSharp, "patched");

        Assert.NotEqual(code, game.CodeFingerprint());
        Assert.NotEqual(data, game.DataFingerprint());
    }

    private static string MakeGame(string path)
    {
        Write(Path.Combine(path, "REPO_Data", "Managed", "Assembly-CSharp.dll"), "game");
        Write(Path.Combine(path, "REPO.exe"), "exe");
        return Path.GetFullPath(path);
    }

    private static void Write(string path, string text)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, text);
    }

    private static string Escape(string path) => path.Replace("\\", "\\\\");
}
