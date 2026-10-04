namespace RepoQuest.Builder;

/// <summary>
/// The builder's working folder. The layout is the one the Mac checkout has always used under work/, so our own builds
/// (QuestBuilder.Cli --dev) keep their exported project and Unity Library.
/// </summary>
public sealed class Workspace
{
    public Workspace(string root)
    {
        Root = Path.GetFullPath(root);
    }

    public string Root { get; }

    /// <summary>AssetRipper's export of the game (ExportedProject/, AuxiliaryFiles/)</summary>
    public string Export => Path.Combine(Root, "export", "REPO");
    public string ExportedProject => Path.Combine(Export, "ExportedProject");

    /// <summary>Game assemblies with RepoXR's patches baked in</summary>
    public string Baked => Path.Combine(Root, "baked", "all-android");
    public string Staging => Path.Combine(Root, "baked", "quest-staging");

    public string Project => Path.Combine(Root, "unity", "REPOQuest");
    public string Library => Path.Combine(Project, "Library");
    /// <summary>Where the Library waits while the project is recreated from the export</summary>
    public string LibraryKeep => Path.Combine(Root, "unity", ".Library-keep");
    /// <summary>The once-per-Library reimport of assets that use game scripts has happened (see UnityBuildStep)</summary>
    public string ReimportMarker => Path.Combine(Library, "RepoQuest.game-assets-reimported");

    public string Build => Path.Combine(Root, "build");
    public string Logs => Path.Combine(Root, "logs");
    /// <summary>Downloaded tools: AssetRipper, uv, Python and its packages</summary>
    public string Tools => Path.Combine(Root, "tools");
    /// <summary>Downloaded third-party releases the build uses: RepoXR, BepInEx, NuGet packages</summary>
    public string Deps => Path.Combine(Root, "deps");
    /// <summary>Downloaded archives; also read by the Python scripts (builtin shaders) and their package cache</summary>
    public string Downloads => Path.Combine(Root, "downloads");

    public string AddressablesEntries => Path.Combine(Root, "addressables-entries.json");
    public string ShaderStates => Path.Combine(Root, "shader-states.json");
    public string StatePath => Path.Combine(Root, "state.json");
    public string KitRoot => Path.Combine(Root, "kit");

    public void Create()
    {
        Directory.CreateDirectory(Root);
        Directory.CreateDirectory(Logs);
    }
}
