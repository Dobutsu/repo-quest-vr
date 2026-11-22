using RepoQuest.Localization;
using Xunit;

public sealed class DialogLabelsTests
{
    [Theory]
    [InlineData("Yes", "ui.yes", "Да")]
    [InlineData("YES", "ui.yes", "Да")]
    [InlineData(" No ", "ui.no", "Нет")]
    [InlineData("NO", "ui.no", "Нет")]
    [InlineData("Да", "ui.yes", "Да")]
    [InlineData("Нет", "ui.no", "Нет")]
    public void StandardChoicesUseCurrentGameLocale(string source, string key, string russian)
    {
        Assert.Equal(key, DialogLabels.BinaryChoiceKey(source));
        Assert.Equal(russian, QuestStrings.Get(key, "ru-RU"));
        Assert.Equal(key == "ui.yes" ? "Yes" : "No", QuestStrings.Get(key, "en-US"));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("No games available")]
    [InlineData("Connect")]
    public void OtherActionLabelsAreNotRewritten(string label) => Assert.Null(DialogLabels.BinaryChoiceKey(label));
}
