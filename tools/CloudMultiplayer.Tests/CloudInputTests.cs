using RepoQuest.Cloud;
using Xunit;

public sealed class CloudInputTests
{
    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("  \r\n")]
    [InlineData("first\nsecond")]
    [InlineData("code\0injected")]
    public void RejectsEmptyAndMultilineClipboard(string value) =>
        Assert.Throws<FormatException>(() => CloudInput.Clipboard(value, 1024));

    [Fact]
    public void TrimsWrappingWhitespaceButNeverTruncatesPayload()
    {
        var appId = Guid.NewGuid().ToString("D");
        Assert.Equal(appId, CloudInput.Clipboard(" \n" + appId + "\r\n", 36));
        Assert.Equal(new string('a', 1024), CloudInput.Clipboard(new string('a', 1024), 1024));
        Assert.Throws<FormatException>(() => CloudInput.Clipboard(new string('a', 1025), 1024));
        Assert.Throws<FormatException>(() => CloudInput.Clipboard(new string('a', 37), 36));
    }
}
