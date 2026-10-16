using RepoQuest.Builder.Pipeline;

namespace RepoQuest.Builder.Tests;

public sealed class HeadsetTests
{
    [Theory]
    [InlineData("REPO-Quest-1.0.5", "REPO-Pico-1.0.5")]
    [InlineData("repo-quest-vr", "repo-quest-vr-pico")]
    public void PicoApk_IsNamedAfterTheQuestOne(string quest, string pico) =>
        Assert.Equal(pico, BuildOptions.PicoApkName(quest));

    [Fact]
    public void HeadsetApk_IsUnitysForQuest()
    {
        var workspace = new Workspace(Path.Combine(Path.GetTempPath(), "repoquest-headset"));
        var quest = new BuildOptions { ApkName = "REPO-Quest-1.0.5" };
        var pico = new BuildOptions { ApkName = "REPO-Quest-1.0.5", Headset = Headset.Pico };
        Assert.Equal(quest.ApkPath(workspace), quest.HeadsetApkPath(workspace));
        Assert.Equal(quest.ApkPath(workspace), pico.ApkPath(workspace));
        Assert.Equal(Path.Combine(workspace.Build, "REPO-Pico-1.0.5.apk"), pico.HeadsetApkPath(workspace));
    }

    [Theory]
    [InlineData(null, Headset.Quest)]
    [InlineData("quest", Headset.Quest)]
    [InlineData("pico", Headset.Pico)]
    public void Headsets_Parse(string? id, Headset headset)
    {
        Assert.Equal(headset, BuildOptions.ParseHeadset(id));
        if (id is not null)
            Assert.Equal(id, BuildOptions.HeadsetId(headset));
    }

    [Fact]
    public void UnknownHeadset_IsRefused() => Assert.Throws<BuildException>(() => BuildOptions.ParseHeadset("vive"));

    [Fact]
    public void PicoStep_IsLastAndOnlyForPico()
    {
        Assert.Equal("pico", BuildPipeline.Steps[^1].Id);
        Assert.DoesNotContain("pico", BuilderService.StepsFor(Headset.Quest));
        Assert.Equal("pico", BuilderService.StepsFor(Headset.Pico)[^1]);
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
