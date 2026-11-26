using RepoQuest.Builder.Pipeline;

namespace RepoQuest.Builder.Tests;

public sealed class GradleTests
{
    [Fact]
    public void ClearMergedAssets_ClearsEveryModule()
    {
        var workspace = new Workspace(Path.Combine(Path.GetTempPath(), "repoquest-gradle-" + Guid.NewGuid().ToString("N")));
        try
        {
            var gradle = Path.Combine(workspace.Library, "Bee", "Android", "Prj", "IL2CPP", "Gradle");
            string[] modules = ["launcher", "unityLibrary"];
            foreach (var module in modules)
            {
                var intermediates = Path.Combine(gradle, module, "build", "intermediates");
                Directory.CreateDirectory(Path.Combine(intermediates, "assets", "release", "mergeReleaseAssets", "bin", "Data"));
                Directory.CreateDirectory(Path.Combine(intermediates, "incremental", "mergeReleaseAssets"));
                Directory.CreateDirectory(Path.Combine(intermediates, "dex"));
            }

            UnityBuildStep.ClearMergedAssets(workspace);

            foreach (var module in modules)
            {
                var intermediates = Path.Combine(gradle, module, "build", "intermediates");
                Assert.False(Directory.Exists(Path.Combine(intermediates, "assets")), module);
                Assert.False(Directory.Exists(Path.Combine(intermediates, "incremental", "mergeReleaseAssets")), module);
                Assert.True(Directory.Exists(Path.Combine(intermediates, "dex")), module);
            }
        }
        finally
        {
            if (Directory.Exists(workspace.Root))
                Directory.Delete(workspace.Root, true);
        }
    }
}

public sealed class QuestInstallerTests
{
    [Theory]
    [InlineData("find: ./RepoXR/RuntimeDeps: Permission denied", "RepoXR/RuntimeDeps")]
    [InlineData("find: './saves/REPO_SAVE_1': Permission denied", "saves/REPO_SAVE_1")]
    public void DeniedFolder_IsRead(string line, string folder) =>
        Assert.Equal(folder, RepoQuest.Builder.Quest.QuestInstaller.DeniedFolderRegex().Match(line).Groups[1].Value);
}
