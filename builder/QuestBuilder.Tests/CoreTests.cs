using System.Text;
using RepoQuest.Builder.Pipeline;
using RepoQuest.Builder.Quest;
using RepoQuest.Builder.Updates;

namespace RepoQuest.Builder.Tests;

public sealed class CoreTests : IDisposable
{
    private readonly string temp = Directory.CreateTempSubdirectory("repoquest-tests").FullName;

    public void Dispose() => Directory.Delete(temp, recursive: true);

    [Theory]
    [InlineData("1.2.3", 10203)]
    [InlineData("1.0.0", 10000)]
    [InlineData("0.0.0", 1)]
    [InlineData("dev", 1)]
    [InlineData("2.10.5", 21005)]
    public void VersionCode_GrowsWithReleases(string version, int code) =>
        Assert.Equal(code, BuildOptions.VersionCodeOf(version));

    [Fact]
    public void PluginGuids_MatchTheBashPipeline()
    {
        // `printf 'repoxr-quest:RepoXR.dll' | md5` and friends, as in the project the bash scripts made
        Assert.Equal("20cc4a9a9eb55b6361834b8b17f129a0", Hashing.Md5Hex("repoxr-quest:RepoXR.dll"));
        Assert.Equal("807a865d60474dfcc65c666ec8321ca9", Hashing.Md5Hex("repoquest-steamkit:SteamKit2"));
        Assert.Equal("12dcbb9dda5644a2b94c88b767268d0b", Hashing.Md5Hex("repoquest-native:libopus_egpv"));
    }

    [Fact]
    public void Signing_AcceptsOnlyTheRightKeyAndBytes()
    {
        var (privateKey, publicKey) = ReleaseSigning.GenerateKey();
        var (_, otherPublicKey) = ReleaseSigning.GenerateKey();
        var data = Encoding.UTF8.GetBytes("{\"version\":\"1.0.1\"}");
        var signature = ReleaseSigning.Sign(data, privateKey);

        Assert.True(ReleaseSigning.Verify(data, signature, publicKey));
        Assert.Equal(publicKey, ReleaseSigning.PublicKeyOf(privateKey));
        Assert.False(ReleaseSigning.Verify(data, signature, otherPublicKey));
        Assert.False(ReleaseSigning.Verify(Encoding.UTF8.GetBytes("{\"version\":\"9.9.9\"}"), signature, publicKey));
        Assert.False(ReleaseSigning.Verify(data, "not base64!", publicKey));
    }

    [Theory]
    [InlineData("1.0.1", "1.0.0", true)]
    [InlineData("1.10.0", "1.9.9", true)]
    [InlineData("1.0.0", "1.0.0", false)]
    [InlineData("0.9.0", "1.0.0", false)]
    [InlineData("garbage", "1.0.0", false)]
    public void IsNewer_ComparesVersionsNumerically(string candidate, string current, bool newer) =>
        Assert.Equal(newer, ReleaseManifest.IsNewer(candidate, current));

    [Fact]
    public void ReleaseManifest_ReadsWhatReleaseCommandsWrite()
    {
        const string json = """
            {
              "version": "1.0.1",
              "published": "2026-10-10T12:00:00Z",
              "notes": { "ru": "Исправления", "en": "Fixes" },
              "builder": { "win-x64": { "url": "RepoQuestBuilder-1.0.1-win-x64.zip", "sha256": "ab", "size": 42 } }
            }
            """;
        var manifest = ReleaseManifest.Parse(Encoding.UTF8.GetBytes(json));

        Assert.Equal("1.0.1", manifest.Version);
        Assert.Equal("Исправления", manifest.NotesFor("ru"));
        Assert.Equal("Fixes", manifest.NotesFor("de"));
        Assert.Equal(42, manifest.Builder["win-x64"].Size);
    }

    [Fact]
    public void Adb_ParsesDevicesAndStates()
    {
        var devices = Adb.ParseDevices(
        [
            "* daemon not running; starting now at tcp:5037",
            "* daemon started successfully",
            "List of devices attached",
            "2G0YC5ZG8F0123       device usb:1-1 product:eureka model:Quest_3 device:eureka transport_id:2",
            "192.168.1.16:5555    unauthorized transport_id:3",
            "",
        ]);

        Assert.Equal(2, devices.Count);
        Assert.True(devices[0].Ready);
        Assert.True(devices[0].IsQuest);
        Assert.Equal("Quest 3", devices[0].Model);
        Assert.True(devices[1].Unauthorized);
        Assert.False(devices[1].Ready);
    }

    [Theory]
    [InlineData("[ 1234/5678  12s] C_Android_arm64 Library/Bee/artifacts/x.o", true)]
    [InlineData("[291/296    0s] Csc Library/Bee/artifacts/1300b0aP.dag/Assembly-CSharp.dll", true)]
    [InlineData("Start importing Assets/x.prefab", false)]
    public void BeeSteps_AreRecognized(string line, bool match) =>
        Assert.Equal(match, UnityBuildStep.BeeStepRegex().IsMatch(line));

    [Theory]
    [InlineData("2026-10-09T22:21:43.268Z|0x0203493f80|Start importing ProjectSettings/TagManager.asset", "Start importing ProjectSettings/TagManager.asset")]
    [InlineData("2026-10-09T22:21:43.268Z|0x0203493f80|[ 323/16996  0s] CopyFiles x", "[ 323/16996  0s] CopyFiles x")]
    [InlineData("[QuestBuild] result: Succeeded", "[QuestBuild] result: Succeeded")]
    [InlineData("a|0x1|b", "a|0x1|b")]
    public void UnityLogTimestamps_AreStripped(string line, string expected) =>
        Assert.Equal(expected, UnityBuildStep.WithoutTimestamp(line));

    [Fact]
    public void Kit_ReadsTheManifestTemplate()
    {
        var kitDir = Path.Combine(temp, "kit");
        Directory.CreateDirectory(kitDir);
        File.Copy(Path.Combine(RepoRoot(), "builder", "kit.json"), Path.Combine(kitDir, "kit.json"));

        var kit = Kit.Load(kitDir);

        Assert.Equal("2022.3.62f3", kit.Manifest.Unity.Version);
        Assert.Equal("1.2.3", kit.Manifest.Downloads.RepoXR.Version);
        Assert.Contains(kit.Manifest.Downloads.NuGet, p => p.Id == "customdiscoverstatelib" && p.Lib == "lib/netstandard2.1");
        Assert.True(kit.Manifest.Downloads.AssetRipper.Files.ContainsKey("win-x64"));
        Assert.True(kit.Manifest.Downloads.Uv.Files.ContainsKey("win-x64"));
        Assert.NotEmpty(kit.Manifest.TestedGames);
        Assert.All(kit.Manifest.Downloads.NuGet, p => Assert.Equal(64, p.Sha256.Length));
    }

    [Fact]
    public void WorkspaceProblems_AreReported()
    {
        Assert.Equal("workspace-not-ascii", AppSettings.WorkspaceProblem(Path.Combine(temp, "Сборка"), null));
        var game = Path.Combine(temp, "REPO");
        Assert.Equal("workspace-in-game", AppSettings.WorkspaceProblem(Path.Combine(game, "build"), game));
        Assert.Null(AppSettings.WorkspaceProblem(Path.Combine(temp, "RepoQuest"), game));
    }

    [Fact]
    public void BuildState_SurvivesARoundTripAndDamage()
    {
        var path = Path.Combine(temp, "state.json");
        var state = new BuildState { GamePath = "/games/REPO", DownloadsAccepted = true };
        state.Stamps["export"] = "abc";
        state.Save(path);

        var loaded = BuildState.Load(path);
        Assert.Equal("/games/REPO", loaded.GamePath);
        Assert.True(loaded.DownloadsAccepted);
        Assert.Equal("abc", loaded.Stamps["export"]);

        File.WriteAllText(path, "{ broken");
        Assert.Null(BuildState.Load(path).GamePath);
    }

    [Fact]
    public async Task UpdateSource_ReadsALocalFolder()
    {
        var release = Path.Combine(temp, "release");
        Directory.CreateDirectory(release);
        var zip = Path.Combine(release, "RepoQuestBuilder-99.0.0-win-x64.zip");
        File.WriteAllText(zip, "zip");
        var rid = Platform.Rid;
        File.WriteAllText(Path.Combine(release, "latest.json"),
            $$"""{ "version": "99.0.0", "published": "2026-10-10T00:00:00Z", "notes": {}, "builder": { "{{rid}}": { "url": "RepoQuestBuilder-99.0.0-win-x64.zip", "sha256": "{{Hashing.Sha256File(zip)}}", "size": 3 } } }""");

        var source = UpdateSource.Resolve(release)!;
        var manifest = await source.CheckAsync(CancellationToken.None);

        Assert.NotNull(manifest);
        var downloaded = await source.DownloadAsync(manifest, Path.Combine(temp, "downloads"), null, CancellationToken.None);
        Assert.Equal("zip", File.ReadAllText(downloaded));
    }

    private static string RepoRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !Directory.Exists(Path.Combine(dir.FullName, "builder")))
            dir = dir.Parent;
        return dir?.FullName ?? throw new InvalidOperationException("repository root not found");
    }
}
