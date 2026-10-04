using System.Collections.Concurrent;
using Avalonia.Collections;
using Avalonia.Threading;
using CommunityToolkit.Mvvm.ComponentModel;

namespace RepoQuest.Builder.App.ViewModels;

public sealed record LogLine(DateTime Time, LogLevel Level, string Text)
{
    /// <summary>As in the log file: time, then WARN/ERROR, debug lines indented</summary>
    public string Display => $"{Time:HH:mm:ss} " + Level switch
    {
        LogLevel.Debug => "  " + Text,
        LogLevel.Warning => "WARN " + Text,
        LogLevel.Error => "ERROR " + Text,
        _ => Text,
    };

    public bool IsDebug => Level == LogLevel.Debug;
    public bool IsWarning => Level == LogLevel.Warning;
    public bool IsError => Level == LogLevel.Error;
}

/// <summary>
/// The live log panel: the last lines of <see cref="Log"/>. Lines arrive on any thread and in bursts (Unity and
/// AssetRipper print thousands), so they are queued and moved to the list four times a second. Debug lines are kept
/// apart so a burst of them never pushes the important lines out.
/// </summary>
public sealed class LogViewModel : ObservableObject
{
    private const int MaxLines = 1000;

    private readonly ConcurrentQueue<LogLine> incoming = new();
    private readonly Queue<LogLine> important = new();
    private readonly Queue<LogLine> all = new();
    private readonly DispatcherTimer timer;
    private bool showDebug;

    public LogViewModel()
    {
        Log.Message += OnMessage;
        timer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(250) };
        timer.Tick += (_, _) => Drain();
        timer.Start();
    }

    /// <summary>What the panel shows: Info and above, or everything</summary>
    public AvaloniaList<LogLine> Lines { get; } = new();

    public bool ShowDebug
    {
        get => showDebug;
        set
        {
            if (!SetProperty(ref showDebug, value))
                return;
            Lines.Clear();
            Lines.AddRange(value ? all : important);
            Appended?.Invoke();
        }
    }

    /// <summary>New lines are in <see cref="Lines"/> (the view scrolls to them)</summary>
    public event Action? Appended;

    public string Text => string.Join(Environment.NewLine, Lines.Select(l => l.Display));

    public void Stop()
    {
        Log.Message -= OnMessage;
        timer.Stop();
    }

    private void OnMessage(LogLevel level, string text)
    {
        var now = DateTime.Now;
        foreach (var line in text.Split('\n'))
            incoming.Enqueue(new LogLine(now, level, line.TrimEnd('\r')));
    }

    private void Drain()
    {
        if (incoming.IsEmpty)
            return;
        var added = new List<LogLine>();
        while (incoming.TryDequeue(out var line))
        {
            Keep(all, line);
            if (line.Level != LogLevel.Debug)
                Keep(important, line);
            if (showDebug || line.Level != LogLevel.Debug)
                added.Add(line);
        }
        if (added.Count == 0)
            return;
        if (added.Count > MaxLines)
            added.RemoveRange(0, added.Count - MaxLines);
        var excess = Lines.Count + added.Count - MaxLines;
        if (excess > 0)
            Lines.RemoveRange(0, excess);
        Lines.AddRange(added);
        Appended?.Invoke();
    }

    private static void Keep(Queue<LogLine> lines, LogLine line)
    {
        lines.Enqueue(line);
        if (lines.Count > MaxLines)
            lines.Dequeue();
    }
}
