namespace RepoQuest.Builder.Quest;

public enum InstallOutcome { Installed, ReinstalledWithData }

/// <summary>
/// Installs the APK on the headset over USB. An APK signed by another computer (Android's debug keys differ per
/// machine) cannot replace the installed one; then the game's data (saves, RepoXR settings, Steam login, cosmetics) is
/// copied to the computer, the old app removed, the new one installed and the data put back.
/// </summary>
public static class QuestInstaller
{
    public const string Package = "io.repoxr.quest";
    public const string DataDir = "/sdcard/Android/data/" + Package + "/files";

    public static async Task<InstallOutcome> InstallAsync(Adb adb, AdbDevice device, string apk, string backupRoot,
        IProgress<string>? status, CancellationToken ct)
    {
        if (!device.Ready)
            throw new BuildException(device.Unauthorized
                ? "The headset has not allowed USB debugging yet: put it on and accept the prompt"
                : $"The headset is {device.State}") { Code = device.Unauthorized ? "quest-unauthorized" : "quest-offline" };

        // An asleep headset accepts the APK but does not install it until it wakes (seen: ten minutes of waiting)
        await adb.ShellAsync(device.Serial, "input keyevent KEYCODE_WAKEUP", ct);

        if (await adb.IsRunningAsync(device.Serial, Package, ct))
        {
            status?.Report("stop");
            await adb.ShellAsync(device.Serial, $"am force-stop {Package}", ct);
        }

        status?.Report("install");
        var result = await adb.RunAsync(device.Serial, ["install", "-r", apk], ct);
        var output = result.TailText;
        if (result.ExitCode == 0 && output.Contains("Success", StringComparison.Ordinal))
            return InstallOutcome.Installed;

        if (output.Contains("INSTALL_FAILED_UPDATE_INCOMPATIBLE") || output.Contains("signatures do not match"))
        {
            await ReinstallKeepingDataAsync(adb, device, apk, backupRoot, status, ct);
            return InstallOutcome.ReinstalledWithData;
        }
        if (output.Contains("INSTALL_FAILED_INSUFFICIENT_STORAGE"))
            throw new BuildException("The headset has no room for the game (about 4 GB needed)", output) { Code = "quest-storage" };
        if (output.Contains("INSTALL_FAILED_VERSION_DOWNGRADE"))
        {
            // An older port version than the one installed: allowed, the player chose to build it
            var downgrade = await adb.RunAsync(device.Serial, ["install", "-r", "-d", apk], ct);
            if (downgrade.ExitCode == 0 && downgrade.TailText.Contains("Success", StringComparison.Ordinal))
                return InstallOutcome.Installed;
            output = downgrade.TailText;
        }
        throw new BuildException("Installing on the headset failed", output) { Code = "quest-install" };
    }

    /// <summary>
    /// Removes the game from the headset; its data (saves, RepoXR settings, Steam login, cosmetics), which Android
    /// deletes with the app, is copied to a new folder in <paramref name="backupRoot"/> first. Returns that folder, or
    /// null when the headset had no data. A headset without the game is fine.
    /// </summary>
    public static async Task<string?> UninstallAsync(Adb adb, AdbDevice device, string backupRoot, IProgress<string>? status,
        CancellationToken ct)
    {
        if (!device.Ready)
            throw new BuildException(device.Unauthorized
                ? "The headset has not allowed USB debugging yet: put it on and accept the prompt"
                : $"The headset is {device.State}") { Code = device.Unauthorized ? "quest-unauthorized" : "quest-offline" };
        await adb.ShellAsync(device.Serial, "input keyevent KEYCODE_WAKEUP", ct);
        if (await adb.IsRunningAsync(device.Serial, Package, ct))
        {
            status?.Report("stop");
            await adb.ShellAsync(device.Serial, $"am force-stop {Package}", ct);
        }

        var (backup, hasData) = await BackupDataAsync(adb, device, backupRoot, status, ct);
        status?.Report("uninstall");
        var result = await adb.RunAsync(device.Serial, ["uninstall", Package], ct);
        var output = result.TailText;
        if (!output.Contains("Success", StringComparison.Ordinal) && !output.Contains("Unknown package", StringComparison.Ordinal) &&
            !output.Contains("DELETE_FAILED_INTERNAL_ERROR", StringComparison.Ordinal))
            throw new BuildException("Removing the game from the headset failed", output) { Code = "quest-uninstall" };
        if (hasData)
            return backup;
        FileOps.DeleteDirectory(backup);
        return null;
    }

    /// <summary>Copies the game's data from the headset into a new dated folder in <paramref name="backupRoot"/></summary>
    private static async Task<(string Backup, bool HasData)> BackupDataAsync(Adb adb, AdbDevice device, string backupRoot,
        IProgress<string>? status, CancellationToken ct)
    {
        status?.Report("backup");
        var backup = Path.Combine(backupRoot, $"quest-data-{DateTime.Now:yyyyMMdd-HHmmss}");
        Directory.CreateDirectory(backup);
        var hasData = (await adb.ShellAsync(device.Serial, $"ls {DataDir}", ct)).Length > 0;
        if (hasData)
        {
            var pull = await adb.RunAsync(device.Serial, ["pull", DataDir, backup], ct);
            if (pull.ExitCode != 0)
                throw new BuildException("Could not copy the game's data from the headset; nothing was changed",
                    pull.TailText) { Code = "quest-backup" };
            Log.Info($"headset data saved in {backup}");
        }
        return (backup, hasData);
    }

    private static async Task ReinstallKeepingDataAsync(Adb adb, AdbDevice device, string apk, string backupRoot,
        IProgress<string>? status, CancellationToken ct)
    {
        var (backup, hasData) = await BackupDataAsync(adb, device, backupRoot, status, ct);

        status?.Report("uninstall");
        await adb.RunAsync(device.Serial, ["uninstall", Package], ct);
        status?.Report("install");
        var install = await adb.RunAsync(device.Serial, ["install", apk], ct);
        if (install.ExitCode != 0 || !install.TailText.Contains("Success", StringComparison.Ordinal))
            throw new BuildException($"Installing on the headset failed; the game's data is saved in {backup}",
                install.TailText) { Code = "quest-install" };

        if (hasData)
        {
            status?.Report("restore");
            await adb.ShellAsync(device.Serial, $"mkdir -p {DataDir}", ct);
            // adb pull put the folder itself into the backup: push its content back
            var restore = await adb.RunAsync(device.Serial, ["push", Path.Combine(backup, "files") + "/.", DataDir], ct);
            if (restore.ExitCode != 0)
                throw new BuildException($"The game is installed, but its data could not be put back; it is saved in {backup}",
                    restore.TailText) { Code = "quest-restore" };
            // Pushed files belong to the shell user; the game must be able to replace them when it saves
            await adb.ShellAsync(device.Serial, $"chmod -R 777 {DataDir}", ct);
        }
    }
}
