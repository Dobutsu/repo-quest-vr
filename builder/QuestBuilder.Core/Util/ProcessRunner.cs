using System.Collections.Concurrent;
using System.Diagnostics;
using System.Text;

namespace RepoQuest.Builder;

public sealed class ProcessSpec
{
    public required string FileName { get; init; }
    public IReadOnlyList<string> Arguments { get; init; } = [];
    public string? WorkingDirectory { get; init; }
    /// <summary>Added to (or, with a null value, removed from) the builder's own environment</summary>
    public IReadOnlyDictionary<string, string?> Environment { get; init; } = new Dictionary<string, string?>();
    /// <summary>Prefix of the output lines in the log ("[python] ..."); null: output is not logged</summary>
    public string? LogPrefix { get; init; } = "";

    public override string ToString() =>
        string.Join(' ', new[] { FileName }.Concat(Arguments).Select(a => a.Contains(' ') || a.Length == 0 ? $"\"{a}\"" : a));
}

public sealed record ProcessResult(int ExitCode, IReadOnlyList<string> Tail)
{
    public string TailText => string.Join('\n', Tail);
}

/// <summary>Runs tools (Unity, AssetRipper, Python, uv, adb, the .NET tools) and collects their output</summary>
public static class ProcessRunner
{
    private const int TailLines = 60;
    private static readonly ConcurrentDictionary<int, Process> Running = new();

    public static async Task<ProcessResult> RunAsync(ProcessSpec spec, Action<string>? onLine = null,
        CancellationToken ct = default)
    {
        var psi = new ProcessStartInfo(spec.FileName)
        {
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            RedirectStandardInput = true,
            CreateNoWindow = true,
            StandardOutputEncoding = Encoding.UTF8,
            StandardErrorEncoding = Encoding.UTF8,
            WorkingDirectory = spec.WorkingDirectory ?? "",
        };
        foreach (var argument in spec.Arguments)
            psi.ArgumentList.Add(argument);
        foreach (var (key, value) in spec.Environment)
        {
            if (value is null)
                psi.Environment.Remove(key);
            else
                psi.Environment[key] = value;
        }

        Log.Debug($"$ {spec}");
        var tail = new Queue<string>();
        void OnLine(string? line)
        {
            if (line is null)
                return;
            lock (tail)
            {
                tail.Enqueue(line);
                if (tail.Count > TailLines)
                    tail.Dequeue();
            }
            if (spec.LogPrefix is not null)
                Log.Debug(spec.LogPrefix + line);
            onLine?.Invoke(line);
        }

        using var process = new Process { StartInfo = psi };
        process.OutputDataReceived += (_, e) => OnLine(e.Data);
        process.ErrorDataReceived += (_, e) => OnLine(e.Data);
        try
        {
            process.Start();
        }
        catch (Exception e)
        {
            throw new BuildException($"Could not start {Path.GetFileName(spec.FileName)}: {e.Message}", spec.ToString(), e);
        }
        process.StandardInput.Close();  // tools that ask questions get EOF instead of waiting forever
        Running[process.Id] = process;
        process.BeginOutputReadLine();
        process.BeginErrorReadLine();
        try
        {
            await process.WaitForExitAsync(ct);
            process.WaitForExit();  // drains the output events
        }
        catch (OperationCanceledException)
        {
            Kill(process);
            throw;
        }
        finally
        {
            Running.TryRemove(process.Id, out _);
        }

        lock (tail)
        {
            return new ProcessResult(process.ExitCode, tail.ToList());
        }
    }

    /// <summary>Like <see cref="RunAsync"/>, but a non-zero exit code throws with <paramref name="failure"/></summary>
    public static async Task<ProcessResult> RunCheckedAsync(ProcessSpec spec, string failure,
        Action<string>? onLine = null, CancellationToken ct = default)
    {
        var result = await RunAsync(spec, onLine, ct);
        if (result.ExitCode != 0)
            throw new BuildException($"{failure} (exit code {result.ExitCode})", $"{spec}\n{result.TailText}");
        return result;
    }

    /// <summary>Stops every tool the builder started (closing the window, cancel)</summary>
    public static void KillAll()
    {
        foreach (var process in Running.Values)
            Kill(process);
    }

    private static void Kill(Process process)
    {
        try
        {
            if (!process.HasExited)
                process.Kill(entireProcessTree: true);
        }
        catch (Exception e)
        {
            Log.Debug($"could not stop process {process.Id}: {e.Message}");
        }
    }
}
