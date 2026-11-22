using RepoQuest.Localization;
using System.Text.RegularExpressions;
using Xunit;

public sealed class LocalizationTests
{
    [Fact]
    public void TranslationsAreCompleteAndPreserveFormattingArguments()
    {
        foreach (var pair in QuestStrings.Entries.Values)
        {
            Assert.Equal(2, pair.Length);
            Assert.All(pair, text => Assert.False(string.IsNullOrWhiteSpace(text)));
            Assert.All(pair, text => Assert.DoesNotContain("???", text));
            var arguments = (string text) => Regex.Matches(text, @"\{\d+\}").Select(m => m.Value).Order().ToArray();
            Assert.Equal(arguments(pair[0]), arguments(pair[1]));
        }
    }

    [Fact]
    public void FollowsGameLocaleAndFallsBackToEnglish()
    {
        Assert.Equal("НАСТРОЙКИ", QuestStrings.Get("cloud.settings", "ru-RU"));
        Assert.Equal("НАСТРОЙКИ", QuestStrings.Get("cloud.settings", "RU"));
        Assert.Equal("SETTINGS", QuestStrings.Get("cloud.settings", "de-DE"));
        Assert.Equal("SETTINGS", QuestStrings.Get("cloud.settings", null));
        Assert.Equal("unknown.key", QuestStrings.Get("unknown.key", "ru-RU"));
        Assert.Equal("custom player input", QuestStrings.Literal("custom player input", "ru-RU"));
    }

    [Theory]
    [InlineData("Off")]
    [InlineData("Reduced")]
    [InlineData("All")]
    [InlineData("Hidden")]
    [InlineData("Alternate Eyes")]
    [InlineData("Both Eyes")]
    [InlineData("Snap")]
    [InlineData("Smooth")]
    [InlineData("Disabled")]
    public void TranslatesVRSettingEnumValues(string value) => Assert.NotEqual(value, QuestStrings.Literal(value, "ru-RU"));

    [Theory]
    [InlineData("TUTORIAL.INVENTORY_FILL.TEXT")]
    [InlineData("TUTORIAL.INVENTORY_FILL.DUMMY_TEXT")]
    [InlineData("TUTORIAL.INVENTORY_EMPTY.TEXT")]
    [InlineData("TUTORIAL.INVENTORY_EMPTY.DUMMY_TEXT")]
    [InlineData("TUTORIAL.MAP.TEXT")]
    [InlineData("TUTORIAL.MAP.DUMMY_TEXT")]
    [InlineData("TUTORIAL.EXPRESSIONS.TEXT")]
    public void RussianVRTutorialPreservesControllerTagsAndMarkup(string key)
    {
        var english = QuestStrings.Get(key, "en-US");
        var russian = QuestStrings.Get(key, "ru-RU");
        Assert.NotEqual(key, russian);
        Assert.NotEqual(english, russian);
        Assert.Matches("[А-Яа-я]", russian);
        var tags = (string text) => Regex.Matches(text, @"\[[A-Za-z0-9]+\]|</?\w+>")
            .Select(m => m.Value).Order().ToArray();
        Assert.Equal(tags(english), tags(russian));
        Assert.Equal(english, QuestStrings.Get(key, "fr-FR"));
    }

    [Theory]
    [InlineData("SETTINGS.GAMEPAD")]
    [InlineData("SETTINGS.CONTROLS.CHAT_CONFIRM")]
    [InlineData("SETTINGS.LANGUAGE.CUSTOM_TRANSLATIONS")]
    [InlineData("SETTINGS.GAMEPAD.ROTATION_SENSITIVITY")]
    [InlineData("setting.LampCount")]
    [InlineData("vr.openXRRuntime")]
    [InlineData("vr.Performance")]
    public void MissingGameAndVRMenuEntriesHaveRussianTranslations(string key)
    {
        Assert.NotEqual(key, QuestStrings.Get(key, "ru-RU"));
        Assert.Matches("[А-Яа-я]", QuestStrings.Get(key, "ru-RU"));
    }

    [Theory]
    [InlineData("Angry")]
    [InlineData("Crazy")]
    [InlineData("Eyes Closed")]
    [InlineData("Happy")]
    [InlineData("Sad")]
    [InlineData("Suspicious")]
    [InlineData("OpenXR Runtime")]
    [InlineData("Performance")]
    [InlineData("Enter")]
    [InlineData("Space")]
    [InlineData("Tab")]
    [InlineData("Enter text...")]
    [InlineData("Movement")]
    [InlineData("Jump")]
    [InlineData("Crouch")]
    [InlineData("Sprint")]
    [InlineData("Tumble")]
    [InlineData("Grabbing")]
    [InlineData("Grab")]
    [InlineData("Rotate")]
    [InlineData("Push")]
    [InlineData("Pull")]
    [InlineData("Use item")]
    [InlineData("Reset height")]
    [InlineData("Turn left")]
    [InlineData("Turn right")]
    [InlineData("Chat")]
    [InlineData("Menu")]
    [InlineData("Spectate Next")]
    [InlineData("Spectate Previous")]
    [InlineData("Push to talk")]
    [InlineData("Toggle mute")]
    [InlineData("Toggle")]
    [InlineData("<sprite=37> DISCONNECTED")]
    [InlineData("NOT BOUND")]
    public void BundleLabelsResolveInRussianAndKeepEnglish(string value)
    {
        Assert.NotEqual(value, QuestStrings.Literal(value, "ru-RU"));
        Assert.Equal(value, QuestStrings.Literal(value, "en-US"));
    }
}
