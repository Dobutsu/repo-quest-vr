using System.Text;
using System.Text.RegularExpressions;
using RepoQuest.Builder.Setup;

namespace RepoQuest.Builder.Pipeline;

/// <summary>
/// Imports the project and builds the APK in two editor runs (RepoQuest.Editor.QuestBuild.Prepare and .BuildApk):
/// settings changes schedule a domain reload, and BuildPlayer refuses to run until it has happened.
/// </summary>
public sealed partial class UnityBuildStep : BuildStep
{
    /// <summary>The APK build without earlier builds to go by: a new Library compiles every shader (measured on an
    /// M-series Mac: ~35 minutes for the first build, ~20 for later ones)</summary>
    private const double FirstBuildMinutes = 35;
    private const double DefaultBuildMinutes = 20;

    public override string Id => "unity";

    public override string Stamp(BuildContext context, IReadOnlyDictionary<string, string> earlierStamps)
    {
        var options = context.Options;
        return Hashing.Combine(Id, earlierStamps.GetValueOrDefault("project"), options.VersionName,
            options.VersionCode.ToString(), options.Development.ToString(), options.Profiling.ToString(),
            options.ApkPath(context.Workspace));
    }

    public override bool ResultExists(BuildContext context) => File.Exists(context.Options.ApkPath(context.Workspace));

    public override async Task RunAsync(BuildContext context, CancellationToken ct)
    {
        var unity = context.RequireUnity();
        var workspace = context.Workspace;
        var options = context.Options;
        Directory.CreateDirectory(workspace.Build);
        Directory.CreateDirectory(workspace.Logs);

        // Import artifacts made before the game assembly was renamed (Assembly-CSharp -> REPO.Game) kept their
        // MonoBehaviours without data. A Library that predates the builder gets those assets reimported once; a new
        // Library is made after the rename and needs nothing
        var freshLibrary = !Directory.Exists(workspace.Library);
        var reimportList = freshLibrary || File.Exists(workspace.ReimportMarker) ? null : WriteReimportList(workspace);

        var prepareLog = Path.Combine(workspace.Logs, "unity-prepare.log");
        string[] prepare =
        [
            "-batchmode", "-projectPath", workspace.Project, "-buildTarget", "Android",
            "-executeMethod", "RepoQuest.Editor.QuestBuild.Prepare",
            "-addressablesEntries", workspace.AddressablesEntries,
            .. reimportList is null ? Array.Empty<string>() : ["-reimportList", reimportList],
            "-logFile", prepareLog,
        ];
        var expectedImports = freshLibrary ? CountAssets(workspace.Project) : 0;
        await RunUnityAsync(context, unity, prepare, prepareLog, new ImportProgress(this, context, expectedImports), ct);
        if (freshLibrary || (reimportList is not null && File.ReadAllText(prepareLog).Contains("[QuestBuild] reimporting")))
            FileOps.WriteText(workspace.ReimportMarker, "");

        var apk = options.ApkPath(workspace);
        var buildLog = Path.Combine(workspace.Logs, "unity-build.log");
        string[] build =
        [
            "-batchmode", "-projectPath", workspace.Project, "-buildTarget", "Android",
            "-executeMethod", "RepoQuest.Editor.QuestBuild.BuildApk",
            "-apkPath", apk,
            "-versionName", options.VersionName,
            "-versionCode", options.VersionCode.ToString(),
            .. options.Development ? new[] { "-developmentBuild" } : [],
            .. options.Profiling ? new[] { "-profiling" } : [],
            "-logFile", buildLog,
        ];
        var started = DateTime.UtcNow;
        await RunUnityAsync(context, unity, build, buildLog,
            new BuildProgress(this, context, ExpectedBuildTime(context.State, freshLibrary)), ct);
        var log = File.ReadAllText(buildLog);
        foreach (var line in log.Split('\n').Where(l => l.StartsWith("[QuestBuild]", StringComparison.Ordinal)))
            Log.Info(line.TrimEnd());
        if (!log.Contains("[QuestBuild] result: Succeeded") || !File.Exists(apk))
            throw Failure(buildLog, "The APK build failed");

        var state = context.State;
        // A first build (every shader compiled) says little about the next ones
        if (!freshLibrary)
        {
            state.UnityBuildDurations.Add((int)(DateTime.UtcNow - started).TotalSeconds);
            if (state.UnityBuildDurations.Count > 10)
                state.UnityBuildDurations.RemoveAt(0);
        }
        state.LastApk = apk;
        state.LastApkVersion = options.VersionName;
        state.LastApkGame = context.Game.AssemblyHash();
        state.LastApkTime = DateTime.Now;
        context.SaveState();
    }

    /// <summary>Assets that use game scripts (prefabs, assets, controllers referencing REPO.Game*.dll)</summary>
    private static string WriteReimportList(Workspace workspace)
    {
        var plugins = Path.Combine(workspace.Project, "Assets", "Plugins");
        var guids = Directory.EnumerateFiles(plugins, "REPO.Game*.dll.meta")
            .Select(meta => GuidRegex().Match(File.ReadAllText(meta)).Groups[1].Value)
            .Where(g => g.Length > 0).ToList();
        var assets = Path.Combine(workspace.Project, "Assets");
        var paths = Directory.EnumerateFiles(assets, "*", SearchOption.AllDirectories)
            .Where(f => f.EndsWith(".prefab") || f.EndsWith(".asset") || f.EndsWith(".controller"))
            .Where(f =>
            {
                var text = File.ReadAllText(f);
                return guids.Any(g => text.Contains("guid: " + g, StringComparison.Ordinal));
            })
            .Select(f => Path.GetRelativePath(workspace.Project, f).Replace('\\', '/'))
            .Order(StringComparer.Ordinal).ToList();
        var list = Path.Combine(workspace.Logs, "unity-reimport.txt");
        FileOps.WriteText(list, string.Join('\n', paths) + (paths.Count > 0 ? "\n" : ""));
        Log.Info($"reimporting {paths.Count} assets that use game scripts (once per Library)");
        return list;
    }

    /// <summary>What a first import logs: "Start importing" comes about 1.5 times per asset (measured: 22,571 lines
    /// for 14,734 assets)</summary>
    private static int CountAssets(string project) =>
        Directory.EnumerateFiles(Path.Combine(project, "Assets"), "*.meta", SearchOption.AllDirectories).Count() * 3 / 2;

    private static TimeSpan ExpectedBuildTime(BuildState state, bool freshLibrary)
    {
        if (freshLibrary)
            return TimeSpan.FromMinutes(FirstBuildMinutes);
        if (state.UnityBuildDurations.Count == 0)
            return TimeSpan.FromMinutes(DefaultBuildMinutes);
        var sorted = state.UnityBuildDurations.Order().ToList();
        return TimeSpan.FromSeconds(sorted[(sorted.Count - 1) / 2]);
    }

    private async Task RunUnityAsync(BuildContext context, UnityInstall unity, string[] args, string logFile,
        IUnityLogWatcher watcher, CancellationToken ct)
    {
        FileOps.DeleteFile(logFile);
        using var stopTail = CancellationTokenSource.CreateLinkedTokenSource(ct);
        var tail = TailAsync(logFile, watcher, stopTail.Token);
        ProcessResult result;
        try
        {
            result = await ProcessRunner.RunAsync(new ProcessSpec
            {
                FileName = unity.Executable,
                Arguments = args,
                Environment = UnityEnvironment(context.Workspace),
                LogPrefix = "[unity] ",
            }, ct: ct);
        }
        finally
        {
            await stopTail.CancelAsync();
            try
            {
                await tail;
            }
            catch (OperationCanceledException)
            {
            }
        }
        if (result.ExitCode != 0)
            throw Failure(logFile, $"Unity stopped with an error (exit code {result.ExitCode})");
    }

    /// <summary>
    /// Windows: Gradle's cache and the temp folder inside the workspace. Under a user name with non-Latin letters
    /// (C:\Users\Дмитрий) the Android tools (aapt2, the NDK) fail on their own paths; the workspace path is ASCII.
    /// </summary>
    private static Dictionary<string, string?> UnityEnvironment(Workspace workspace)
    {
        var environment = new Dictionary<string, string?>();
        if (!Platform.IsWindows)
            return environment;
        var gradle = Path.Combine(workspace.Tools, "gradle");
        var temp = Path.Combine(workspace.Tools, "tmp");
        Directory.CreateDirectory(gradle);
        Directory.CreateDirectory(temp);
        environment["GRADLE_USER_HOME"] = gradle;
        environment["TEMP"] = temp;
        environment["TMP"] = temp;
        return environment;
    }

    /// <summary>Feeds new lines of the Unity log to the progress watcher until stopped</summary>
    private static async Task TailAsync(string path, IUnityLogWatcher watcher, CancellationToken ct)
    {
        long position = 0;
        var partial = new StringBuilder();
        var buffer = new byte[1 << 16];
        while (true)
        {
            await Task.Delay(1000, ct);
            if (!File.Exists(path))
                continue;
            await using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
            if (stream.Length < position)
                position = 0;  // the editor started a new log
            stream.Position = position;
            int read;
            while ((read = await stream.ReadAsync(buffer, ct)) > 0)
            {
                position += read;
                partial.Append(Encoding.UTF8.GetString(buffer, 0, read));
                var text = partial.ToString();
                var lastNewline = text.LastIndexOf('\n');
                if (lastNewline < 0)
                    continue;
                foreach (var line in text[..lastNewline].Split('\n'))
                    watcher.OnLine(line.TrimEnd('\r'));
                partial.Clear().Append(text[(lastNewline + 1)..]);
            }
            watcher.OnTick();
        }
    }

    /// <summary>A failure with the editor's own explanation where we recognize it</summary>
    private static BuildException Failure(string logFile, string fallback)
    {
        var log = File.Exists(logFile) ? File.ReadAllText(logFile) : "";
        var errors = string.Join('\n', log.Split('\n')
            .Where(l => l.Contains("error", StringComparison.OrdinalIgnoreCase) || l.Contains("[QuestBuild]") ||
                        l.Contains("Exception", StringComparison.Ordinal))
            .TakeLast(40));
        var details = $"{logFile}\n{errors}";
        if (Regex.IsMatch(log, @"No valid Unity Editor license|License is not active|Licensing::Module.*(?:error|failed)|Unity Editor license.*not found", RegexOptions.IgnoreCase))
            return new BuildException("Unity has no license: sign in to Unity Hub and add a free Personal license", details) { Code = "unity-license" };
        if (Regex.IsMatch(log, @"No space left on device|There is not enough space on the disk|ERROR_DISK_FULL", RegexOptions.IgnoreCase))
            return new BuildException("The disk is full", details) { Code = "disk-full" };
        if (Regex.IsMatch(log, @"Android SDK|Android NDK|JDK.*not found|Unable to (?:find|locate) (?:the )?Android", RegexOptions.IgnoreCase) &&
            log.Contains("not found", StringComparison.OrdinalIgnoreCase))
            return new BuildException("Unity's Android Build Support is incomplete", details) { Code = "unity-android" };
        if (log.Contains("Scripts have compiler errors", StringComparison.Ordinal))
            return new BuildException("The project's scripts do not compile", details) { Code = "unity-compile" };
        return new BuildException(fallback, details) { Code = "unity-failed" };
    }

    [GeneratedRegex(@"guid: ([0-9a-f]{32})")]
    private static partial Regex GuidRegex();

    [GeneratedRegex(@"^\[ *(\d+)/(\d+) ")]
    internal static partial Regex BeeStepRegex();

    private interface IUnityLogWatcher
    {
        void OnLine(string line);
        void OnTick();
    }

    /// <summary>The Prepare run: on a new Library, assets imported out of all in the project</summary>
    private sealed class ImportProgress(UnityBuildStep step, BuildContext context, int expected) : IUnityLogWatcher
    {
        private int imported;

        public void OnLine(string line)
        {
            if (line.StartsWith("Start importing ", StringComparison.Ordinal))
                imported++;
        }

        public void OnTick() => step.Report(context,
            expected > 0 ? Math.Min(0.99, imported / (double)expected) : null, "import",
            imported > 0 ? imported.ToString() : null);
    }

    /// <summary>The BuildApk run: its stage from the log, the fraction from the time earlier builds took</summary>
    private sealed class BuildProgress(UnityBuildStep step, BuildContext context, TimeSpan expected) : IUnityLogWatcher
    {
        private readonly DateTime started = DateTime.UtcNow;
        private string stage = "scripts";
        private string? detail;
        private int shaders;

        public void OnLine(string line)
        {
            if (line.StartsWith("[QuestBuild] building", StringComparison.Ordinal))
            {
                stage = "scenes";
            }
            else if (line.StartsWith("Compiling shader ", StringComparison.Ordinal) && stage is "scenes" or "shaders")
            {
                stage = "shaders";
                detail = (++shaders).ToString();
            }
            else if (BeeStepRegex().Match(line) is { Success: true } bee && int.Parse(bee.Groups[2].Value) >= 2000)
            {
                // Script compilation also counts steps (a few hundred); IL2CPP + C++ is the run with thousands
                stage = "il2cpp";
                detail = $"{bee.Groups[1].Value}/{bee.Groups[2].Value}";
            }
            else if (line.Contains("> Task :", StringComparison.Ordinal) || line.Contains("Starting a Gradle Daemon", StringComparison.Ordinal))
            {
                stage = "gradle";
                detail = null;
            }
        }

        public void OnTick()
        {
            var fraction = Math.Min(0.99, (DateTime.UtcNow - started) / expected);
            step.Report(context, fraction, stage, detail);
        }
    }
}
