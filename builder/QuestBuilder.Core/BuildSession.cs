using RepoQuest.Builder.Game;
using RepoQuest.Builder.Pipeline;
using RepoQuest.Builder.Setup;

namespace RepoQuest.Builder;

/// <summary>Puts a build together: the tools it needs (Python, downloads) and the context the pipeline runs in</summary>
public static class BuildSession
{
    public static async Task<BuildContext> PrepareAsync(Workspace workspace, Kit kit, GameInstall game,
        UnityInstall? unity, BuildOptions options, BuildState state, IBuildProgress progress, CancellationToken ct)
    {
        workspace.Create();
        state.GamePath = game.Path;
        state.Save(workspace.StatePath);

        var thirdParty = new ThirdParty(workspace, kit);
        BuildContext? context = null;
        thirdParty.Progress = new Progress<(string Name, DownloadProgress Progress)>(p =>
            progress.StepProgress(context?.CurrentStep ?? "setup",
                p.Progress.Total is { } total and > 0 ? p.Progress.Done / (double)total : null, "download",
                $"{p.Name} {p.Progress.Done >> 20}/{(p.Progress.Total ?? 0) >> 20} MB"));
        progress.StepStarted("setup");
        PythonEnv python;
        try
        {
            python = await PythonEnv.EnsureAsync(workspace, kit, thirdParty, ct);
        }
        catch (OperationCanceledException)
        {
            progress.StepFinished("setup", StepStatus.Cancelled);
            throw;
        }
        catch
        {
            progress.StepFinished("setup", StepStatus.Failed);
            throw;
        }
        progress.StepFinished("setup", StepStatus.Done);

        return context = new BuildContext
        {
            Workspace = workspace,
            Kit = kit,
            Game = game,
            Unity = unity,
            Python = python,
            Tools = ToolLauncher.For(workspace),
            ThirdParty = thirdParty,
            State = state,
            Options = options,
            Progress = progress,
        };
    }
}
