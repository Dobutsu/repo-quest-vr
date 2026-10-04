namespace RepoQuest.Builder.Pipeline;

/// <summary>
/// The build: export (AssetRipper) → bake (RepoXR's patches into the game's code) → project (the Unity project for
/// Quest) → unity (import and APK). Each step runs only if its inputs changed since it last finished, so a new builder
/// version rebuilds from the bake on, a game update from the export, and an interrupted build resumes where it stopped.
/// </summary>
public static class BuildPipeline
{
    public static IReadOnlyList<BuildStep> Steps { get; } =
        [new ExportStep(), new BakeStep(), new ProjectStep(), new UnityBuildStep()];

    /// <summary>The steps that would run now, without running them</summary>
    public static IReadOnlyList<string> Pending(BuildContext context)
    {
        var stamps = new Dictionary<string, string>();
        var pending = new List<string>();
        foreach (var step in Steps)
        {
            stamps[step.Id] = step.Stamp(context, stamps);
            if (!IsUpToDate(context, step, stamps[step.Id]))
                pending.Add(step.Id);
        }
        return pending;
    }

    /// <param name="until">Stop after this step (the builder exports and bakes while Unity is still installing)</param>
    public static async Task RunAsync(BuildContext context, CancellationToken ct, string? until = null)
    {
        var stamps = new Dictionary<string, string>();
        foreach (var step in Steps)
        {
            if (until is not null && stamps.ContainsKey(until))
                break;
            var stamp = step.Stamp(context, stamps);
            stamps[step.Id] = stamp;
            var selected = context.Options.Only?.Contains(step.Id) ?? true;
            if (!selected || (!context.Options.Force && IsUpToDate(context, step, stamp)))
            {
                if (context.FinishedThisBuild.Add(step.Id))
                {
                    Log.Info($"{step.Id}: up to date");
                    context.Progress.StepFinished(step.Id, StepStatus.UpToDate);
                }
                continue;
            }

            // Forget the old stamp first: a step interrupted halfway must run again
            context.State.Stamps.Remove(step.Id);
            context.SaveState();
            Log.Info($"{step.Id}: running");
            context.CurrentStep = step.Id;
            context.Progress.StepStarted(step.Id);
            var started = DateTime.UtcNow;
            try
            {
                await step.RunAsync(context, ct);
            }
            catch (OperationCanceledException)
            {
                context.Progress.StepFinished(step.Id, StepStatus.Cancelled);
                throw;
            }
            catch
            {
                context.Progress.StepFinished(step.Id, StepStatus.Failed);
                throw;
            }
            context.State.Stamps[step.Id] = stamp;
            context.SaveState();
            context.FinishedThisBuild.Add(step.Id);
            Log.Info($"{step.Id}: done in {DateTime.UtcNow - started:hh\\:mm\\:ss}");
            context.Progress.StepFinished(step.Id, StepStatus.Done);
        }
    }

    private static bool IsUpToDate(BuildContext context, BuildStep step, string stamp) =>
        context.State.Stamps.TryGetValue(step.Id, out var last) && last == stamp && step.ResultExists(context);
}
