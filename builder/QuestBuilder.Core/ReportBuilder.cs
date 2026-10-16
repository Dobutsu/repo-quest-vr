using System.Diagnostics;
using System.IO.Compression;
using System.Text;
using RepoQuest.Builder.Game;
using RepoQuest.Builder.Setup;

namespace RepoQuest.Builder;

/// <summary>
/// "Create report": a zip with the logs and a summary of the machine and the build, for whoever helps when a build fails.
/// Only logs and text; never game files, the APK or Steam logins.
/// </summary>
public static class ReportBuilder
{
    private const long MaxLogBytes = 20L << 20;

    public static string Create(Workspace workspace, string targetDir, string? gamePath, string kitVersion)
    {
        Directory.CreateDirectory(targetDir);
        var path = Path.Combine(targetDir, $"RepoQuest-report-{DateTime.Now:yyyyMMdd-HHmmss}.zip");
        using var zip = ZipFile.Open(path, ZipArchiveMode.Create);

        var summary = new StringBuilder();
        summary.AppendLine($"REPO Quest Builder {Platform.BuilderVersion}, kit {kitVersion}");
        summary.AppendLine($"OS: {Environment.OSVersion.VersionString} ({Platform.Rid}), {Environment.ProcessorCount} CPUs");
        summary.AppendLine($"Memory: {GC.GetGCMemoryInfo().TotalAvailableMemoryBytes >> 30} GB");
        summary.AppendLine($"Workspace: {workspace.Root}, free {FreeGb(workspace.Root)} GB");
        if (Platform.IsWindows)
            summary.Append(WindowsDetails(workspace.Root));
        if (gamePath is not null && GameInstall.IsValid(gamePath))
        {
            var game = new GameInstall(gamePath);
            summary.AppendLine($"Game: {game.Path}, {game.Version() ?? "?"}, Steam build {game.SteamBuildId() ?? "?"}, " +
                               $"Assembly-CSharp {game.AssemblyHash()}");
        }
        var unity = UnityInstall.Find("2022.3.62f3");
        summary.AppendLine($"Unity: {unity?.Root ?? "not found"}{(unity is { HasAndroid: true } ? " (Android)" : "")}");
        summary.AppendLine($"Unity Hub: {UnityHub.Executable() ?? "not found"}");
        if (File.Exists(workspace.StatePath))
            summary.AppendLine().AppendLine("state.json:").AppendLine(File.ReadAllText(workspace.StatePath));
        Add(zip, "summary.txt", summary.ToString());

        if (Log.FilePath is { } builderLog && File.Exists(builderLog) &&
            !Path.GetFullPath(builderLog).StartsWith(Path.GetFullPath(workspace.Logs), StringComparison.OrdinalIgnoreCase))
            AddFileTail(zip, "builder.log", builderLog);
        if (Directory.Exists(workspace.Logs))
        {
            foreach (var log in Directory.EnumerateFiles(workspace.Logs, "*", SearchOption.TopDirectoryOnly))
                AddFileTail(zip, "logs/" + Path.GetFileName(log), log);
        }
        var bakeReport = Path.Combine(workspace.Baked, "bake-report.json");
        if (File.Exists(bakeReport))
            AddFileTail(zip, "bake-report.json", bakeReport);
        return path;
    }

    private static void Add(ZipArchive zip, string name, string text)
    {
        using var writer = new StreamWriter(zip.CreateEntry(name).Open(), new UTF8Encoding(false));
        writer.Write(text);
    }

    /// <summary>The whole file, or its end if it is very large (Unity logs)</summary>
    private static void AddFileTail(ZipArchive zip, string name, string path)
    {
        try
        {
            using var source = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
            if (source.Length > MaxLogBytes)
                source.Position = source.Length - MaxLogBytes;
            using var target = zip.CreateEntry(name, CompressionLevel.Optimal).Open();
            source.CopyTo(target);
        }
        catch (IOException e)
        {
            Add(zip, name + ".error.txt", e.Message);
        }
    }

    /// <summary>
    /// What makes a Windows build slow, read without administrator rights: the CPU, the workspace's disk (an HDD or a
    /// USB drive), Defender's real-time scanning and other antivirus products
    /// </summary>
    private const string WindowsScript = """
        $ErrorActionPreference = 'Stop'
        [Console]::OutputEncoding = [Text.Encoding]::UTF8
        try { "CPU: " + ((Get-CimInstance Win32_Processor | ForEach-Object { $_.Name.Trim() }) -join '; ') } catch { "CPU: ? ($_)" }
        try {
            $disk = Get-Partition -DriveLetter '{DRIVE}' | Get-Disk
            $physical = Get-PhysicalDisk | Where-Object { $_.DeviceId -eq "$($disk.Number)" }
            "Workspace disk: $($disk.FriendlyName), $($physical.MediaType), $($disk.BusType), $([math]::Round($disk.Size / 1GB)) GB"
        } catch { "Workspace disk: ? ($_)" }
        try {
            $mp = Get-MpComputerStatus
            "Defender: running mode $($mp.AMRunningMode), real-time protection $($mp.RealTimeProtectionEnabled)"
        } catch { "Defender: ? ($_)" }
        try {
            $av = Get-CimInstance -Namespace root/SecurityCenter2 -ClassName AntiVirusProduct | ForEach-Object { $_.displayName }
            "Antivirus: " + ($av -join ', ')
        } catch { "Antivirus: ? ($_)" }
        """;

    private static string WindowsDetails(string workspace)
    {
        var drive = Path.GetPathRoot(workspace)?.TrimEnd('\\', ':') is [var letter] ? letter.ToString() : "C";
        var script = WindowsScript.Replace("{DRIVE}", drive);
        var start = new ProcessStartInfo("powershell.exe")
        {
            ArgumentList = { "-NoProfile", "-NonInteractive", "-EncodedCommand", Convert.ToBase64String(Encoding.Unicode.GetBytes(script)) },
            RedirectStandardOutput = true,
            StandardOutputEncoding = new UTF8Encoding(false),
            UseShellExecute = false,
            CreateNoWindow = true,
        };
        try
        {
            using var process = Process.Start(start)!;
            var output = process.StandardOutput.ReadToEndAsync();
            if (!process.WaitForExit(30_000))
            {
                process.Kill(entireProcessTree: true);
                return "Windows details: PowerShell did not answer in 30 s\n";
            }
            return output.Result.Replace("\r\n", "\n");
        }
        catch (Exception e)
        {
            return $"Windows details: {e.Message}\n";
        }
    }

    private static long FreeGb(string path)
    {
        try
        {
            return FileOps.FreeSpace(path) >> 30;
        }
        catch (Exception)
        {
            return -1;
        }
    }
}
