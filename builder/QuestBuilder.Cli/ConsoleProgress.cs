using RepoQuest.Builder.Pipeline;

namespace RepoQuest.Builder.Cli;

/// <summary>Build progress as console lines: steps as they start and end, progress at most every few seconds</summary>
internal sealed class ConsoleProgress : IBuildProgress
{
    private readonly Dictionary<string, DateTime> started = new();
    private DateTime lastLine = DateTime.MinValue;
    private string? lastText;

    public ConsoleProgress()
    {
        Log.Message += (level, text) =>
        {
            if (level >= LogLevel.Warning)
                Console.WriteLine($"{(level == LogLevel.Error ? "error" : "warning")}: {text}");
        };
    }

    public void StepStarted(string step)
    {
        started[step] = DateTime.UtcNow;
        Console.WriteLine($"[{step}] started {DateTime.Now:HH:mm:ss}");
    }

    public void StepProgress(string step, double? fraction, string? stage = null, string? detail = null)
    {
        var text = $"[{step}] {stage}{(detail is null ? "" : " " + detail)}{(fraction is { } f ? $" {f:P0}" : "")}";
        if (text == lastText || DateTime.UtcNow - lastLine < TimeSpan.FromSeconds(5))
            return;
        lastText = text;
        lastLine = DateTime.UtcNow;
        Console.WriteLine($"{text}  ({Elapsed(step)})");
    }

    public void StepFinished(string step, StepStatus status)
    {
        Console.WriteLine($"[{step}] {status.ToString().ToLowerInvariant()}{(started.ContainsKey(step) ? $" after {Elapsed(step)}" : "")}");
    }

    private string Elapsed(string step) =>
        started.TryGetValue(step, out var start) ? (DateTime.UtcNow - start).ToString(@"m\:ss") : "";
}
