using CommunityToolkit.Mvvm.ComponentModel;
using RepoQuest.Builder.App.Localization;
using RepoQuest.Builder.Pipeline;

namespace RepoQuest.Builder.App.ViewModels;

/// <summary>One row of the build list (a step of <see cref="BuilderService.Steps"/>): its state, stage and time</summary>
public sealed class StepViewModel : ObservableObject
{
    private DateTime? started;
    private TimeSpan? duration;

    public StepViewModel(string id)
    {
        Id = id;
    }

    public string Id { get; }
    public StepStatus Status { get; private set; } = StepStatus.Pending;
    public double? Fraction { get; private set; }
    /// <summary>A stage code from Core (see Strings: stage.&lt;step&gt;.&lt;stage&gt;)</summary>
    public string? Stage { get; private set; }
    /// <summary>Untranslated detail from Core: counts, sub-step names, Unity Hub output</summary>
    public string? Detail { get; private set; }

    public string Title => Loc.T($"step.{Id}.title");
    public string About => Loc.T($"step.{Id}.about");

    public bool IsRunning => Status == StepStatus.Running;
    public bool IsDone => Status == StepStatus.Done;
    public bool IsUpToDate => Status == StepStatus.UpToDate;
    public bool IsFailed => Status == StepStatus.Failed;
    public bool IsCancelled => Status == StepStatus.Cancelled;
    public bool IsIndeterminate => IsRunning && Fraction is null;
    public double Percent => Math.Clamp((Fraction ?? 0) * 100, 0, 100);

    /// <summary>The stage while the step runs (with the raw detail), its outcome otherwise</summary>
    public string StateText
    {
        get
        {
            if (!IsRunning)
                return Loc.T($"status.{Status}");
            // A stage Core added later than these texts is shown as its code rather than not at all
            var stage = Stage is null ? Loc.T("status.Running") : Loc.TryT($"stage.{Id}.{Stage}") ?? Stage;
            return Detail is { Length: > 0 } detail ? $"{stage} · {detail}" : stage;
        }
    }

    /// <summary>How long the step has been running, or took (empty for steps that did not run)</summary>
    public string ElapsedText => (duration ?? (started is { } start ? DateTime.UtcNow - start : null)) is { } time
        ? Format(time)
        : "";

    public static string Format(TimeSpan time) =>
        time.TotalHours >= 1 ? $"{(int)time.TotalHours}:{time:mm\\:ss}" : time.ToString(@"m\:ss");

    /// <summary>Back to a state that is not from a running build (before a build, from the builder's status)</summary>
    public void Reset(StepStatus status)
    {
        Status = status;
        Fraction = null;
        Stage = null;
        Detail = null;
        started = null;
        duration = null;
        Refresh();
    }

    public void Start()
    {
        Reset(StepStatus.Running);
        started = DateTime.UtcNow;
    }

    public void Report(double? fraction, string? stage, string? detail)
    {
        // Progress<T> callbacks may arrive after the step finished: they no longer matter
        if (!IsRunning)
            return;
        Fraction = fraction;
        Stage = stage ?? Stage;
        Detail = detail;
        Refresh();
    }

    public void Finish(StepStatus status)
    {
        duration = started is { } start ? DateTime.UtcNow - start : null;
        Status = status;
        Fraction = null;
        Refresh();
    }

    /// <summary>Every second while running: the clock</summary>
    public void Tick()
    {
        if (IsRunning)
            OnPropertyChanged(nameof(ElapsedText));
    }

    /// <summary>All texts again (the language changed, or the state did: the row has few bindings)</summary>
    public void Refresh() => OnPropertyChanged(string.Empty);
}
