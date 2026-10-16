using RepoQuest.Builder.Game;
using RepoQuest.Builder.Setup;

namespace RepoQuest.Builder.Pipeline;

/// <summary>The headset the APK is for: Unity builds for Quest; for Pico the pico step converts that APK</summary>
public enum Headset { Quest, Pico }

public sealed class BuildOptions
{
    /// <summary>APK file name in the workspace's build folder, without .apk</summary>
    public string ApkName { get; init; } = "REPO-Quest";
    /// <summary>Unity development build (-developmentBuild)</summary>
    public bool Development { get; init; }
    /// <summary>Profileable release build with IL2CPP symbols (-profiling)</summary>
    public bool Profiling { get; init; }
    public string VersionName { get; init; } = Platform.BuilderVersion;
    public int VersionCode { get; init; } = VersionCodeOf(Platform.BuilderVersion);
    /// <summary>Run every step even if its inputs did not change</summary>
    public bool Force { get; init; }
    /// <summary>Run only these steps (ids); null: all that are not up to date</summary>
    public IReadOnlySet<string>? Only { get; init; }
    public Headset Headset { get; init; } = Headset.Quest;

    /// <summary>The APK Unity builds (for Quest)</summary>
    public string ApkPath(Workspace workspace) => Path.Combine(workspace.Build, ApkName + ".apk");

    /// <summary>The APK for <see cref="Headset"/>: Unity's, or the pico step's next to it (REPO-Quest-1.0.5 -> REPO-Pico-1.0.5)</summary>
    public string HeadsetApkPath(Workspace workspace) => Headset == Headset.Pico
        ? Path.Combine(workspace.Build, PicoApkName(ApkName) + ".apk")
        : ApkPath(workspace);

    public static string PicoApkName(string apkName) =>
        apkName.Contains("Quest", StringComparison.Ordinal) ? apkName.Replace("Quest", "Pico", StringComparison.Ordinal) : apkName + "-pico";

    /// <summary>"quest" or "pico", as state.json and the command line spell it</summary>
    public static string HeadsetId(Headset headset) => headset == Headset.Pico ? "pico" : "quest";

    public static Headset ParseHeadset(string? id) => id switch
    {
        null or "quest" => Headset.Quest,
        "pico" => Headset.Pico,
        _ => throw new BuildException($"unknown headset {id}: quest or pico"),
    };

    /// <summary>Android versionCode of a version: 1.2.3 -> 10203 (always growing with releases; 0.0.0 -> 1)</summary>
    public static int VersionCodeOf(string version)
    {
        var parts = version.Split('.').Select(p => int.TryParse(p, out var n) ? n : 0).Concat([0, 0, 0]).Take(3).ToArray();
        return Math.Max(1, parts[0] * 10000 + parts[1] * 100 + parts[2]);
    }
}

/// <summary>Everything a build step works with</summary>
public sealed class BuildContext
{
    public required Workspace Workspace { get; init; }
    public required Kit Kit { get; init; }
    public required GameInstall Game { get; init; }
    /// <summary>Set once Unity is installed (export and bake run without it)</summary>
    public required UnityInstall? Unity { get; set; }
    public required PythonEnv Python { get; init; }
    public required ToolLauncher Tools { get; init; }
    public required ThirdParty ThirdParty { get; init; }
    public required BuildState State { get; init; }
    public required BuildOptions Options { get; init; }
    public required IBuildProgress Progress { get; init; }

    /// <summary>The step that is running (downloads report their progress under it)</summary>
    public string CurrentStep { get; set; } = "setup";

    /// <summary>Steps finished during this build (a second pass after Unity's installation does not report them again)</summary>
    public HashSet<string> FinishedThisBuild { get; } = new();

    public UnityInstall RequireUnity() =>
        Unity ?? throw new BuildException("Unity is not installed") { Code = "unity-missing" };

    public Task<ProcessResult> RunScriptAsync(string script, CancellationToken ct, params string[] args) =>
        Python.RunScriptAsync(this, script, args, ct);

    public void SaveState() => State.Save(Workspace.StatePath);
}
