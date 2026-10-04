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
