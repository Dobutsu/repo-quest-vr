using RepoQuest.Builder.Pipeline;
using RepoQuest.Builder.Setup;

namespace RepoQuest.Builder.Tests;

public sealed class UniversalTests
{
    [Theory]
    [InlineData("REPO-Quest-1.5.0", "REPO-VR-1.5.0")]
    [InlineData("repo-quest-vr", "repo-quest-vr-universal")]
    public void UniversalApk_IsNamedAfterUnitys(string unity, string universal) =>
        Assert.Equal(universal, BuildOptions.UniversalApkName(unity));

    [Fact]
    public void FinalApk_IsTheUniversalOne_UnlessQuestOnly()
    {
        var workspace = new Workspace(Path.Combine(Path.GetTempPath(), "repoquest-universal"));
        var universal = new BuildOptions { ApkName = "REPO-Quest-1.5.0" };
        var questOnly = new BuildOptions { ApkName = "REPO-Quest-1.5.0", Universal = false };
        Assert.Equal(Path.Combine(workspace.Build, "REPO-VR-1.5.0.apk"), universal.FinalApkPath(workspace));
        Assert.Equal(Path.Combine(workspace.Build, "REPO-Quest-1.5.0.apk"), universal.ApkPath(workspace));
        Assert.Equal(questOnly.ApkPath(workspace), questOnly.FinalApkPath(workspace));
    }

    [Fact]
    public void UniversalStep_IsLastAndAlwaysListed()
    {
        Assert.Equal("universal", BuildPipeline.Steps[^1].Id);
        Assert.Equal("universal", BuilderService.Steps[^1]);
    }

    [Fact]
    public void Kit_PinsJavaAndOvrport()
    {
        var kit = System.Text.Json.JsonSerializer.Deserialize<KitManifest>(
            File.ReadAllText(Path.Combine(RepoRoot(), "builder", "kit.json")), KitManifest.Json)!;
        Assert.NotNull(kit.Downloads.Ovrport);
        Assert.True(kit.Downloads.Java!.Files.ContainsKey("win-x64"));
    }

    [Fact]
    public void UniversalPreparation_RequiresRegisteredReleaseAndLoaders()
    {
        var root = Directory.CreateTempSubdirectory("repoquest-universal-");
        try
        {
            var workspace = new Workspace(root.FullName);
            var version = UniversalPreparation.DefaultVersion;
            var index = Path.Combine(workspace.Ovrport, "installed.json");
            var lib = Path.Combine(workspace.Ovrport, "libraries", version, "lib", "arm64-v8a");
            FileOps.WriteText(index, """{"releases":[{"version":"3.3.0"}]}""");

            Assert.False(UniversalPreparation.LibrariesReady(workspace, version));
            foreach (var name in new[] { "libopenxr_loader.so", "libopenxr_loader_meta.so", "libopenxr_loader_pico.so" })
                FileOps.WriteText(Path.Combine(lib, name), "loader");
            Assert.True(UniversalPreparation.LibrariesReady(workspace, version));

            FileOps.WriteText(index, """{"releases":[{"version":"3.2.2"}]}""");
            Assert.False(UniversalPreparation.LibrariesReady(workspace, version));
            FileOps.WriteText(index, "{");
            Assert.False(UniversalPreparation.LibrariesReady(workspace, version));
        }
        finally
        {
            FileOps.DeleteDirectory(root.FullName);
        }
    }

    [Theory]
    [InlineData(false, StepStatus.Failed)]
    [InlineData(true, StepStatus.Cancelled)]
    public async Task UniversalPreparation_ReportsFailureOrCancellation(bool cancel, StepStatus expected)
    {
        var root = Directory.CreateTempSubdirectory("repoquest-preparation-");
        try
        {
            var manifest = System.Text.Json.JsonSerializer.Deserialize<KitManifest>(
                File.ReadAllText(Path.Combine(RepoRoot(), "builder", "kit.json")), KitManifest.Json)!;
            FileOps.WriteText(Path.Combine(root.FullName, "kit.json"), System.Text.Json.JsonSerializer.Serialize(
                manifest with { Downloads = manifest.Downloads with { Java = null } }, KitManifest.Json));
            var workspace = new Workspace(Path.Combine(root.FullName, "workspace"));
            var progress = new PreparationProgress();
            var task = UniversalPreparation.EnsureAsync(workspace, Kit.Load(root.FullName), progress,
                new CancellationToken(cancel));

            if (cancel)
                await Assert.ThrowsAnyAsync<OperationCanceledException>(() => task);
            else
                Assert.Equal("universal-setup-java", (await Assert.ThrowsAsync<BuildException>(() => task)).Code);
            Assert.Equal(expected, progress.Status);
        }
        finally
        {
            FileOps.DeleteDirectory(root.FullName);
        }
    }

    private sealed class PreparationProgress : IBuildProgress
    {
        public StepStatus Status { get; private set; }
        public void StepStarted(string step) => Assert.Equal(UniversalPreparation.StepId, step);
        public void StepProgress(string step, double? fraction, string? stage = null, string? detail = null) { }
        public void StepFinished(string step, StepStatus status) => Status = status;
    }

    private static string RepoRoot()
    {
        var dir = AppContext.BaseDirectory;
        while (!Directory.Exists(Path.Combine(dir, "builder")))
            dir = Path.GetDirectoryName(dir) ?? throw new DirectoryNotFoundException("repository root");
        return dir;
    }
}

public sealed class CaseTwinTests
{
    private static string Zip(string dir, string name, params string[] entries)
    {
        var path = Path.Combine(dir, name);
        using var zip = System.IO.Compression.ZipFile.Open(path, System.IO.Compression.ZipArchiveMode.Create);
        foreach (var entry in entries)
            using (var writer = new StreamWriter(zip.CreateEntry(entry).Open()))
                writer.Write(entry);
        return path;
    }

    [Fact]
    public void MissingCaseTwins_FindsTheLostOne()
    {
        var dir = Directory.CreateTempSubdirectory("repoquest-twins").FullName;
        try
        {
            var input = Zip(dir, "in.apk", "res/Dm.xml", "res/dM.xml", "res/ab.xml", "lib/x.so");
            Assert.Equal(["res/dM.xml"], UniversalStep.MissingCaseTwins(input, Zip(dir, "lost.apk", "res/Dm.xml", "res/ab.xml", "lib/x.so", "lib/new.so")));
            Assert.Empty(UniversalStep.MissingCaseTwins(input, Zip(dir, "kept.apk", "res/Dm.xml", "res/dM.xml", "lib/x.so")));
        }
        finally
        {
            Directory.Delete(dir, true);
        }
    }
}
