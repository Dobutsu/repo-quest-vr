using System.Diagnostics;
using System.Runtime.InteropServices;

namespace RepoQuest.Builder;

/// <summary>Keeps the computer from going to sleep during a build (an hour-long Unity run on a laptop)</summary>
public sealed class SleepBlocker : IDisposable
{
    private readonly Process? caffeinate;
    private readonly Thread? holder;
    private readonly ManualResetEventSlim release = new();

    private SleepBlocker()
    {
        if (OperatingSystem.IsWindows())
        {
            // The request belongs to the thread that made it: a dedicated thread holds it until disposal
            holder = new Thread(() =>
            {
                SetThreadExecutionState(EsContinuous | EsSystemRequired);
                release.Wait();
                SetThreadExecutionState(EsContinuous);
            }) { IsBackground = true, Name = "sleep blocker" };
            holder.Start();
        }
        else if (OperatingSystem.IsMacOS())
        {
            try
            {
                caffeinate = Process.Start(new ProcessStartInfo("caffeinate", ["-i", "-w", Environment.ProcessId.ToString()])
                {
                    UseShellExecute = false,
                    CreateNoWindow = true,
                });
            }
            catch (Exception e)
            {
                Log.Debug($"caffeinate: {e.Message}");
            }
        }
    }

    public static SleepBlocker Start() => new();

    public void Dispose()
    {
        release.Set();
        try
        {
            if (caffeinate is { HasExited: false })
                caffeinate.Kill();
        }
        catch (InvalidOperationException)
        {
        }
        caffeinate?.Dispose();
    }

    private const uint EsContinuous = 0x80000000;
    private const uint EsSystemRequired = 0x00000001;

    [DllImport("kernel32.dll")]
    private static extern uint SetThreadExecutionState(uint flags);
}
