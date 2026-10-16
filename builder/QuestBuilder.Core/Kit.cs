using System.IO.Compression;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace RepoQuest.Builder;

public sealed record PinnedFile(string Url, string Sha256);

public sealed record PinnedTool(string Version, Dictionary<string, PinnedFile> Files)
{
    public PinnedFile ForThisMachine() => Files.TryGetValue(Platform.Rid, out var file)
        ? file
        : throw new BuildException($"No {Platform.Rid} download in kit.json");
}

public sealed record PinnedRelease(string Version, string Url, string Sha256);

public sealed record PinnedPackage(string Id, string Version, string Url, string Sha256, string Lib);

public sealed record KitDownloads(
    PinnedTool AssetRipper,
    PinnedTool Uv,
    PinnedRelease RepoXR,
    PinnedRelease BepInEx,
    PinnedPackage[] NuGet,
    PinnedRelease BuiltinShaders,
    PinnedTool? Java = null,
    PinnedRelease? Ovrport = null);

public sealed record UnityVersion(string Version, string Changeset);

public sealed record TestedGame(string Name, string AssemblyCSharp);

/// <summary>kit.json: the kit's version and everything the builder downloads, pinned by hash</summary>
public sealed record KitManifest(
    string Version,
    UnityVersion Unity,
    string Python,
    KitDownloads Downloads,
    TestedGame[] TestedGames)
{
    public static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web)
    {
        WriteIndented = true,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        // Release notes and paths in Russian stay readable (the files are never embedded in HTML)
        Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
    };
}

/// <summary>
/// The kit: the scripts, Unity tools and prebuilt pieces (our code and what we build from source) that turn the
/// player's game into the Quest project. The released builder carries it as an embedded zip (scripts/make-kit.sh
/// assembles it, scripts/release.sh embeds it); our own builds point at work/kit.
/// </summary>
public sealed class Kit
{
    private const string EmbeddedName = "kit.zip";

    private Kit(string root, KitManifest manifest)
    {
        Root = root;
        Manifest = manifest;
    }

    public string Root { get; }
    public KitManifest Manifest { get; }
    public string Version => Manifest.Version;

    public string Scripts => Path.Combine(Root, "scripts");
    public string UnityTools => Path.Combine(Root, "unity-tools");
    public string PythonLock => Path.Combine(Root, "python", "requirements.lock");
    public string RepoXRDll => Path.Combine(Root, "prebuilt", "repoxr", "RepoXR.dll");
    public string StandaloneDll => Path.Combine(Root, "prebuilt", "standalone", "RepoXR.Standalone.dll");
    /// <summary>SteamShim (Facepunch.Steamworks.Win64.dll) and SteamKit2 with its dependencies</summary>
    public string SteamDir => Path.Combine(Root, "prebuilt", "steam");
    public string Opus => Path.Combine(Root, "prebuilt", "android", "libopus_egpv.so");
    /// <summary>RepoXR's asset bundle and Addressables built for Android (repoxrassets, Addressables/)</summary>
    public string RepoXRAndroidAssets => Path.Combine(Root, "prebuilt", "repoxr-android");

    /// <summary>Hash of the kit's content: part of the pipeline's stamps, so a new kit rebuilds what it affects</summary>
    public string ContentHash => contentHash ??= Hashing.Directory(Root,
        rel => !rel.Contains("__pycache__") && !rel.EndsWith(".pyc") && rel != ".content-hash");
    private string? contentHash;

    public static Kit Load(string root)
    {
        root = Path.GetFullPath(root);
        var manifestPath = Path.Combine(root, "kit.json");
        if (!File.Exists(manifestPath))
            throw new BuildException($"{root} is not a builder kit (no kit.json); scripts/make-kit.sh makes one");
        var manifest = JsonSerializer.Deserialize<KitManifest>(File.ReadAllText(manifestPath), KitManifest.Json)
                       ?? throw new BuildException($"{manifestPath} is empty");
        var kit = new Kit(root, manifest);
        var hashFile = Path.Combine(root, ".content-hash");
        if (File.Exists(hashFile))
            kit.contentHash = File.ReadAllText(hashFile).Trim();
        return kit;
    }

    /// <summary>Whether this builder carries a kit (release builds)</summary>
    public static bool HasEmbedded => typeof(Kit).Assembly.GetManifestResourceInfo(EmbeddedName) is not null
                                      || EntryAssemblyResource() is not null;

    /// <summary>Unpacks the embedded kit into the workspace (kit/&lt;version&gt;), once per version</summary>
    public static Kit ExtractEmbedded(Workspace workspace)
    {
        using var stream = typeof(Kit).Assembly.GetManifestResourceStream(EmbeddedName) ?? EntryAssemblyResource()
                           ?? throw new BuildException("This builder has no kit inside (a development build?)");
        var target = Path.Combine(workspace.KitRoot, Platform.BuilderVersion);
        var marker = Path.Combine(target, ".content-hash");
        using var buffer = new MemoryStream();
        stream.CopyTo(buffer);
        var zipHash = Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(buffer.ToArray()));
        var zipMarker = Path.Combine(target, ".zip-hash");
        if (!File.Exists(zipMarker) || File.ReadAllText(zipMarker).Trim() != zipHash || !File.Exists(marker))
        {
            var temp = target + ".extracting";
            FileOps.DeleteDirectory(temp);
            buffer.Position = 0;
            using (var zip = new ZipArchive(buffer, ZipArchiveMode.Read, leaveOpen: true))
                zip.ExtractToDirectory(temp, overwriteFiles: true);
            var kit = Load(temp);
            File.WriteAllText(Path.Combine(temp, ".content-hash"), kit.ContentHash);
            File.WriteAllText(Path.Combine(temp, ".zip-hash"), zipHash);
            FileOps.DeleteDirectory(target);
            Directory.Move(temp, target);
            // Kits of older builder versions are no longer needed
            foreach (var old in Directory.EnumerateDirectories(workspace.KitRoot).Where(d => d != target))
                FileOps.DeleteDirectory(old);
        }
        return Load(target);
    }

    private static Stream? EntryAssemblyResource() =>
        System.Reflection.Assembly.GetEntryAssembly()?.GetManifestResourceStream(EmbeddedName);
}
