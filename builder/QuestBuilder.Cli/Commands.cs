using RepoQuest.Builder.Game;
using RepoQuest.Builder.Pipeline;
using RepoQuest.Builder.Setup;

namespace RepoQuest.Builder.Cli;

internal sealed class Options
{
    private readonly Dictionary<string, string?> values = new();

    public static Options Parse(string[] args)
    {
        var options = new Options();
        for (var i = 0; i < args.Length; i++)
        {
            if (!args[i].StartsWith("--"))
                throw new BuildException($"unexpected argument {args[i]}");
            var key = args[i][2..];
            var takesValue = i + 1 < args.Length && !args[i + 1].StartsWith("--");
            options.values[key] = takesValue ? args[++i] : null;
        }
        return options;
    }

    public bool Has(string key) => values.ContainsKey(key);
    public string? Get(string key) => values.GetValueOrDefault(key);
}

internal static class Commands
{
    public static int Usage()
    {
        Console.WriteLine("usage: QuestBuilder.Cli build [--dev] [--workspace <dir>] [--game <dir>] [--kit <dir>] [--unity <dir>]");
        Console.WriteLine("                              [--apk-name <name>] [--development] [--profiling] [--force] [--only <steps>]");
        Console.WriteLine("       QuestBuilder.Cli locate");
        return 64;
    }

    public static async Task<int> BuildAsync(Options options)
    {
        var (workspace, kit, gamePath, unityRoot) = Resolve(options);
        workspace.Create();
        Log.OpenFile(Path.Combine(workspace.Logs, "builder.log"));
        var state = BuildState.Load(workspace.StatePath);

        gamePath ??= state.GamePath ?? GameLocator.Find()
            ?? throw new BuildException("R.E.P.O. not found; pass --game <folder with REPO_Data>");
        if (!GameInstall.IsValid(gamePath))
            throw new BuildException($"{gamePath} is not a R.E.P.O. installation (no REPO_Data/Managed/Assembly-CSharp.dll)");
        var game = new GameInstall(gamePath);
        var unity = unityRoot is not null ? new UnityInstall(unityRoot) : UnityInstall.Find(kit.Manifest.Unity.Version);
        if (unity is null)
            throw new BuildException($"Unity {kit.Manifest.Unity.Version} not found; pass --unity <its folder>");
        // The project step only reads the install's built-in packages; a Mac install seen from Wine (Z:\Applications\...)
        // serves it for testing the Windows pipeline without Unity's Windows editor
        if (!unity.HasEditor)
            Console.WriteLine($"note: no Unity editor executable in {unity.Root}; the unity step will fail");

        var buildOptions = new BuildOptions
        {
            ApkName = options.Get("apk-name") ?? (options.Has("dev") ? "repo-quest-vr" : "REPO-Quest"),
            Development = options.Has("development"),
            Profiling = options.Has("profiling"),
            Force = options.Has("force"),
            Only = options.Get("only")?.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).ToHashSet(),
            VersionName = options.Get("version-name") ?? (kit.Version == "dev" ? "0.0.0" : kit.Version),
            VersionCode = int.TryParse(options.Get("version-code"), out var code)
                ? code
                : BuildOptions.VersionCodeOf(kit.Version == "dev" ? "0.0.0" : kit.Version),
        };

        Console.WriteLine($"game:      {game.Path} ({game.Version() ?? "version unknown"})");
        Console.WriteLine($"unity:     {unity.Root}");
        Console.WriteLine($"kit:       {kit.Root} ({kit.Version})");
        Console.WriteLine($"workspace: {workspace.Root}");
        if (!kit.Manifest.TestedGames.Any(g => g.AssemblyCSharp == game.AssemblyHash()))
            Console.WriteLine("note: this game version is not in the kit's list of tested versions");

        using var cancel = new CancellationTokenSource();
        Console.CancelKeyPress += (_, e) =>
        {
            e.Cancel = true;
            cancel.Cancel();
        };
        var progress = new ConsoleProgress();
        using var awake = SleepBlocker.Start();
        var context = await BuildSession.PrepareAsync(workspace, kit, game, unity, buildOptions, state, progress, cancel.Token);
        await BuildPipeline.RunAsync(context, cancel.Token);
        if (File.Exists(buildOptions.ApkPath(workspace)))
            Console.WriteLine($"APK: {buildOptions.ApkPath(workspace)}");
        return 0;
    }

    public static int Locate(Options options)
    {
        foreach (var candidate in GameLocator.Candidates())
            Console.WriteLine($"{(GameInstall.IsValid(candidate) ? "game  " : "      ")} {candidate}");
        var unity = UnityInstall.Find("2022.3.62f3");
        Console.WriteLine($"unity  {unity?.Root ?? "(not found)"}{(unity is { HasAndroid: true } ? " +android" : "")}");
        Console.WriteLine($"hub    {UnityHub.Executable() ?? "(not found)"}");
        return 0;
    }

    private static (Workspace, Kit, string?, string?) Resolve(Options options)
    {
        string? root = null;
        if (options.Has("dev"))
        {
            root = Directory.GetCurrentDirectory();
            if (!Directory.Exists(Path.Combine(root, "builder")) || !Directory.Exists(Path.Combine(root, "scripts")))
                throw new BuildException("--dev runs from the repository root");
        }
        var workspace = new Workspace(options.Get("workspace") ?? (root is not null ? Path.Combine(root, "work") : "RepoQuest"));
        var kitDir = options.Get("kit") ?? (root is not null ? Path.Combine(root, "work", "kit") : null)
                     ?? throw new BuildException("--kit <dir> is required (scripts/make-kit.sh makes one in work/kit)");
        var game = options.Get("game") ?? (root is not null ? Path.Combine(root, "work", "game") : null);
        return (workspace, Kit.Load(kitDir), game, options.Get("unity"));
    }
}
