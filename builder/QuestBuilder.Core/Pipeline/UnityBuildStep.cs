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
            "-batchmode", "-timestamps", "-projectPath", workspace.Project, "-buildTarget", "Android",
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
            "-batchmode", "-timestamps", "-projectPath", workspace.Project, "-buildTarget", "Android",
            "-executeMethod", "RepoQuest.Editor.QuestBuild.BuildApk",
            "-apkPath", apk,
            "-versionName", options.VersionName,
            "-versionCode", options.VersionCode.ToString(),
            .. options.Development ? new[] { "-developmentBuild" } : [],
            .. options.Profiling ? new[] { "-profiling" } : [],
            "-logFile", buildLog,
        ];
        QuoteSdkManagerPaths(unity);
        ClearMergedAssets(workspace);
        ClearGradleTransforms(workspace);
        var started = DateTime.UtcNow;
        await RunUnityAsync(context, unity, build, buildLog,
            new BuildProgress(this, context, ExpectedBuildTime(context.State, freshLibrary)), ct);
        var log = File.ReadAllText(buildLog);
        foreach (var line in log.Split('\n').Select(WithoutTimestamp).Where(l => l.StartsWith("[QuestBuild]", StringComparison.Ordinal)))
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
        state.LastApkHeadset = "quest";
        state.LastApkVersion = options.VersionName;
        state.LastApkGame = context.Game.AssemblyHash();
        state.LastApkTime = DateTime.Now;
        context.SaveState();
    }

    /// <summary>
    /// The Android build lists SDK components with sdkmanager, whose shell script (cmdline-tools 16.0) leaves its own
    /// folder unquoted: from an editor in a folder with a space ("/Volumes/Time Machine/...") java takes the rest of
    /// the path for its main class and the build stops at "Failed to update Android SDK package list". The Windows .bat
    /// quotes it already
    /// </summary>
    private static void QuoteSdkManagerPaths(UnityInstall unity)
    {
        var tools = Path.Combine(unity.AndroidPlayer, "SDK", "cmdline-tools");
        if (Platform.IsWindows || !Directory.Exists(tools))
            return;
        foreach (var script in Directory.EnumerateFiles(tools, "sdkmanager", SearchOption.AllDirectories))
        {
            var text = File.ReadAllText(script);
            var quoted = text
                .Replace("DEFAULT_JVM_OPTS='-Dcom.android.sdklib.toolsdir=$APP_HOME'",
                    "DEFAULT_JVM_OPTS='\"-Dcom.android.sdklib.toolsdir=$APP_HOME\"'")
                .Replace("$($JAVACMD -version", "$(\"$JAVACMD\" -version");
            if (quoted == text)
                continue;
            File.WriteAllText(script, quoted);
            Log.Info($"quoted the paths in {script}");
        }
    }

    /// <summary>
    /// Gradle merges the exported assets (~20 000 files) into unityLibrary/build/intermediates/assets incrementally.
    /// When a new game version changes many of them, the merge removes the old files and fails with "Too many open
    /// files" (the Android Gradle plugin's MergedAssetWriter.removeEmptyParents leaves a directory stream open per
    /// folder), after the whole IL2CPP build (b28, game 0.5.1). Without the earlier merge Gradle copies everything,
    /// which costs a few minutes; code-only builds (scripts/codebuild.sh) keep it.
    /// </summary>
    private static void ClearMergedAssets(Workspace workspace)
    {
        var projects = Path.Combine(workspace.Library, "Bee", "Android", "Prj");
        if (!Directory.Exists(projects))
            return;
        foreach (var backend in Directory.EnumerateDirectories(projects))
        {
            var intermediates = Path.Combine(backend, "Gradle", "unityLibrary", "build", "intermediates");
            var merged = Path.Combine(intermediates, "assets");
            if (!Directory.Exists(merged))
                continue;
            Log.Info($"clearing Gradle's merged assets: {merged}");
            FileOps.DeleteDirectory(merged);
            // The merge's own record of what it wrote, which would otherwise make it incremental again
            FileOps.DeleteDirectory(Path.Combine(intermediates, "incremental", "mergeReleaseAssets"));
            FileOps.DeleteDirectory(Path.Combine(intermediates, "incremental", "mergeDebugAssets"));
        }
    }

    /// <summary>
    /// The transforms in Gradle's cache: each APK build adds a ~2 GB one of the game's assets, which Gradle keeps for 7
    /// days and no later build reuses (the assets differ). Removed before each build, so the cache stays the size of one
    /// </summary>
    private static void ClearGradleTransforms(Workspace workspace)
    {
        var caches = Path.Combine(workspace.Tools, "gradle", "caches");
        if (!Directory.Exists(caches))
            return;
        foreach (var version in Directory.EnumerateDirectories(caches))
        {
            var transforms = Path.Combine(version, "transforms");
            if (!Directory.Exists(transforms))
                continue;
            Log.Info($"clearing Gradle's transforms: {transforms}");
            FileOps.DeleteDirectory(transforms);
        }
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
    /// Gradle's cache inside the workspace: every build leaves a ~2 GB transform of the game's assets there, kept for 7
    /// days, which in ~/.gradle filled the system disk (25 builds, 49 GB); in the workspace it goes with it.
    /// Windows: the temp folder too. Under a user name with non-Latin letters (C:\Users\Пользователь) the Android tools
    /// (aapt2, the NDK) fail on their own paths; the workspace path is ASCII.
    /// </summary>
    private static Dictionary<string, string?> UnityEnvironment(Workspace workspace)
    {
        var environment = new Dictionary<string, string?>();
        var gradle = Path.Combine(workspace.Tools, "gradle");
        Directory.CreateDirectory(gradle);
        environment["GRADLE_USER_HOME"] = gradle;
        if (!Platform.IsWindows)
            return environment;
        var temp = Path.Combine(workspace.Tools, "tmp");
        Directory.CreateDirectory(temp);
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
                    watcher.OnLine(WithoutTimestamp(line.TrimEnd('\r')));
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
        if (Regex.IsMatch(log, @"No valid Unity Editor license|License is not active|Licensing::Module(?!.*Access token is unavailable).*(?:error|failed)|Unity Editor license.*not found", RegexOptions.IgnoreCase))
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

    /// <summary>The editor runs with -timestamps, so every log line reads `2026-10-09T22:21:38.161Z|0x0203493f80|...`:
    /// the times show where a slow build spends them; the line parsing looks past them</summary>
    internal static string WithoutTimestamp(string line) => TimestampRegex().Replace(line, "", 1);

    [GeneratedRegex(@"^\d{4}-\d\d-\d\dT[\d:.]+Z\|0x[0-9A-Fa-f]+\|")]
    private static partial Regex TimestampRegex();

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
