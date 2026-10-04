using RepoQuest.Builder;
using RepoQuest.Builder.Cli;
using RepoQuest.Builder.Setup;

// QuestBuilder.Cli: the builder's pipeline from a terminal.
//
//   QuestBuilder.Cli build [--dev] [--workspace <dir>] [--game <dir>] [--kit <dir>] [--unity <dir>]
//                          [--apk-name <name>] [--development] [--profiling] [--force] [--only <step,...>]
//                          [--version-name <x.y.z>] [--version-code <n>]
//   QuestBuilder.Cli locate                  where the builder finds the game and Unity
//   QuestBuilder.Cli player-build|update|install   the players' flow without the window (see PlayerCommands)
//   QuestBuilder.Cli keygen|manifest|sign    release tooling (scripts/release.sh, see ReleaseCommands)
//   QuestBuilder.Cli --tool <name> <args>    one of the .NET tools (the builder runs itself this way)
//
// --dev: this checkout (run from the repository root): workspace work/, game work/game, kit work/kit (scripts/make-kit.sh)

if (ToolHost.TryRun(args, out var toolExit))
    return toolExit;

try
{
    return args.FirstOrDefault() switch
    {
        "build" => await Commands.BuildAsync(Options.Parse(args[1..])),
        "locate" => Commands.Locate(Options.Parse(args[1..])),
        "player-build" => await PlayerCommands.BuildAsync(Options.Parse(args[1..])),
        "update" => await PlayerCommands.UpdateAsync(Options.Parse(args[1..]), args[1..]),
        "install" => await PlayerCommands.InstallAsync(Options.Parse(args[1..])),
        "keygen" => ReleaseCommands.KeyGen(Options.Parse(args[1..])),
        "manifest" => ReleaseCommands.Manifest(Options.Parse(args[1..]), args[1..]),
        "sign" => ReleaseCommands.Sign(Options.Parse(args[1..])),
        _ => Commands.Usage(),
    };
}
catch (BuildException e)
{
    Console.Error.WriteLine();
    Console.Error.WriteLine($"FAILED: {e.Message}");
    if (e.Details is not null)
        Console.Error.WriteLine(e.Details);
    Log.Error($"{e.Message}\n{e.Details}");
    return 1;
}
catch (OperationCanceledException)
{
    Console.Error.WriteLine("cancelled");
    return 130;
}
catch (Exception e)
{
    Console.Error.WriteLine($"FAILED: {e}");
    Log.Error(e.ToString());
    return 1;
}
