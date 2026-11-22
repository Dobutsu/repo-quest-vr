using System.Reflection.Metadata;
using System.Reflection.PortableExecutable;
using System.Text;
using System.Text.Json;
using RepoQuest.Builder.Game;
using RepoQuest.Builder.Pipeline;
using RepoQuest.Builder.Setup;

namespace RepoQuest.Builder;

/// <summary>Checks the actual packaged entry point and tools without exporting the game or starting Unity.</summary>
public static class BuilderDiagnostics
{
    public static bool TryRun(string[] args, out int exitCode)
    {
        exitCode = 0;
        if (args.FirstOrDefault() != "--check") return false;
        exitCode = RunAsync(args[1..]).GetAwaiter().GetResult();
        return true;
    }

    public static async Task<int> RunAsync(string[] args)
    {
        string? Value(string key)
        {
            var index = Array.IndexOf(args, "--" + key);
            return index < 0 ? null : index + 1 < args.Length && !args[index + 1].StartsWith("--")
                ? args[index + 1] : throw new BuildException($"--{key} needs a value");
        }
        var defaults = BuilderLaunchDefaults.Load();
        var settings = AppSettings.Load();
        var workspace = new Workspace(Value("workspace") ?? settings.WorkspacePath ?? defaults.WorkspacePath ?? AppSettings.DefaultWorkspace());
        var reportPath = Value("report") ?? Path.Combine(workspace.Root, "preflight.json");
        var report = new DiagnosticReport { BuilderVersion = Platform.BuilderVersion, Workspace = workspace.Root };
        try
        {
            var gamePath = Value("game") ?? BuildState.Load(workspace.StatePath).GamePath ?? defaults.GamePath ?? GameLocator.Find();
            if (AppSettings.WorkspaceProblem(workspace.Root, gamePath) is { } problem)
                throw new BuildException($"Workspace: {problem}");
            workspace.Create();
            Log.OpenFile(Path.Combine(workspace.Logs, "preflight.log"));
            var kit = Value("kit") is { } kitDir ? Kit.Load(kitDir) : Kit.ExtractEmbedded(workspace);
            kit.ValidateContent();
            report.KitVersion = kit.Version;
            if (kit.Version != Platform.BuilderVersion)
                throw new BuildException($"Builder version {Platform.BuilderVersion} differs from kit version {kit.Version}.");
            report.KitHash = kit.ContentHash;
            RequireType(kit.StandaloneDll, "RepoXR.Standalone.CloudMenu");
            RequireType(kit.StandaloneDll, "RepoXR.Standalone.CloudHostButtonPatch");
            RequireType(kit.StandaloneDll, "RepoXR.Standalone.CloudInviteButtonPatch");
            RequireType(kit.StandaloneDll, "RepoQuest.Cloud.CloudRoomRouting");
            RequireType(kit.StandaloneDll, "RepoXR.Standalone.DialogLocalizationPatch");
            RequireType(kit.StandaloneDll, "RepoXR.Standalone.ModalLayout");
            RequireType(kit.StandaloneDll, "RepoXR.Standalone.RussianMenuTranslations");
            if (ReadStringConstant(kit.RepoXRDll, "RepoQuest.Localization.QuestStrings", "RussianTutorialFeatures") != "russian-vr-tutorial-v2")
                throw new BuildException("RepoXR is missing the Russian VR tutorial catalog; rebuild RepoXR before packaging.");
            if (ReadStringConstant(kit.RepoXRDll, "RepoQuest.Localization.QuestStrings", "NetworkMenuFeatures") != "network-play-v1")
                throw new BuildException("RepoXR is missing the Network play catalog; rebuild RepoXR before packaging.");
            if (ReadStringConstant(kit.RepoXRDll, "RepoQuest.Localization.QuestStrings", "CloudHostingFeatures") != "headset-host-v1")
                throw new BuildException("RepoXR is missing headset-host localization; rebuild RepoXR and pass -RepoXRDll when packaging.");
            RequireType(Path.Combine(kit.SteamDir, "Facepunch.Steamworks.Win64.dll"), "Steamworks.CloudSteamBackend");
            var tools = ToolLauncher.For(workspace);
            report.ToolCommand = tools.Command.ToArray();
            if (tools.Command.Count == 0 || !File.Exists(tools.Command[0]))
                throw new BuildException("The packaged build tools could not be located");
            if (!GameInstall.IsValid(gamePath)) throw new BuildException("Game not found: select the R.E.P.O. folder or pass --game");
            var game = new GameInstall(gamePath!);
            report.GamePath = game.Path;
            report.GameSha256 = game.AssemblyHash();
            report.GameTested = kit.Manifest.TestedGames.Any(t => t.AssemblyCSharp == report.GameSha256);
            if (!report.GameTested) throw new BuildException("This game version is not in the kit's tested game list");
            var unity = UnityInstall.Find(kit.Manifest.Unity.Version);
            report.UnityVersion = kit.Manifest.Unity.Version;
            report.UnityReady = unity is { HasAndroid: true };
            report.UnityLicensed = UnityLicense.LooksLicensed();
            if (!report.UnityReady) report.Notes.Add("On Build APK, the builder will install the pinned Unity editor and Android modules.");
            if (!report.UnityLicensed) report.Notes.Add("Sign in to Unity Hub and activate a suitable license when the builder asks.");
            var state = BuildState.Load(workspace.StatePath);
            var thirdParty = new ThirdParty(workspace, kit);
            var python = args.Contains("--prepare-tools")
                ? await PythonEnv.EnsureAsync(workspace, kit, thirdParty, CancellationToken.None) : null;
            if (args.Contains("--bake"))
            {
                var context = new BuildContext
                {
                    Workspace = workspace, Kit = kit, Game = game, Unity = unity, Python = python!, Tools = tools,
                    ThirdParty = thirdParty, State = state, Options = new BuildOptions(), Progress = NullProgress.Instance,
                };
                // This only launches the packaged baker. No AssetRipper export or Unity APK build is started.
                await new BakeStep().RunAsync(context, CancellationToken.None);
                report.BakePassed = true;
            }
            report.PythonPrepared = python != null;
            report.ReadyToStart = true;
        }
        catch (Exception error)
        {
            report.Errors.Add(error.ToString());
        }
        FileOps.WriteTextAtomic(Path.GetFullPath(reportPath), JsonSerializer.Serialize(report, KitManifest.Json));
        Console.WriteLine(JsonSerializer.Serialize(report, KitManifest.Json));
        return report.ReadyToStart ? 0 : 1;
    }

    internal static void RequireType(string assembly, string fullName)
    {
        using var pe = new PEReader(File.OpenRead(assembly));
        var metadata = pe.GetMetadataReader();
        if (!metadata.TypeDefinitions.Any(h => TypeName(metadata, h) == fullName))
            throw new BuildException($"{Path.GetFileName(assembly)} is missing {fullName}; rebuild the kit from modified sources");
    }

    internal static string? ReadStringConstant(string assembly, string type, string field)
    {
        using var pe = new PEReader(File.OpenRead(assembly));
        var metadata = pe.GetMetadataReader();
        foreach (var handle in metadata.TypeDefinitions.Where(h => TypeName(metadata, h) == type))
        foreach (var fieldHandle in metadata.GetTypeDefinition(handle).GetFields())
        {
            var definition = metadata.GetFieldDefinition(fieldHandle);
            if (metadata.GetString(definition.Name) != field || definition.GetDefaultValue().IsNil) continue;
            var constant = metadata.GetConstant(definition.GetDefaultValue());
            return constant.TypeCode == ConstantTypeCode.String ? Encoding.Unicode.GetString(metadata.GetBlobBytes(constant.Value)) : null;
        }
        return null;
    }

    private static string TypeName(MetadataReader metadata, TypeDefinitionHandle handle)
    {
        var type = metadata.GetTypeDefinition(handle);
        return metadata.GetString(type.Namespace) + "." + metadata.GetString(type.Name);
    }

    private sealed class DiagnosticReport
    {
        public string BuilderVersion { get; set; } = "";
        public string? KitVersion { get; set; }
        public string? KitHash { get; set; }
        public string Workspace { get; set; } = "";
        public string? GamePath { get; set; }
        public string? GameSha256 { get; set; }
        public bool GameTested { get; set; }
        public string[] ToolCommand { get; set; } = [];
        public string? UnityVersion { get; set; }
        public bool UnityReady { get; set; }
        public bool UnityLicensed { get; set; }
        public bool PythonPrepared { get; set; }
        public bool BakePassed { get; set; }
        public bool ReadyToStart { get; set; }
        public List<string> Notes { get; set; } = [];
        public List<string> Errors { get; set; } = [];
    }
}
