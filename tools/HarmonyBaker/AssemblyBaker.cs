using System.Reflection;
using System.Text.RegularExpressions;
using Mono.Cecil;

namespace HarmonyBaker;

/// <summary>Writes baked method bodies and native-call redirects into copies of the affected assemblies</summary>
internal sealed partial class AssemblyBaker(AssemblyIndex index, string gameManagedDir, string outDir, string? overridesPath, string platform,
    IReadOnlyDictionary<string, string> rewriteOnly)
{
    public sealed record AssemblyResult(string Name, int MethodsBaked, int CallSitesRedirected, List<string> Errors, List<string> MissingNetstandardTypes,
        List<string> TypeRepairs);

    // RepoXR must not try to patch at runtime anymore: there is no Harmony backend under IL2CPP
    private static readonly string[] RuntimePatcherEntryPoints = ["PatchUniversal", "PatchVR", "PatchClass", "PatchNetworkRPCs", ".cctor"];

    public List<string> OverridesApplied { get; } = [];

    public List<AssemblyResult> Write(IReadOnlyList<BakingPatcher> patchers, string modAssemblyName, NativeHooks hooks)
    {
        var results = new List<AssemblyResult>();
        var netstandardTypes = LoadNetstandardSurface();

        var bakedByAssembly = patchers.Where(p => !p.IsNative && p.Latest is not null)
            .GroupBy(p => p.Original.Module.Assembly.GetName().Name!)
            .ToDictionary(g => g.Key, g => g.ToList());

        // The mod assembly is always rewritten; with native hooks every managed game assembly may hold call sites
        var overrides = LoadOverrides();
        var candidates = new HashSet<string>(bakedByAssembly.Keys) { modAssemblyName };
        candidates.UnionWith(overrides.Select(o => o.Assembly));
        if (hooks.Any)
            candidates.UnionWith(ManagedGameAssemblies());

        Directory.CreateDirectory(outDir);

        foreach (var assemblyName in candidates.OrderBy(n => n))
        {
            var errors = new List<string>();
            if (!index.TryGetPath(assemblyName, out var path) && !rewriteOnly.TryGetValue(assemblyName, out path!))
            {
                results.Add(new AssemblyResult(assemblyName, 0, 0, [$"assembly file not found for {assemblyName}"], [], []));
                continue;
            }

            var resolver = new DefaultAssemblyResolver();
            foreach (var dir in index.Directories.Append(Path.GetDirectoryName(path)!).Distinct())
                resolver.AddSearchDirectory(dir);

            using var assembly = AssemblyDefinition.ReadAssembly(path, new ReaderParameters { AssemblyResolver = resolver });
            var module = assembly.MainModule;
            var transplanter = new BodyTransplanter(module);
            var baked = 0;

            foreach (var patcher in bakedByAssembly.GetValueOrDefault(assemblyName, []))
            {
                var name = Describe(patcher.Original);
                try
                {
                    if (!patcher.Completed)
                        errors.Add($"{name}: Harmony did not finish patching (see failures), baking the manipulated body anyway");

                    var target = (MethodDefinition)module.LookupToken(patcher.Original.MetadataToken);
                    transplanter.Transplant(patcher.Latest!.Definition, target);
                    baked++;
                }
                catch (Exception e)
                {
                    errors.Add($"{name}: {e.GetType().Name}: {e.Message}");
                }
            }

            if (assemblyName == modAssemblyName)
            {
                StubRuntimePatcher(module, errors);
                hooks.EmitWrappers(module, transplanter);
                Publicize(module);
            }

            ApplyOverrides(module, transplanter, overrides.Where(o => o.Assembly == assemblyName), errors);

            var redirected = 0;
            try
            {
                redirected = hooks.RedirectCallSites(module, modAssemblyName);
            }
            catch (Exception e)
            {
                errors.Add($"call site redirect: {e.Message}");
            }

            if (baked == 0 && redirected == 0 && assemblyName != modAssemblyName && overrides.All(o => o.Assembly != assemblyName))
                continue; // untouched

            RemoveStaleReferences(module);

            var missing = transplanter.NetstandardTypesUsed.Where(t => !netstandardTypes.Contains(t)).OrderBy(t => t).ToList();
            assembly.Write(Path.Combine(outDir, Path.GetFileName(path)));
            results.Add(new AssemblyResult(assemblyName, baked, redirected, errors, missing, transplanter.TypeRepairsMade));
        }

        return results;
    }

    /// <summary>Game-side managed assemblies: everything in Managed except the engine itself and the BCL</summary>
    private IEnumerable<string> ManagedGameAssemblies()
    {
        foreach (var file in Directory.EnumerateFiles(gameManagedDir, "*.dll"))
        {
            string name;
            try
            {
                name = AssemblyName.GetAssemblyName(file).Name!;
            }
            catch (BadImageFormatException)
            {
                continue;
            }

            if (!AssemblyIndex.IsFrameworkName(name) && !EngineAssembly().IsMatch(name) && name != "Mono.Security")
                yield return name;
        }
    }

    [GeneratedRegex(@"^UnityEngine(\.\w+Module)?$")]
    private static partial Regex EngineAssembly();

    private static void StubRuntimePatcher(ModuleDefinition module, List<string> errors)
    {
        var patcher = module.GetType("RepoXR.Patches.HarmonyPatcher");
        if (patcher is null)
        {
            errors.Add("RepoXR.Patches.HarmonyPatcher not found; runtime patching was not disabled");
            return;
        }

        foreach (var method in patcher.Methods.Where(m => RuntimePatcherEntryPoints.Contains(m.Name)))
            BodyTransplanter.ReplaceWithReturn(method);
    }

    private sealed record Override(string Assembly, string Type, string Method, MethodDefinition Body);

    /// <summary>Methods marked [Replaces(type, method, platform, Assembly = ...)] in the overrides assembly</summary>
    private List<Override> LoadOverrides()
    {
        var result = new List<Override>();
        if (overridesPath is null)
            return result;

        // Kept open: the definitions are read while the target assemblies are written
        var overrides = AssemblyDefinition.ReadAssembly(overridesPath);
        foreach (var method in overrides.MainModule.GetTypes().SelectMany(t => t.Methods))
        {
            var attribute = method.CustomAttributes.FirstOrDefault(a => a.AttributeType.Name == "ReplacesAttribute");
            if (attribute is null)
                continue;

            var targetPlatform = (string)attribute.ConstructorArguments[2].Value;
            if (targetPlatform != "any" && targetPlatform != platform)
                continue;

            var assembly = attribute.Properties.FirstOrDefault(p => p.Name == "Assembly").Argument.Value as string ?? "RepoXR";
            result.Add(new Override(assembly, (string)attribute.ConstructorArguments[0].Value,
                (string)attribute.ConstructorArguments[1].Value, method));
        }

        return result;
    }

    /// <summary>The override's body is copied over the target (so builds need no runtime reference to it)</summary>
    private void ApplyOverrides(ModuleDefinition module, BodyTransplanter transplanter, IEnumerable<Override> overrides, List<string> errors)
    {
        foreach (var o in overrides)
        {
            var name = $"{o.Assembly}:{o.Type}::{o.Method}";
            var candidates = module.GetType(o.Type.Replace('+', '/'))?.Methods.Where(m => m.Name == o.Method).ToList() ?? [];
            var target = candidates.FirstOrDefault(m => (m.HasThis ? 1 : 0) + m.Parameters.Count == o.Body.Parameters.Count);
            if (target is null)
            {
                errors.Add($"override target {name} not found or signature differs (game or RepoXR changed?)");
                continue;
            }

            if (target.ReturnType.FullName != o.Body.ReturnType.FullName)
            {
                errors.Add($"override {name}: return type {o.Body.ReturnType.FullName} does not match {target.ReturnType.FullName}");
                continue;
            }

            transplanter.Transplant(o.Body, target);
            OverridesApplied.Add(name);
        }
    }

    /// <summary>
    /// Baked game methods now call RepoXR's private patch methods and read its private fields directly (under
    /// Harmony they ran from DynamicMethods, which skip visibility checks). Opening up the mod assembly keeps the
    /// IL valid for any runtime that does check.
    /// </summary>
    private static void Publicize(ModuleDefinition module)
    {
        foreach (var type in module.GetTypes())
        {
            if (type.Name == "<Module>")
                continue;

            if (type.IsNested)
                type.IsNestedPublic = true;
            else
                type.IsPublic = true;

            foreach (var method in type.Methods)
                method.IsPublic = true;
            foreach (var field in type.Fields)
                field.IsPublic = true;
        }
    }

    /// <summary>Imports add references to the baker's own runtime (System.Private.CoreLib...) and to the assembly
    /// itself; every use was retargeted, so drop the references that were not in the original file</summary>
    private static void RemoveStaleReferences(ModuleDefinition module)
    {
        var self = module.Assembly.Name.Name;
        foreach (var reference in module.AssemblyReferences.ToList())
        {
            var added = reference.MetadataToken.RID == 0;
            if (reference.Name == self || (added && AssemblyIndex.IsFrameworkName(reference.Name)))
                module.AssemblyReferences.Remove(reference);
        }
    }

    private HashSet<string> LoadNetstandardSurface()
    {
        var types = new HashSet<string>();
        if (!index.TryGetPath("netstandard", out var path))
            return types;

        using var netstandard = AssemblyDefinition.ReadAssembly(path);
        foreach (var exported in netstandard.MainModule.ExportedTypes)
            types.Add(exported.FullName);
        foreach (var type in netstandard.MainModule.Types)
            types.Add(type.FullName);

        return types;
    }

    private static string Describe(MethodBase method) => $"{method.DeclaringType?.FullName}::{method.Name}";
}
