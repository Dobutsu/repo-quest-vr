using System.ComponentModel;
using System.Diagnostics;

namespace RepoQuest.Builder.Setup;

/// <summary>
/// Windows Defender scans every file a build writes, and Unity, IL2CPP and Gradle write tens of thousands into the
/// workspace. The player can exclude the workspace from real-time scanning (a machine setting that needs administrator
/// rights; asked once, see <see cref="BuilderService"/>). Non-administrators cannot read the exclusion list, so the
/// answer lives in the workspace's state, not in a check.
/// </summary>
public static class WindowsDefender
{
    /// <summary>Adds <paramref name="folder"/> to Defender's exclusions through an elevated PowerShell; false when
    /// the prompt was declined or Defender refused (switched off, replaced by another antivirus, managed by policy)</summary>
    public static Task<bool> AddExclusionAsync(string folder, CancellationToken ct) =>
        RunElevatedAsync("Add-MpPreference", folder, ct);

    /// <summary>Takes <paramref name="folder"/> off Defender's exclusions again</summary>
    public static Task<bool> RemoveExclusionAsync(string folder, CancellationToken ct) =>
        RunElevatedAsync("Remove-MpPreference", folder, ct);

    private static async Task<bool> RunElevatedAsync(string cmdlet, string folder, CancellationToken ct)
    {
        if (!OperatingSystem.IsWindows())
            return false;
        var path = folder.Replace("'", "''");
        var start = new ProcessStartInfo("powershell.exe",
            $"-NoProfile -NonInteractive -Command \"try {{ {cmdlet} -ExclusionPath '{path}' -ErrorAction Stop; exit 0 }} catch {{ exit 2 }}\"")
        {
            UseShellExecute = true,
            Verb = "runas",
            WindowStyle = ProcessWindowStyle.Hidden,
        };
        try
        {
            using var process = Process.Start(start);
            if (process is null)
                return false;
            await process.WaitForExitAsync(ct);
            if (process.ExitCode == 0)
            {
                Log.Info($"Defender: {cmdlet} {folder}");
                return true;
            }
            Log.Warn($"Defender: {cmdlet} failed (exit code {process.ExitCode}); Defender may be off or managed by another antivirus");
        }
        catch (Win32Exception e) when (e.NativeErrorCode == 1223)
        {
            Log.Warn("Defender: the administrator prompt was declined");
        }
        catch (Win32Exception e)
        {
            Log.Warn($"Defender: {e.Message}");
        }
        return false;
    }
}
