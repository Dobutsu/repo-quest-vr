using System.ComponentModel;
using System.Diagnostics;
using Microsoft.Win32;

namespace RepoQuest.Builder.Setup;

/// <summary>
/// Windows' 260-character path limit: Unity's Android build writes files ~240 characters deep into the project's
/// Library, and the NDK's tools fail on longer paths unless Windows allows them (LongPathsEnabled, a machine setting
/// that needs administrator rights; the player turns it on in the consent dialog)
/// </summary>
public static class WindowsLongPaths
{
    private const string Key = @"HKEY_LOCAL_MACHINE\SYSTEM\CurrentControlSet\Control\FileSystem";

    public static bool IsEnabled()
    {
        if (!OperatingSystem.IsWindows())
            return true;
        try
        {
            return Registry.GetValue(Key, "LongPathsEnabled", 0) is int value && value == 1;
        }
        catch (Exception e)
        {
            Log.Debug($"LongPathsEnabled: {e.Message}");
            return false;
        }
    }

    /// <summary>Turns the setting on through an elevated reg.exe (Windows asks for administrator rights)</summary>
    public static async Task EnableAsync(CancellationToken ct)
    {
        if (IsEnabled())
            return;
        var start = new ProcessStartInfo("reg.exe",
            @"add HKLM\SYSTEM\CurrentControlSet\Control\FileSystem /v LongPathsEnabled /t REG_DWORD /d 1 /f")
        {
            UseShellExecute = true,
            Verb = "runas",
            WindowStyle = ProcessWindowStyle.Hidden,
        };
        try
        {
            using var process = Process.Start(start);
            if (process is not null)
                await process.WaitForExitAsync(ct);
        }
        catch (Win32Exception e) when (e.NativeErrorCode == 1223)
        {
            Log.Warn("long paths: the administrator prompt was declined");
        }
        if (!IsEnabled())
            Log.Warn("long paths are still off; a build may fail on long file names");
    }
}
