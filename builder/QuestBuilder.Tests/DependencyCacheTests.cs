using System.IO.Compression;
using System.Text.Json;
using RepoQuest.Builder.Setup;

namespace RepoQuest.Builder.Tests;

public sealed class DependencyCacheTests
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task BundledDependenciesAreVerifiedBeforeTheyAreUsed(bool corrupt)
    {
        var root = Directory.CreateTempSubdirectory("kit-cache-").FullName;
        try
        {
            var kitRoot = Path.Combine(root, "kit");
            var cache = Path.Combine(kitRoot, "downloads");
            Directory.CreateDirectory(cache);
            var packagePath = Path.Combine(cache, "fixture.1.0.0.nupkg");
            using (var zip = ZipFile.Open(packagePath, ZipArchiveMode.Create))
            {
                using var text = new StreamWriter(zip.CreateEntry("lib/fixture.txt").Open());
                text.Write("cached dependency");
            }
            var hash = Hashing.Sha256File(packagePath);
            var repoRoot = FindRepo();
            var manifest = JsonSerializer.Deserialize<KitManifest>(File.ReadAllText(Path.Combine(repoRoot, "builder", "kit.json")), KitManifest.Json)!;
            manifest = manifest with
            {
                Downloads = manifest.Downloads with
                {
                    NuGet = [new PinnedPackage("fixture", "1.0.0", "https://invalid.example/not-contacted", hash, "lib")],
                },
            };
            File.WriteAllText(Path.Combine(kitRoot, "kit.json"), JsonSerializer.Serialize(manifest, KitManifest.Json));
            if (corrupt) File.AppendAllText(packagePath, "tampering");
            var workspace = new Workspace(Path.Combine(root, "workspace"));
            var thirdParty = new ThirdParty(workspace, Kit.Load(kitRoot));
            if (corrupt)
            {
                var error = await Assert.ThrowsAsync<BuildException>(() => thirdParty.NuGetLibAsync("fixture", CancellationToken.None));
                Assert.Contains("cache is damaged", error.Message);
                Assert.False(File.Exists(Path.Combine(workspace.Downloads, "fixture.1.0.0.nupkg")));
            }
            else
            {
                var library = await thirdParty.NuGetLibAsync("fixture", CancellationToken.None);
                Assert.Equal("cached dependency", File.ReadAllText(Path.Combine(library, "fixture.txt")));
                Assert.Equal(hash, Hashing.Sha256File(Path.Combine(workspace.Downloads, "fixture.1.0.0.nupkg")));
            }
        }
        finally { Directory.Delete(root, recursive: true); }
    }

    private static string FindRepo()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory != null && !File.Exists(Path.Combine(directory.FullName, "builder", "kit.json"))) directory = directory.Parent;
        return directory?.FullName ?? throw new DirectoryNotFoundException("Repository root");
    }
}
