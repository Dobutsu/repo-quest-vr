using System.Text.Json;
using RepoXR.Standalone;
using Steamworks;
using Xunit;

public sealed class CloudIdentityTests
{
    private static readonly JsonSerializerOptions Json = new() { IncludeFields = true };

    [Fact]
    public void DraftGenerationKeepsTheActiveAndSavedIdentityUntilRestart()
    {
        var profile = CloudProfile.Resolve(null, CloudProfile.GenerateId());
        var active = new CloudSettings
        {
            realtimeAppId = Guid.NewGuid().ToString("D"),
            voiceAppId = Guid.NewGuid().ToString("D"),
        };
        var draft = active.Copy();
        draft.cloudPlayerId = CloudProfile.GenerateId(profile.gameId);
        var firstChoice = draft.cloudPlayerId;
        draft.cloudPlayerId = CloudProfile.GenerateId(profile.gameId, firstChoice);
        Assert.NotEqual(firstChoice, draft.cloudPlayerId);
        Assert.True(CloudProfile.IsLocalId(draft.cloudPlayerId));
        Assert.Equal("", active.cloudPlayerId);
        Assert.Equal(profile.userId, profile.gameId);
        Assert.NotEqual(profile.gameId, draft.cloudPlayerId);

        var saved = draft.PrepareSave();
        Assert.False(saved.MatchesRuntime(active));
        // Serialize the selected settings and apply them as a fresh launch does.
        var loaded = JsonSerializer.Deserialize<CloudSettings>(JsonSerializer.Serialize(saved, Json), Json);
        var restarted = CloudProfile.Resolve(profile, loaded.cloudPlayerId);
        Assert.Equal(saved.cloudPlayerId, restarted.userId);
        Assert.Equal(restarted.userId, new CloudSteamBackend(ulong.Parse(restarted.gameId), saved.playerName).SteamId.ToString());
        Assert.NotEqual(profile.userId, restarted.userId);
        Assert.True(saved.MatchesRuntime(loaded));
        Assert.Equal(restarted.userId, CloudProfile.Resolve(restarted, loaded.cloudPlayerId).userId);
        draft.cloudPlayerId = "";
        Assert.True(draft.MatchesRuntime(active));
    }

    [Fact]
    public void LegacySettingsRetainTheExistingProfileEvenOutsideTheLocalRange()
    {
        var settings = JsonSerializer.Deserialize<CloudSettings>("{\"playerName\":\"Legacy\"}", Json);
        var profile = new CloudProfile { userId = "1234", gameId = "1234" };
        Assert.Same(profile, CloudProfile.Resolve(profile, settings.cloudPlayerId));
        Assert.True(new CloudSettings { cloudPlayerId = null }.MatchesRuntime(new CloudSettings()));
    }

    [Theory]
    [InlineData("0")]
    [InlineData("1234")]
    [InlineData("162129590880305152")]
    [InlineData("18446744073709551616")]
    [InlineData("bad")]
    [InlineData(" 162129590880305153")]
    [InlineData("0162129590880305153")]
    public void InvalidSelectionCannotReplaceTheSavedIdentity(string id)
    {
        var settings = new CloudSettings();
        var draft = settings.Copy();
        draft.cloudPlayerId = id;
        Assert.Throws<FormatException>(() => draft.PrepareSave());
        Assert.Throws<FormatException>(() => CloudProfile.Resolve(null, id));
        Assert.Equal("", settings.cloudPlayerId);
    }

    [Fact]
    public void DamagedProfileRequiresRepairOrAnExplicitNewIdentity()
    {
        var damaged = new CloudProfile { userId = "123", gameId = "456" };
        Assert.Throws<FormatException>(() => CloudProfile.Resolve(damaged, ""));
        Assert.Throws<FormatException>(() => CloudProfile.Resolve(null, null));
        var selected = CloudProfile.GenerateId();
        var repaired = CloudProfile.Resolve(damaged, selected);
        Assert.Equal(selected, repaired.userId);
        Assert.Equal(selected, repaired.gameId);
        Assert.Equal("123", damaged.userId);
    }

    [Fact]
    public void FileWriteFailureKeepsCommittedSettingsAndProfileAndRetryPreservesABackup()
    {
        var directory = Path.Combine(Path.GetTempPath(), "repoquest-identity-" + Guid.NewGuid());
        Directory.CreateDirectory(directory);
        try
        {
            var settingsPath = Path.Combine(directory, "cloud-coop.json");
            var profilePath = Path.Combine(directory, "cloud-profile.json");
            var active = CloudProfile.Resolve(null, CloudProfile.GenerateId());
            var oldJson = JsonSerializer.Serialize(active, Json);
            CloudConfigFile.Write(profilePath, oldJson);
            CloudConfigFile.Write(settingsPath, "{}");
            Directory.CreateDirectory(settingsPath + ".tmp");
            var settings = new CloudSettings { cloudPlayerId = CloudProfile.GenerateId(active.gameId) };
            var newSettings = JsonSerializer.Serialize(settings, Json);
            var settingsError = Record.Exception(() => CloudConfigFile.Write(settingsPath, newSettings));
            Assert.True(settingsError is IOException || settingsError is UnauthorizedAccessException);
            Assert.Equal("{}", File.ReadAllText(settingsPath));
            Assert.Equal(oldJson, File.ReadAllText(profilePath));
            Directory.Delete(settingsPath + ".tmp");
            CloudConfigFile.Write(settingsPath, newSettings);
            Assert.Equal("{}", File.ReadAllText(settingsPath + ".bak"));

            // A failed startup profile write retains the committed selection for the next launch.
            Directory.CreateDirectory(profilePath + ".tmp");
            var next = CloudProfile.Resolve(active, settings.cloudPlayerId);
            var nextJson = JsonSerializer.Serialize(next, Json);
            var profileError = Record.Exception(() => CloudConfigFile.Write(profilePath, nextJson));
            Assert.True(profileError is IOException || profileError is UnauthorizedAccessException);
            Assert.Equal(oldJson, File.ReadAllText(profilePath));
            Assert.Equal(newSettings, File.ReadAllText(settingsPath));
            Directory.Delete(profilePath + ".tmp");
            CloudConfigFile.Write(profilePath, nextJson);
            Assert.Equal(oldJson, File.ReadAllText(profilePath + ".bak"));
            Assert.Equal(nextJson, File.ReadAllText(profilePath));
        }
        finally { Directory.Delete(directory, true); }
    }
}
