using System.Reflection;
using System.Text.Json;
using HarmonyBaker;
using HarmonyLib;

// Spike 2: bake RepoXR's Harmony patches into the game's assemblies.
//
//   HarmonyBaker --game <REPO_Data/Managed> --mod <RepoXR.dll> --deps <dir> [--deps <dir>...]
//                --targets universal|all --out <dir> [--rewrite <dll>...]
//
// --rewrite: an assembly that platform overrides may target but that must not be loaded next to the baker's own copy
// (BepInEx's HarmonyX 0Harmony.dll).
//
// "universal" = patches RepoXR applies even without VR (used to test the baked game flat in CrossOver);
// "all"       = universal + VR-only, i.e. what the Quest build needs.

var opts = ParseOptions(args);
var gameDir = Path.GetFullPath(Require("game"));
var modPath = Path.GetFullPath(Require("mod"));
var outDir = Path.GetFullPath(Require("out"));
var targets = opts.GetValueOrDefault("targets")?.FirstOrDefault() switch
{
    "universal" => new HashSet<PatchSelection.Target> { PatchSelection.Target.Universal },
    "all" or null => new HashSet<PatchSelection.Target> { PatchSelection.Target.Universal, PatchSelection.Target.VROnly },
    "none" => new HashSet<PatchSelection.Target>(),
    var other => throw new ArgumentException($"unknown --targets {other}"),
};

var searchDirs = new List<string> { gameDir, Path.GetDirectoryName(modPath)! };
searchDirs.AddRange(opts.GetValueOrDefault("deps", []).Select(Path.GetFullPath));

var index = new AssemblyIndex(searchDirs);
var debugBuild = opts.TryGetValue("debug-build", out var db) ? bool.Parse(db[0]) : UnityBakeShim.IsDebugCompiled(Path.Combine(gameDir, "Assembly-CSharp.dll"));
index.InstallResolver((name, path) => UnityBakeShim.Preprocess(name, path, debugBuild));
BakingPatcher.Install();

var mod = Assembly.LoadFrom(modPath);
var selection = new PatchSelection(mod);
var harmony = new Harmony("io.daxcess.repoxr.bake");

selection.ApplyPatchClasses(harmony, targets);
if (targets.Count > 0)
    selection.ApplyNetworkRpcPrefixes(harmony);

var hooks = new NativeHooks(BakingPatcher.Instances.Where(p => p.IsNative));
var overridesPath = opts.TryGetValue("overrides", out var ov) ? Path.GetFullPath(ov[0]) : null;
var platform = opts.TryGetValue("platform", out var pl) ? pl[0] : "windows";
var rewriteOnly = opts.GetValueOrDefault("rewrite", []).Select(Path.GetFullPath)
    .ToDictionary(p => Path.GetFileNameWithoutExtension(p), p => p);
var baker = new AssemblyBaker(index, gameDir, outDir, overridesPath, platform, rewriteOnly);
var results = baker.Write(BakingPatcher.Instances, mod.GetName().Name!, hooks);

Console.WriteLine($"game scripts compiled as debug build: {debugBuild}");
Console.WriteLine($"platform: {platform}; overrides applied: {string.Join(", ", baker.OverridesApplied)}");
Console.WriteLine($"native engine methods hooked at call sites: {string.Join(", ", hooks.Hooks.Select(h => $"{h.Original.DeclaringType!.Name}.{h.Original.Name}"))}");
hooks.Errors.ForEach(e => Console.WriteLine($"  hook error: {e}"));
Console.WriteLine($"patch classes applied: {selection.Applied.Count}, skipped: {selection.Skipped.Count}, failed: {selection.Failed.Count}");
foreach (var (name, error) in selection.Failed)
    Console.WriteLine($"  FAILED {name}: {Flatten(error)}");

foreach (var r in results)
{
    Console.WriteLine($"{r.Name}: {r.MethodsBaked} methods baked, {r.CallSitesRedirected} call sites redirected, {r.Errors.Count} errors, {r.MissingNetstandardTypes.Count} types missing from netstandard");
    r.Errors.ForEach(e => Console.WriteLine($"  error: {e}"));
    r.MissingNetstandardTypes.ForEach(t => Console.WriteLine($"  not in netstandard: {t}"));
    r.TypeRepairs.ForEach(t => Console.WriteLine($"  type repair: {t}"));
}

var report = new
{
    targets = targets.Select(t => t.ToString()),
    applied = selection.Applied,
    skipped = selection.Skipped,
    failed = selection.Failed.Select(f => new { f.Name, error = Flatten(f.Error) }),
    debugBuild,
    platform,
    overrides = baker.OverridesApplied,
    nativeHooks = hooks.Hooks.Select(h => new { method = $"{h.Original.DeclaringType!.FullName}::{h.Original.Name}", h.WrapperName }),
    hookErrors = hooks.Errors,
    assemblies = results,
    methods = BakingPatcher.Instances.Select(p => new
    {
        method = $"{p.Original.DeclaringType?.FullName}::{p.Original.Name}",
        assembly = p.Original.Module.Assembly.GetName().Name,
        completed = p.Completed,
        native = p.IsNative,
    }),
};
File.WriteAllText(Path.Combine(outDir, "bake-report.json"), JsonSerializer.Serialize(report, new JsonSerializerOptions { WriteIndented = true }));

var ok = selection.Failed.Count == 0 && hooks.Errors.Count == 0 && results.All(r => r.Errors.Count == 0 && r.MissingNetstandardTypes.Count == 0);
Console.WriteLine(ok ? "BAKE OK" : "BAKE FINISHED WITH PROBLEMS");
return ok ? 0 : 1;

string Require(string key) =>
    opts.TryGetValue(key, out var values) && values.Count > 0 ? values[0] : throw new ArgumentException($"--{key} is required");

static string Flatten(Exception e)
{
    var parts = new List<string>();
    for (var cur = e; cur is not null; cur = cur.InnerException)
        parts.Add($"{cur.GetType().Name}: {cur.Message}");
    return string.Join(" -> ", parts);
}

static Dictionary<string, List<string>> ParseOptions(string[] argv)
{
    var result = new Dictionary<string, List<string>>();
    for (var i = 0; i < argv.Length - 1; i++)
    {
        if (!argv[i].StartsWith("--"))
            continue;

        var key = argv[i][2..];
        if (!result.TryGetValue(key, out var list))
            result[key] = list = [];
        list.Add(argv[++i]);
    }

    return result;
}
