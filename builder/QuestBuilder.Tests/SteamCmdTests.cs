using RepoQuest.Builder.Game;

namespace RepoQuest.Builder.Tests;

public sealed class SteamCmdTests
{
    [Theory]
    [InlineData("password: ", "Password")]
    [InlineData("Two-factor code:", "AuthenticatorCode")]
    [InlineData("Steam Guard code:", "EmailCode")]
    [InlineData("Logging in user 'x' [U:1:0] to Steam Public...", null)]
    [InlineData("Steam>", null)]
    public void PromptsAreRecognised(string text, string? expected) =>
        Assert.Equal(expected, SteamCmdReader.PromptIn(text)?.ToString());

    [Fact]
    public async Task PromptsWithoutALineBreakAreAnsweredAndLinesSplit()
    {
        var prompts = new List<string>();
        var lines = new List<string>();
        var reader = new SteamCmdReader(p => { prompts.Add(p.ToString()); return Task.CompletedTask; }, lines.Add);
        await reader.FeedAsync("Cached credentials not found.\n\npass");
        await reader.FeedAsync("word: ");
        await reader.FeedAsync("\nProceeding with login\r\nUpdate state (0x61) downloading, progress: 12.50 (1 / 8)\n");
        await reader.FlushAsync();
        Assert.Equal(["Password"], prompts);
        Assert.Equal(["Cached credentials not found.", "password:", "Proceeding with login",
            "Update state (0x61) downloading, progress: 12.50 (1 / 8)"], lines);
    }

    [Theory]
    [InlineData("Logging in user 'zz' [U:1:0] to Steam Public...ERROR (Invalid Password)", "steam-password")]
    [InlineData("ERROR! Failed to install app '3241660' (No subscription)", "steam-not-owned")]
    [InlineData("Logging in user 'zz' to Steam Public...ERROR (Rate Limit Exceeded)", "steam-rate-limit")]
    [InlineData("ERROR! Failed to install app '3241660' (Disk write failure)", "steamcmd-failed")]
    [InlineData("Success! App '3241660' fully installed.", null)]
    [InlineData("Waiting for user info...OK", null)]
    public void ErrorsAreExplained(string line, string? code) =>
        Assert.Equal(code, SteamCmd.SteamCmdOutcome.ErrorFor(line)?.Code);

    [Fact]
    public void ProgressAndSuccessAreSeen()
    {
        var reports = new List<(double? Fraction, string Text)>();
        var outcome = new SteamCmd.SteamCmdOutcome();
        var progress = new SyncProgress(reports.Add);
        outcome.Observe(" Update state (0x61) downloading, progress: 45.12 (551023104 / 1221000000)", new NoPrompts(), progress);
        outcome.Observe(" Update state (0x81) verifying update, progress: 99.00 (1 / 1)", new NoPrompts(), progress);
        outcome.Observe("Success! App '3241660' fully installed.", new NoPrompts(), progress);
        Assert.Equal("download", reports[0].Text);
        Assert.Equal(0.4512, reports[0].Fraction!.Value, 4);
        Assert.Equal("verify", reports[1].Text);
        Assert.True(outcome.Succeeded);
        Assert.Null(outcome.Error);

        var failed = new SteamCmd.SteamCmdOutcome();
        failed.Observe("Logging in user 'zz' [U:1:0] to Steam Public...ERROR (Invalid Password)", new NoPrompts(), progress);
        Assert.Equal("steam-password", failed.Error?.Code);
    }

    [Fact]
    public void FreeSpace_IsMeasuredOnTheVolumeOfThePath()
    {
        if (OperatingSystem.IsWindows())
            return;
        var other = DriveInfo.GetDrives().FirstOrDefault(d => d.RootDirectory.FullName.StartsWith("/Volumes/", StringComparison.Ordinal));
        Assert.Equal("/", RepoQuest.Builder.FileOps.VolumeOf("/usr/bin"));
        if (other is not null)
            Assert.Equal(other.RootDirectory.FullName, RepoQuest.Builder.FileOps.VolumeOf(Path.Combine(other.RootDirectory.FullName, "a", "b")));
    }

    private sealed class SyncProgress(Action<(double?, string)> report) : IProgress<(double? Fraction, string Text)>
    {
        public void Report((double? Fraction, string Text) value) => report(value);
    }

    private sealed class NoPrompts : ISteamLoginPrompts
    {
        public Task<string?> AskPasswordAsync(string user) => Task.FromResult<string?>(null);
        public Task<string?> AskGuardCodeAsync(bool email, bool wrongBefore) => Task.FromResult<string?>(null);
        public void ShowMobileConfirmation() { }
    }
}
