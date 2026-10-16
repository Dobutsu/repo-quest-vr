using System.Text.RegularExpressions;

namespace RepoQuest.Builder.Pipeline;

/// <summary>
/// Turns the export into the Unity project for Quest: the work the bash scripts make-quest-project.sh, make-quest-vr.sh,
/// apply-quest-runtime.sh and the copying part of build-quest.sh did, in the same order and with the same results.
/// The project is recreated from the export every time; its Library (Unity's import cache, the slow part) is kept.
/// </summary>
public sealed class ProjectStep : BuildStep
{
    private static readonly Dictionary<string, string> GameAssemblyNames = new()
    {
        ["Assembly-CSharp"] = "REPO.Game",
        ["Assembly-CSharp-firstpass"] = "REPO.Game.FirstPass",
    };

    /// <summary>SteamKit2 and what it needs that the player does not have (see apply-quest-runtime.sh)</summary>
    private static readonly string[] SteamKitAssemblies = ["SteamKit2", "protobuf-net", "protobuf-net.Core", "System.Collections.Immutable"];

    /// <summary>BepInEx assemblies RepoXR uses at runtime</summary>
    private static readonly string[] BepInExAssemblies = ["BepInEx", "MonoMod.Utils", "MonoMod.RuntimeDetour", "Mono.Cecil"];

    // IL2CPP's linker only keeps assemblies reachable from the scenes; nothing references the bootstrap (it runs via
    // RuntimeInitializeOnLoadMethod), and RepoXR's asset bundle may use XR types no code references
    private const string LinkXml = """
        <linker>
          <assembly fullname="RepoXR.Standalone" preserve="all"/>
          <assembly fullname="RepoXR" preserve="all"/>
          <assembly fullname="BepInEx" preserve="all"/>
          <assembly fullname="Unity.XR.Interaction.Toolkit" preserve="all"/>
          <assembly fullname="Unity.XR.OpenXR" preserve="all"/>
          <assembly fullname="Unity.XR.CoreUtils" preserve="all"/>
          <assembly fullname="UnityEngine.XR.LegacyInputHelpers" preserve="all"/>
          <assembly fullname="UnityEngine.SpatialTracking" preserve="all"/>
          <!-- Steam without the client: SteamKit2 and protobuf-net (de)serialize messages through reflection -->
          <assembly fullname="Facepunch.Steamworks.Win64" preserve="all"/>
          <assembly fullname="SteamKit2" preserve="all"/>
          <assembly fullname="protobuf-net" preserve="all"/>
          <assembly fullname="protobuf-net.Core" preserve="all"/>
          <assembly fullname="System.Collections.Immutable" preserve="all"/>
        </linker>
        """;

    private const int Parts = 22;
    private int done;

    public override string Id => "project";

    public override string Stamp(BuildContext context, IReadOnlyDictionary<string, string> earlierStamps)
    {
        var pins = context.Kit.Manifest.Downloads;
        return Hashing.Combine(Id, earlierStamps.GetValueOrDefault("export"), BakeStep.OutputHash(context),
            context.Kit.ContentHash, context.Kit.Manifest.Unity.Version, pins.BepInEx.Sha256,
            string.Join(',', pins.NuGet.Select(p => p.Sha256)), pins.BuiltinShaders.Sha256);
    }

    public override bool ResultExists(BuildContext context) =>
        File.Exists(Path.Combine(context.Workspace.Project, "Assets", "RepoQuest", "link.xml"));

    public override async Task RunAsync(BuildContext context, CancellationToken ct)
    {
        done = 0;
        var workspace = context.Workspace;
        var game = context.Game;
        var unity = context.RequireUnity();
        var project = workspace.Project;

        Report(context, null, "download");
        var bepInExCore = await context.ThirdParty.BepInExCoreAsync(ct);
        var discoverState = await context.ThirdParty.NuGetLibAsync("customdiscoverstatelib", ct);
        await context.ThirdParty.BuiltinShadersAsync(ct);

        // make-quest-project.sh
        Step(context, "copy");
        RecreateFromExport(workspace);
        FileOps.WriteText(Path.Combine(project, "ProjectSettings", "ProjectVersion.txt"),
            $"m_EditorVersion: {context.Kit.Manifest.Unity.Version}\n" +
            $"m_EditorVersionWithRevision: {context.Kit.Manifest.Unity.Version} ({context.Kit.Manifest.Unity.Changeset})\n");
        EnableParallelImport(project);

        // Package DLLs -> real packages, dummy package shaders -> package shaders
        await Script(context, ct, "package_swap.py", project);
        // Unity reserves Assembly-CSharp* for its own compiled scripts
        await Script(context, ct, "rename_game_assemblies.py", project);
        // Assembly-CSharp's global `Version` type shadows System.Version inside the Input System package
        await Script(context, ct, "embed_package.py", project, "com.unity.inputsystem", "1.14.2");
        // The Windows Addressables build that came with the export; the Android one is built from restored groups
        FileOps.DeleteDirectory(Path.Combine(project, "Assets", "StreamingAssets", "aa"));
        FileOps.DeleteFile(Path.Combine(project, "Assets", "StreamingAssets", "aa.meta"));
        await Script(context, ct, "addressables_catalog.py", game.AddressablesCatalog, "--out", workspace.AddressablesEntries);
        // Localization finds tables by their original asset GUIDs; AssetRipper assigned new ones
        await Script(context, ct, "restore_guids.py", project, workspace.AddressablesEntries);
        // Photon instantiates networked objects by Resources path; AssetRipper's sanitized file names break those loads
        await Script(context, ct, "restore_resource_names.py", game.Data, project);
        // AssetRipper merges the source-weight curves of a constraint into one
        await Script(context, ct, "fix_constraint_curves.py", project);
        // AssetRipper's shader placeholders render nothing on Vulkan
        await Script(context, ct, "shader_states.py", game.Data, "--out", workspace.ShaderStates);
        await Script(context, ct, "generate_shaders.py", project, workspace.ShaderStates);
        // The UI render target is RGBA32F, which Adreno cannot blend into
        await Script(context, ct, "fix_render_textures.py", project);
        var editorDir = Path.Combine(project, "Assets", "RepoQuest", "Editor");
        CopyTree(Path.Combine(context.Kit.UnityTools, "Editor"), editorDir);

        // make-quest-vr.sh: XR packages, package sources with RepoXR's package patches and Post Processing's fixes
        await Script(context, ct, "xr_packages.py", project);
        await Script(context, ct, "embed_package.py", project, "com.unity.inputsystem", "1.14.2");
        await Script(context, ct, "embed_package.py", project, "com.unity.ugui", "1.0.0", "--builtin", unity.Root);
        await Script(context, ct, "embed_package.py", project, "com.unity.postprocessing", "3.5.0");
        await Script(context, ct, "patch_packages.py", project);

        await ApplyQuestRuntimeAsync(context, bepInExCore, ct);
        await AddRepoXRAsync(context, bepInExCore, discoverState, ct);

        // Halo glows are drawn for one eye only in single-pass instanced stereo: StereoHalo draws them for both
        await Script(context, ct, "stereo_halos.py", project);
        // Lamps lit per pixel in one pass: Standard copies that evaluate them, and the game's materials pointed at them
        await Script(context, ct, "make_lamp_shaders.py", project);
        await Script(context, ct, "retarget_standard.py", project);

        // build-quest.sh: build tooling, runtime scripts and the shaders they use
        Step(context, "copy");
        var tools = context.Kit.UnityTools;
        var repoQuest = Path.Combine(project, "Assets", "RepoQuest");
        FileOps.CopyFiles(Path.Combine(tools, "Editor"), "*.cs", editorDir);
        FileOps.CopyFiles(Path.Combine(tools, "Runtime"), "*.cs", Path.Combine(repoQuest, "Runtime"));
        // Scripts that prefabs reference (StereoHalo) keep a fixed GUID
        FileOps.CopyFiles(Path.Combine(tools, "Runtime"), "*.cs.meta", Path.Combine(repoQuest, "Runtime"));
        // Shaders our runtime code creates materials from (Shader.Find): Resources keeps them in the build
        FileOps.CopyFiles(Path.Combine(tools, "Runtime", "Shaders"), "*.shader", Path.Combine(repoQuest, "Resources"));
        // Shared shader code the generated and Standard shaders include (per-pixel lamps)
        FileOps.CopyFiles(Path.Combine(tools, "Runtime", "Shaders"), "*.cginc", Path.Combine(repoQuest, "Shaders"));
        Report(context, 1, "done");
    }

    /// <summary>A fresh copy of the export, with the previous Library moved back in</summary>
    private static void RecreateFromExport(Workspace workspace)
    {
        if (Directory.Exists(workspace.Library))
        {
            FileOps.DeleteDirectory(workspace.LibraryKeep);
            Directory.Move(workspace.Library, workspace.LibraryKeep);
        }
        FileOps.DeleteDirectory(workspace.Project);
        FileOps.CopyDirectory(workspace.ExportedProject, workspace.Project);
        // An interrupted earlier run may have left the Library waiting here as well
        if (Directory.Exists(workspace.LibraryKeep))
            Directory.Move(workspace.LibraryKeep, workspace.Library);
    }

    /// <summary>
    /// Parallel Import (Project Settings → Editor → Asset Pipeline): textures and models import in worker processes
    /// instead of one at a time; a first build imports some 15,000 assets. The export keeps Unity's default, off.
    /// </summary>
    private static void EnableParallelImport(string project)
    {
        var settings = Path.Combine(project, "ProjectSettings", "EditorSettings.asset");
        var text = File.ReadAllText(settings);
        var parallel = Regex.Replace(text, @"(?m)^(  m_RefreshImportMode:) \d+$", "$1 1");
        if (parallel == text && !text.Contains("  m_RefreshImportMode: 1", StringComparison.Ordinal))
            Log.Warn("EditorSettings.asset has no m_RefreshImportMode; assets import one at a time");
        FileOps.WriteText(settings, parallel);
    }

    /// <summary>apply-quest-runtime.sh: the baked game assembly, Photon Voice, libopus, SteamShim with SteamKit2</summary>
    private async Task ApplyQuestRuntimeAsync(BuildContext context, string bepInExCore, CancellationToken ct)
    {
        Step(context, "runtime");
        var workspace = context.Workspace;
        var plugins = Path.Combine(workspace.Project, "Assets", "Plugins");
        var staging = workspace.Staging;
        FileOps.DeleteDirectory(staging);
        FileOps.CopyFiles(workspace.Baked, "*.dll", staging);
        await RenameAsync(context, bepInExCore, Directory.EnumerateFiles(staging, "*.dll").Order(StringComparer.Ordinal), ct);

        // The .meta (and with it every script reference) stays; only the assembly changes
        FileOps.CopyFile(Path.Combine(staging, "REPO.Game.dll"), Path.Combine(plugins, "REPO.Game.dll"));
        // Other game plugins with Android overrides baked in (PhotonVoice: WebRTC audio processing off)
        if (File.Exists(Path.Combine(staging, "PhotonVoice.dll")))
            FileOps.CopyFile(Path.Combine(staging, "PhotonVoice.dll"), Path.Combine(plugins, "PhotonVoice.dll"));

        // Voice: libopus for arm64 under the name Photon Voice imports
        var native = Path.Combine(plugins, "Android", "arm64-v8a");
        FileOps.CopyFile(context.Kit.Opus, Path.Combine(native, "libopus_egpv.so"));
        WriteMeta(Path.Combine(native, "libopus_egpv.so.meta"), PluginMetas.OpusPlugin, "repoquest-native:libopus_egpv");

        // SteamShim in place of Facepunch.Steamworks, whose steam_api64 does not exist on Android
        FileOps.CopyFile(Path.Combine(context.Kit.SteamDir, "Facepunch.Steamworks.Win64.dll"),
            Path.Combine(plugins, "Facepunch.Steamworks.Win64.dll"));
        var steamKit = Path.Combine(plugins, "SteamKit");
        foreach (var name in SteamKitAssemblies)
        {
            FileOps.CopyFile(Path.Combine(context.Kit.SteamDir, name + ".dll"), Path.Combine(steamKit, name + ".dll"));
            WriteMeta(Path.Combine(steamKit, name + ".dll.meta"), PluginMetas.SteamKitPlugin, $"repoquest-steamkit:{name}");
        }
        // IL2CPP on Android lacks Process.StartTime (SteamKit2) and custom modifiers (protobuf-net)
        await context.Tools.RunAsync("il2cpp-fixes",
            [Path.Combine(steamKit, "SteamKit2.dll"), Path.Combine(steamKit, "protobuf-net.dll")],
            "Could not adapt SteamKit2 for Android", ct);
    }

    /// <summary>make-quest-vr.sh: RepoXR and what it needs at runtime as Android-only plugins, link.xml, its assets</summary>
    private async Task AddRepoXRAsync(BuildContext context, string bepInExCore, string discoverState, CancellationToken ct)
    {
        Step(context, "repoxr");
        var workspace = context.Workspace;
        var staging = workspace.Staging;
        // CustomDiscoverStateLib is a REPO mod library too (it references the game); keep its file named after its assembly
        var discover = Path.Combine(staging, "Kistras-CustomDiscoverStateLib.dll");
        var standalone = Path.Combine(staging, "RepoXR.Standalone.dll");
        FileOps.CopyFile(Path.Combine(discoverState, "CustomDiscoverStateLib.dll"), discover);
        FileOps.CopyFile(context.Kit.StandaloneDll, standalone);
        await RenameAsync(context, bepInExCore, [discover, standalone], ct);

        var plugins = Path.Combine(workspace.Project, "Assets", "Plugins", "RepoXR");
        foreach (var file in new[] { "RepoXR.dll", "RepoXR.Standalone.dll", "Kistras-CustomDiscoverStateLib.dll" })
            FileOps.CopyFile(Path.Combine(staging, file), Path.Combine(plugins, file));
        foreach (var name in BepInExAssemblies)
            FileOps.CopyFile(Path.Combine(bepInExCore, name + ".dll"), Path.Combine(plugins, name + ".dll"));
        // HarmonyX with the IL2CPP-safe method invoker (Android override, see RepoXR.Standalone/PlatformOverrides.cs)
        FileOps.CopyFile(Path.Combine(workspace.Baked, "0Harmony.dll"), Path.Combine(plugins, "0Harmony.dll"));
        foreach (var dll in Directory.EnumerateFiles(plugins, "*.dll").Order(StringComparer.Ordinal))
            WriteMeta(dll + ".meta", PluginMetas.RepoXRPlugin, $"repoxr-quest:{Path.GetFileName(dll)}");

        var repoQuest = Path.Combine(workspace.Project, "Assets", "RepoQuest");
        FileOps.WriteText(Path.Combine(repoQuest, "link.xml"), LinkXml + "\n");

        // RepoXR's own assets; the bootstrap extracts them from the APK on first start
        var streaming = Path.Combine(workspace.Project, "Assets", "StreamingAssets", "RepoXR");
        FileOps.DeleteDirectory(streaming);
        FileOps.CopyFile(Path.Combine(context.Kit.RepoXRAndroidAssets, "repoxrassets"), Path.Combine(streaming, "repoxrassets"));
        CopyTree(Path.Combine(context.Kit.RepoXRAndroidAssets, "Addressables"), Path.Combine(streaming, "Addressables"));

        FileOps.CopyFiles(Path.Combine(context.Kit.UnityTools, "EditorXR"), "*.cs", Path.Combine(repoQuest, "Editor"));
    }

    private static Task RenameAsync(BuildContext context, string bepInExCore, IEnumerable<string> dlls, CancellationToken ct) =>
        context.Tools.RunAsync("assembly-renamer",
            [
                .. GameAssemblyNames.SelectMany(m => new[] { "--map", $"{m.Key}={m.Value}" }),
                "--search", context.Game.Managed,
                "--search", bepInExCore,
                .. dlls,
            ],
            "Could not rename the game's assemblies", ct);

    private static void WriteMeta(string path, string template, string guidSeed) =>
        FileOps.WriteText(path, template.Replace("{GUID}", Hashing.Md5Hex(guidSeed)) + "\n");

    /// <summary>`cp -R source target` for a target that does not exist yet: the folder with everything in it</summary>
    private static void CopyTree(string source, string target)
    {
        foreach (var file in Directory.EnumerateFiles(source, "*", SearchOption.AllDirectories))
            FileOps.CopyFile(file, Path.Combine(target, Path.GetRelativePath(source, file)));
    }

    private Task<ProcessResult> Script(BuildContext context, CancellationToken ct, string script, params string[] args)
    {
        Step(context, Path.GetFileNameWithoutExtension(script));
        return context.RunScriptAsync(script, ct, args);
    }

    private void Step(BuildContext context, string detail) =>
        Report(context, Math.Min(0.99, done++ / (double)Parts), "project", detail);
}
