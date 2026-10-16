namespace RepoQuest.Builder.Pipeline;

public enum StepStatus { Pending, Running, UpToDate, Done, Failed, Cancelled }

/// <summary>Progress of a build as the window and the console show it</summary>
public interface IBuildProgress
{
    void StepStarted(string step);

    /// <param name="fraction">0..1, or null when the step cannot tell</param>
    /// <param name="stage">A code the UI translates (e.g. "il2cpp", "shaders"), or null</param>
    /// <param name="detail">Untranslated detail (counts, file names), or null</param>
    void StepProgress(string step, double? fraction, string? stage = null, string? detail = null);

    void StepFinished(string step, StepStatus status);
}

public sealed class NullProgress : IBuildProgress
{
    public static readonly NullProgress Instance = new();
    public void StepStarted(string step) { }
    public void StepProgress(string step, double? fraction, string? stage = null, string? detail = null) { }
    public void StepFinished(string step, StepStatus status) { }
}

public abstract class BuildStep
{
    /// <summary>Stable id (state.json stamps, UI texts): export, bake, project, unity, pico</summary>
    public abstract string Id { get; }

    /// <summary>
    /// Hash of everything the step's result depends on, including the stamps of the steps it builds on. The pipeline
    /// skips a step whose stamp matches the one it last finished with.
    /// </summary>
    public abstract string Stamp(BuildContext context, IReadOnlyDictionary<string, string> earlierStamps);

    /// <summary>Whether the step is part of this build at all (the pico step only is for Pico)</summary>
    public virtual bool AppliesTo(BuildContext context) => true;

    /// <summary>Whether the step's result is still on disk (a stamp alone is not enough if the folder was deleted)</summary>
    public virtual bool ResultExists(BuildContext context) => true;

    public abstract Task RunAsync(BuildContext context, CancellationToken ct);

    protected void Report(BuildContext context, double? fraction, string? stage = null, string? detail = null) =>
        context.Progress.StepProgress(Id, fraction, stage, detail);
}
