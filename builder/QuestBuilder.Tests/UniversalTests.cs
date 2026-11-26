using RepoQuest.Builder.Pipeline;

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

    private static string RepoRoot()
    {
        var dir = AppContext.BaseDirectory;
        while (!Directory.Exists(Path.Combine(dir, "builder")))
            dir = Path.GetDirectoryName(dir) ?? throw new DirectoryNotFoundException("repository root");
        return dir;
    }
}
