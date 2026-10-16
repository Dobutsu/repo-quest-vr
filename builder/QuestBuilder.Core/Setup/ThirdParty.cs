namespace RepoQuest.Builder.Setup;

/// <summary>
/// Third-party releases the build needs, downloaded from their publishers and checked against the hashes in kit.json:
/// AssetRipper and uv (tools), RepoXR's Thunderstore release (its RuntimeDeps), BepInEx 5 (0Harmony, MonoMod),
/// NuGet packages RepoXR builds against, Unity's built-in shader source; for Pico, a Java 17 runtime and ovrport.
/// </summary>
public sealed class ThirdParty
{
    private readonly Workspace workspace;
    private readonly KitDownloads pins;

    public ThirdParty(Workspace workspace, Kit kit)
    {
        this.workspace = workspace;
        pins = kit.Manifest.Downloads;
    }

    public IProgress<(string Name, DownloadProgress Progress)>? Progress { get; set; }

    /// <summary>AssetRipper.GUI.Free executable</summary>
    public async Task<string> AssetRipperAsync(CancellationToken ct)
    {
        var file = pins.AssetRipper.ForThisMachine();
        var dir = await ToolAsync("assetripper", pins.AssetRipper.Version, file, ct);
        return Path.Combine(dir, Platform.Exe("AssetRipper.GUI.Free"));
    }

    /// <summary>uv executable (Python and its packages)</summary>
    public async Task<string> UvAsync(CancellationToken ct)
    {
        var file = pins.Uv.ForThisMachine();
        var dir = await ToolAsync("uv", pins.Uv.Version, file, ct);
        // The archives have the binary at the top (Windows) or in a uv-<target>/ folder (macOS)
        var uv = Directory.EnumerateFiles(dir, Platform.Exe("uv"), SearchOption.AllDirectories).FirstOrDefault()
                 ?? throw new BuildException($"uv not found in {dir}");
        return uv;
    }

    /// <summary>RepoXR's RuntimeDeps folder (Unity XR assemblies the mod ships with)</summary>
    public async Task<string> RepoXRRuntimeDepsAsync(CancellationToken ct)
    {
        var dir = await ReleaseAsync("RepoXR", pins.RepoXR, ".zip", ct);
        return Path.Combine(dir, "BepInEx", "plugins", "RepoXR", "RuntimeDeps");
    }

    /// <summary>BepInEx/core of BepInEx 5 (0Harmony, BepInEx, MonoMod, Mono.Cecil)</summary>
    public async Task<string> BepInExCoreAsync(CancellationToken ct)
    {
        var dir = await ReleaseAsync("BepInEx", pins.BepInEx, ".zip", ct);
        return Path.Combine(dir, "BepInEx", "core");
    }

    /// <summary>The lib folder of a NuGet package from kit.json (e.g. lib/netstandard2.1)</summary>
    public async Task<string> NuGetLibAsync(string id, CancellationToken ct)
    {
        var package = pins.NuGet.FirstOrDefault(p => p.Id.Equals(id, StringComparison.OrdinalIgnoreCase))
                      ?? throw new BuildException($"kit.json has no NuGet package {id}");
        var dir = Path.Combine(workspace.Deps, "nuget", package.Id, package.Version);
        await EnsureExtractedAsync(package.Url, package.Sha256, $"{package.Id}.{package.Version}.nupkg", dir, ct);
        return Path.Combine(dir, package.Lib.Replace('/', Path.DirectorySeparatorChar));
    }

    /// <summary>The java executable of a Java 17 runtime (Eclipse Temurin JRE): ovrport needs 17, Unity brings 11</summary>
    public async Task<string> JavaAsync(CancellationToken ct)
    {
        var pin = pins.Java ?? throw new BuildException("kit.json has no Java download");
        var dir = await ToolAsync("java", pin.Version, pin.ForThisMachine(), ct);
        // bin/java.exe (Windows) or <jdk>/Contents/Home/bin/java (macOS)
        return Directory.EnumerateFiles(dir, Platform.Exe("java"), SearchOption.AllDirectories)
                   .FirstOrDefault(f => Path.GetFileName(Path.GetDirectoryName(f)) == "bin")
               ?? throw new BuildException($"java not found in {dir}");
    }

    /// <summary>ovrport's command-line jar (github.com/ovrport/app), which turns a Quest APK into a Pico one</summary>
    public async Task<string> OvrportJarAsync(CancellationToken ct)
    {
        var pin = pins.Ovrport ?? throw new BuildException("kit.json has no ovrport download");
        var dir = Path.Combine(workspace.Tools, $"ovrport-{pin.Version}");
        await EnsureExtractedAsync(pin.Url, pin.Sha256, $"ovrport-cli-{pin.Version}.zip", dir, ct);
        return Directory.EnumerateFiles(dir, "*.jar").FirstOrDefault()
               ?? throw new BuildException($"ovrport's jar not found in {dir}");
    }

    /// <summary>Unity's built-in shader source, where scripts/make_lamp_shaders.py looks for it</summary>
    public async Task BuiltinShadersAsync(CancellationToken ct)
    {
        var pin = pins.BuiltinShaders;
        var archive = Path.Combine(workspace.Downloads, $"builtin_shaders-{pin.Version}.zip");
        await Downloader.DownloadAsync(pin.Url, archive, pin.Sha256, Report("builtin shaders"), ct);
    }

    private async Task<string> ToolAsync(string name, string version, PinnedFile file, CancellationToken ct)
    {
        var dir = Path.Combine(workspace.Tools, $"{name}-{version}");
        var archiveName = Path.GetFileName(new Uri(file.Url).AbsolutePath);
        await EnsureExtractedAsync(file.Url, file.Sha256, archiveName, dir, ct);
        return dir;
    }

    private async Task<string> ReleaseAsync(string name, PinnedRelease release, string extension, CancellationToken ct)
    {
        var dir = Path.Combine(workspace.Deps, $"{name}-{release.Version}");
        await EnsureExtractedAsync(release.Url, release.Sha256, $"{name}-{release.Version}{extension}", dir, ct);
        return dir;
    }

    /// <summary>Downloads (if needed) and extracts (once per archive hash) into <paramref name="dir"/></summary>
    private async Task EnsureExtractedAsync(string url, string sha256, string archiveName, string dir, CancellationToken ct)
    {
        var marker = Path.Combine(dir, ".repoquest-sha256");
        if (File.Exists(marker) && File.ReadAllText(marker).Trim() == sha256)
            return;
        var archive = Path.Combine(workspace.Downloads, archiveName);
        await Downloader.DownloadAsync(url, archive, sha256, Report(archiveName), ct);
        await Archive.ExtractAsync(archive, dir, ct);
        File.WriteAllText(marker, sha256);
    }

    private IProgress<DownloadProgress>? Report(string name) =>
        Progress is { } progress ? new Progress<DownloadProgress>(p => progress.Report((name, p))) : null;
}
