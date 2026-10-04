using System.Text.Json;

namespace RepoQuest.Builder.Pipeline;

/// <summary>
/// Bakes RepoXR's Harmony patches into the game's assemblies for Android (tools/HarmonyBaker; what scripts/bake.sh all
/// android did): IL2CPP cannot patch at runtime, so the patched IL is written into the DLLs.
/// </summary>
public sealed class BakeStep : BuildStep
{
    public override string Id => "bake";

    public override string Stamp(BuildContext context, IReadOnlyDictionary<string, string> earlierStamps)
    {
        var pins = context.Kit.Manifest.Downloads;
        return Hashing.Combine(Id, context.Game.CodeFingerprint(), Hashing.Sha256File(context.Kit.RepoXRDll),
            Hashing.Sha256File(context.Kit.StandaloneDll), pins.RepoXR.Sha256, pins.BepInEx.Sha256,
            string.Join(',', pins.NuGet.Select(p => p.Sha256)), ToolsVersion());
    }

    public override bool ResultExists(BuildContext context) =>
        File.Exists(Path.Combine(context.Workspace.Baked, "Assembly-CSharp.dll"));

    public override async Task RunAsync(BuildContext context, CancellationToken ct)
    {
        Report(context, null, "download");
        var thirdParty = context.ThirdParty;
        var runtimeDeps = await thirdParty.RepoXRRuntimeDepsAsync(ct);
        var bepInExCore = await thirdParty.BepInExCoreAsync(ct);
        var baseLib = await thirdParty.NuGetLibAsync("bepinex.baselib", ct);
        var discoverState = await thirdParty.NuGetLibAsync("customdiscoverstatelib", ct);

        Report(context, null, "bake");
        var output = context.Workspace.Baked;
        FileOps.DeleteDirectory(output);
        var result = await ProcessRunner.RunAsync(new ProcessSpec
        {
            FileName = context.Tools.Command[0],
            Arguments =
            [
                .. context.Tools.Command.Skip(1), "harmony-baker",
                "--game", context.Game.Managed,
                "--mod", context.Kit.RepoXRDll,
                "--deps", runtimeDeps,
                "--deps", baseLib,
                "--deps", discoverState,
                "--overrides", context.Kit.StandaloneDll,
                "--platform", "android",
                "--rewrite", Path.Combine(bepInExCore, "0Harmony.dll"),
                "--targets", "all",
                "--out", output,
            ],
            LogPrefix = "[harmony-baker] ",
        }, ct: ct);

        var report = Path.Combine(output, "bake-report.json");
        if (result.ExitCode != 0 || !File.Exists(report))
        {
            var (problems, applied, failed) = File.Exists(report) ? Problems(report) : ([], 0, 0);
            // A game update that moved some code breaks a few patches; when (nearly) all fail, the baker itself could
            // not run here (not the game's fault)
            var brokenTool = !File.Exists(report) || applied == 0 || failed > applied;
            throw new BuildException(
                (brokenTool ? "The builder could not apply RepoXR's patches on this computer"
                            : "RepoXR's patches do not fit this version of the game") +
                (problems.Count > 0 ? ": " + string.Join("; ", problems.Take(5)) : ""),
                result.TailText) { Code = brokenTool ? "bake-tool" : "bake-failed" };
        }
    }

    /// <summary>What went wrong according to HarmonyBaker's report, and how many patch classes applied and failed</summary>
    private static (List<string> Problems, int Applied, int Failed) Problems(string reportPath)
    {
        using var json = JsonDocument.Parse(File.ReadAllText(reportPath));
        var root = json.RootElement;
        var problems = new List<string>();
        foreach (var failed in root.GetProperty("failed").EnumerateArray())
            problems.Add($"{failed.GetProperty("Name").GetString()}: {failed.GetProperty("error").GetString()}");
        problems.AddRange(root.GetProperty("hookErrors").EnumerateArray().Select(e => e.ToString()));
        foreach (var assembly in root.GetProperty("assemblies").EnumerateArray())
        {
            problems.AddRange(assembly.GetProperty("Errors").EnumerateArray().Select(e => e.ToString()));
            problems.AddRange(assembly.GetProperty("MissingNetstandardTypes").EnumerateArray()
                .Select(t => $"not in netstandard: {t}"));
        }
        return (problems, root.GetProperty("applied").GetArrayLength(), root.GetProperty("failed").GetArrayLength());
    }

    /// <summary>The baker is compiled into this builder: a builder with a changed HarmonyBaker bakes again</summary>
    private static string ToolsVersion() =>
        System.Reflection.Assembly.Load(new System.Reflection.AssemblyName("HarmonyBaker")).ManifestModule.ModuleVersionId.ToString();

    /// <summary>Hash of the baked assemblies: what the project step builds on (an identical re-bake changes nothing)</summary>
    public static string OutputHash(BuildContext context) =>
        Directory.Exists(context.Workspace.Baked)
            ? Hashing.Directory(context.Workspace.Baked, rel => rel.EndsWith(".dll", StringComparison.Ordinal))
            : "";
}
