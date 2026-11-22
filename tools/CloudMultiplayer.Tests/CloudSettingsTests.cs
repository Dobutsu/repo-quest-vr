using RepoXR.Standalone;
using Xunit;

public sealed class CloudSettingsTests
{
    [Fact]
    public void NewInstallRequiresPhotonAppIdsForCloud()
    {
        var settings = new CloudSettings();
        Assert.True(settings.enabled);
        Assert.True(settings.Migrate());
        Assert.Equal(2, settings.modeSchemaVersion);
        Assert.False(settings.HasPhotonAppIds);
        Assert.Throws<FormatException>(() => settings.PrepareSave());
    }

    [Fact]
    public void MigrationPreservesCloudFieldsAndRunsOnlyOnce()
    {
        var settings = new CloudSettings { enabled = false, playerName = "Player", websiteUrl = "https://example.com", useCustomAppIds = true,
            realtimeAppId = "custom realtime", voiceAppId = "custom voice" };
        Assert.True(settings.Migrate());
        Assert.True(settings.enabled);
        Assert.Equal("Player", settings.playerName);
        Assert.Equal("https://example.com", settings.websiteUrl);
        Assert.Equal("custom realtime", settings.realtimeAppId);
        Assert.Equal("custom voice", settings.voiceAppId);
        Assert.True(settings.useCustomAppIds);
        settings.enabled = false;
        Assert.False(settings.Migrate());
        Assert.False(settings.enabled);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void MigrationClearsBundledIdsAndPreservesExplicitMode(bool enabled)
    {
        var settings = new CloudSettings
        {
            modeSchemaVersion = 1,
            enabled = enabled,
            realtimeAppId = Guid.NewGuid().ToString(),
            voiceAppId = Guid.NewGuid().ToString(),
        };
        Assert.True(settings.Migrate());
        Assert.Equal(enabled, settings.enabled);
        Assert.Empty(settings.realtimeAppId);
        Assert.Empty(settings.voiceAppId);
        Assert.False(settings.Migrate());
    }

    [Fact]
    public void EditingDraftDoesNotChangeSavedSettings()
    {
        var settings = new CloudSettings { modeSchemaVersion = 1 };
        var draft = settings.Copy();
        draft.enabled = false;
        draft.playerName = "New name";
        Assert.True(settings.enabled);
        Assert.Equal("Quest Player", settings.playerName);
    }

    [Fact]
    public void InvalidCloudDraftCannotOverwriteSavedSettings()
    {
        var saved = new CloudSettings
        {
            modeSchemaVersion = 2,
            realtimeAppId = Guid.NewGuid().ToString(),
            voiceAppId = Guid.NewGuid().ToString(),
        };
        var draft = saved.Copy();
        draft.websiteUrl = "javascript:alert(1)";
        Assert.Throws<FormatException>(() => draft.PrepareSave());
        Assert.Equal(saved.websiteUrl, new CloudSettings().websiteUrl);
        draft = saved.Copy();
        draft.playerName = "<b>name</b>";
        Assert.Throws<FormatException>(() => draft.PrepareSave());
        Assert.Equal(saved.realtimeAppId, saved.PrepareSave().realtimeAppId);
        Assert.Equal(saved.voiceAppId, saved.PrepareSave().voiceAppId);
        Assert.Equal("Quest Player", saved.playerName);
    }

    [Fact]
    public void SwitchingToSteamDoesNotRequireCloudFields()
    {
        var draft = new CloudSettings { enabled = false, realtimeAppId = "invalid", voiceAppId = "", websiteUrl = "", playerName = "" };
        var saved = draft.PrepareSave();
        Assert.False(saved.enabled);
        Assert.Equal(2, saved.modeSchemaVersion);
    }

    [Fact]
    public void RuntimeSnapshotSurvivesEditingAndCanDetectRevertedSelection()
    {
        var active = new CloudSettings();
        var desired = active.Copy();
        desired.playerName = "Changed";
        Assert.False(desired.MatchesRuntime(active));
        Assert.Equal("Quest Player", active.playerName);
        desired = active.Copy();
        desired.enabled = false;
        Assert.False(desired.MatchesRuntime(active));
        desired.enabled = true;
        Assert.True(desired.MatchesRuntime(active));
    }
}
