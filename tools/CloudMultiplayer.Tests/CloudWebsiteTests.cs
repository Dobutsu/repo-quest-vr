using RepoQuest.Cloud;
using RepoXR.Standalone;
using System.Text.Json;
using Xunit;

public sealed class CloudWebsiteTests
{
    [Theory]
    [InlineData("http://localhost:8080/code")]
    [InlineData("https://example.org/path?room=123#code")]
    public void PreservesFullAddressAndTrimsOuterWhitespace(string value) =>
        Assert.Equal(value, CloudWebsite.Load("  " + value + "  "));

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("  ")]
    public void EmptyAddressDisablesWebsite(string value)
    {
        Assert.Empty(CloudWebsite.Normalize(value));
        Assert.Empty(CloudWebsite.Load(value));
        Assert.Empty(new CloudSettings
        {
            realtimeAppId = Guid.NewGuid().ToString("D"),
            voiceAppId = Guid.NewGuid().ToString("D"),
            websiteUrl = value,
        }.PrepareSave().websiteUrl);
    }

    [Theory]
    [InlineData("example.org")]
    [InlineData("https://")]
    [InlineData("javascript:alert(1)")]
    [InlineData("file:///sdcard/test")]
    [InlineData("ftp://example.org")]
    [InlineData("https://example.org/\nother")]
    public void RejectsInvalidAddressesAndSafelyLoadsLegacyValues(string value)
    {
        Assert.Throws<FormatException>(() => CloudWebsite.Normalize(value));
        Assert.Empty(CloudWebsite.Load(value));
    }

    [Fact]
    public void RejectsOversizedAddressWithoutTruncation() =>
        Assert.Throws<FormatException>(() => CloudWebsite.Normalize("https://example.org/" + new string('a', 2048)));

    [Fact]
    public void OldFieldsRetainDefaultsAndWebsiteDraftDoesNotChangePhotonSettings()
    {
        // Unity serialization is covered by device acceptance; this checks the shared model's defaults and copies.
        var settings = JsonSerializer.Deserialize<CloudSettings>("{\"enabled\":true,\"playerName\":\"Quest Two\"}",
            new JsonSerializerOptions { IncludeFields = true });
        Assert.True(settings.enabled);
        Assert.Empty(settings.websiteUrl);
        Assert.Empty(settings.realtimeAppId);
        var draft = settings.Copy();
        draft.websiteUrl = CloudWebsite.Normalize("https://example.org/code");
        Assert.Empty(settings.websiteUrl);
        Assert.Equal(settings.playerName, draft.playerName);
        Assert.Equal(settings.useCustomAppIds, draft.useCustomAppIds);
        Assert.Equal(settings.realtimeAppId, draft.realtimeAppId);
        Assert.Equal(settings.voiceAppId, draft.voiceAppId);
        Assert.Equal(settings.enabled, draft.enabled);
    }
}
