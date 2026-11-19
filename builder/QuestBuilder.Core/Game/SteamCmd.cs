using System.Diagnostics;
using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;

namespace RepoQuest.Builder.Game;

/// <summary>What SteamCMD asks the player while it logs in (asked on the UI thread; null = the player cancelled)</summary>
public interface ISteamLoginPrompts
{
    /// <summary>The account's password (only when SteamCMD has no saved login for this account)</summary>
    Task<string?> AskPasswordAsync(string user);

    /// <summary>A Steam Guard code from the e-mail or the authenticator; <paramref name="wrongBefore"/> after a rejected one</summary>
    Task<string?> AskGuardCodeAsync(bool email, bool wrongBefore);

    /// <summary>Steam waits for the login to be approved in the Steam Mobile app</summary>
    void ShowMobileConfirmation();
}

/// <summary>
/// The game on a Mac: R.E.P.O. is a Windows game, Steam for Mac does not download it. SteamCMD, Valve's own command
/// line client (signed by Valve, run through Rosetta), downloads the Windows version for the player's account into the
/// workspace. The password goes to SteamCMD over its standard input and from there only to Steam; SteamCMD then keeps
/// its own login token (in ~/Library/Application Support/Steam), so later updates ask for nothing.
/// </summary>
public static partial class SteamCmd
{
    /// <summary>Valve's Developer ID team: SteamCMD must be signed by it</summary>
    private const string ValveTeam = "MXGJJ98X76";

    public static bool IsSupported => Platform.IsMac;

    public static string Folder(Workspace workspace) => Path.Combine(workspace.Tools, "steamcmd");

    /// <summary>Where the game goes (SteamCMD's force_install_dir; its manifest lands in steamapps/ inside)</summary>
    public static string GameFolder(Workspace workspace) => Path.Combine(workspace.Root, "game", "REPO");

    /// <summary>Downloads SteamCMD (pinned in kit.json), checks Valve's signature and that Rosetta can run it</summary>
    public static async Task<string> EnsureAsync(Workspace workspace, KitManifest kit, CancellationToken ct)
    {
        if (!IsSupported)
            throw new BuildException("SteamCMD is only used on a Mac");
        var folder = Folder(workspace);
        var script = Path.Combine(folder, "steamcmd.sh");
        if (!File.Exists(script))
        {
            var pinned = (kit.Downloads.SteamCmd ?? throw new BuildException("kit.json has no SteamCMD")).ForThisMachine();
            var archive = Path.Combine(workspace.Downloads, Path.GetFileName(new Uri(pinned.Url).AbsolutePath));
            await Downloader.DownloadAsync(pinned.Url, archive, pinned.Sha256, ct: ct);
            await Archive.ExtractAsync(archive, folder, ct);
        }
        await VerifySignatureAsync(Path.Combine(folder, "steamcmd"), ct);
        await EnsureRosettaAsync(ct);
        return script;
    }

    private static async Task VerifySignatureAsync(string binary, CancellationToken ct)
    {
        var requirement = $"=anchor apple generic and certificate leaf[subject.OU] = \"{ValveTeam}\"";
        var result = await ProcessRunner.RunAsync(new ProcessSpec
        {
            FileName = "/usr/bin/codesign",
            Arguments = ["--verify", "--strict", $"-R{requirement}", binary],
            LogPrefix = "[codesign] ",
        }, ct: ct);
        if (result.ExitCode != 0)
            throw new BuildException("SteamCMD is not signed by Valve; it was not started", result.TailText) { Code = "steamcmd-signature" };
    }

    /// <summary>SteamCMD is an Intel program: Apple Silicon runs it through Rosetta 2, which is not there by default</summary>
    private static async Task EnsureRosettaAsync(CancellationToken ct)
    {
        if (System.Runtime.InteropServices.RuntimeInformation.OSArchitecture != System.Runtime.InteropServices.Architecture.Arm64)
            return;
        var result = await ProcessRunner.RunAsync(new ProcessSpec
        {
            FileName = "/usr/bin/arch",
            Arguments = ["-x86_64", "/usr/bin/true"],
            LogPrefix = "[rosetta] ",
        }, ct: ct);
        if (result.ExitCode != 0)
            throw new BuildException("SteamCMD needs Rosetta. Open Terminal and run: softwareupdate --install-rosetta " +
                                     "--agree-to-license, then try again.") { Code = "rosetta-missing" };
    }

    /// <summary>
    /// Downloads (or updates) the Windows version of the game with SteamCMD into <see cref="GameFolder"/> and returns
    /// that folder. <paramref name="progress"/> gets (fraction or null, a short status line).
    /// </summary>
    public static async Task<string> DownloadGameAsync(Workspace workspace, KitManifest kit, string user,
        ISteamLoginPrompts prompts, IProgress<(double? Fraction, string Text)> progress, CancellationToken ct)
    {
        var script = await EnsureAsync(workspace, kit, ct);
        var target = GameFolder(workspace);
        Directory.CreateDirectory(target);

        var start = new ProcessStartInfo("/bin/bash")
        {
            RedirectStandardInput = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            WorkingDirectory = Path.GetDirectoryName(script)!,
            StandardOutputEncoding = Encoding.UTF8,
        };
        foreach (var argument in new[]
                 {
                     script,
                     "+@ShutdownOnFailedCommand", "1",
                     "+@NoPromptForPassword", "0",
                     "+@sSteamCmdForcePlatformType", "windows",
                     "+force_install_dir", target,
                     "+login", user,
                     "+app_update", GameInstall.SteamAppId.ToString(CultureInfo.InvariantCulture), "validate",
                     "+quit",
                 })
            start.ArgumentList.Add(argument);

        Log.Info($"SteamCMD: downloading the game for {user} into {target}");
        using var process = Process.Start(start) ?? throw new BuildException("Could not start SteamCMD");
        using var kill = ct.Register(() =>
        {
            try
            {
                process.Kill(entireProcessTree: true);
            }
            catch (InvalidOperationException)
            {
            }
        });
        _ = Task.Run(async () =>
        {
            while (await process.StandardError.ReadLineAsync(ct) is { } line)
                Log.Debug("[steamcmd] " + line);
        }, ct);

        var outcome = new SteamCmdOutcome();
        var reader = new SteamCmdReader(async prompt =>
        {
            string? answer = prompt switch
            {
                SteamCmdPrompt.Password => await prompts.AskPasswordAsync(user),
                SteamCmdPrompt.EmailCode => await prompts.AskGuardCodeAsync(email: true, outcome.CodeAsked++ > 0),
                SteamCmdPrompt.AuthenticatorCode => await prompts.AskGuardCodeAsync(email: false, outcome.CodeAsked++ > 0),
                _ => null,
            };
            if (answer is null)
            {
                outcome.Cancelled = true;
                process.Kill(entireProcessTree: true);
                return;
            }
            // The answer goes to SteamCMD only: never into the log
            await process.StandardInput.WriteLineAsync(answer.AsMemory(), ct);
            await process.StandardInput.FlushAsync(ct);
        }, line =>
        {
            Log.Debug("[steamcmd] " + line);
            outcome.Observe(line, prompts, progress);
        });

        var buffer = new char[4096];
        int read;
        while ((read = await process.StandardOutput.ReadAsync(buffer.AsMemory(), ct)) > 0)
            await reader.FeedAsync(buffer.AsSpan(0, read).ToString());
        await reader.FlushAsync();
        await process.WaitForExitAsync(CancellationToken.None);
        ct.ThrowIfCancellationRequested();
        if (outcome.Cancelled)
            throw new OperationCanceledException();
        if (outcome.Succeeded && GameInstall.IsValid(target))
        {
            Log.Info("SteamCMD: the game is downloaded");
            return target;
        }
        if (outcome.Error is { } error)
            throw error;
        if (!outcome.Succeeded || !GameInstall.IsValid(target))
            throw new BuildException("SteamCMD did not download the game", $"exit code {process.ExitCode}, last line: {outcome.LastLine}")
                { Code = "steamcmd-failed" };
        return target;
    }

    /// <summary>What SteamCMD said so far: progress, success and the errors it reports in its own words</summary>
    internal sealed class SteamCmdOutcome
    {
        public int CodeAsked;
        public bool Cancelled;
        public bool Succeeded;
        public BuildException? Error;
        public string? LastLine;

        public void Observe(string line, ISteamLoginPrompts prompts, IProgress<(double? Fraction, string Text)> progress)
        {
            LastLine = line;
            if (Error is null && ErrorFor(line) is { } error)
            {
                Error = error;
                return;
            }
            if (line.Contains("Steam Mobile app", StringComparison.OrdinalIgnoreCase))
            {
                prompts.ShowMobileConfirmation();
                progress.Report((null, "login-confirm"));
            }
            else if (line.StartsWith("Logging in user", StringComparison.Ordinal))
            {
                progress.Report((null, "login"));
            }
            else if (ProgressRegex().Match(line) is { Success: true } match)
            {
                var stage = match.Groups[1].Value.Trim();
                var fraction = double.Parse(match.Groups[2].Value, CultureInfo.InvariantCulture) / 100;
                progress.Report((fraction, stage.Contains("verif", StringComparison.OrdinalIgnoreCase) ? "verify" : "download"));
            }
            else if (line.Contains("fully installed", StringComparison.Ordinal) && line.StartsWith("Success!", StringComparison.Ordinal))
            {
                Succeeded = true;
            }
        }

        internal static BuildException? ErrorFor(string line)
        {
            if (line.Contains("Invalid Password", StringComparison.OrdinalIgnoreCase))
                return new BuildException("Steam refused the login: wrong account name or password") { Code = "steam-password" };
            if (line.Contains("Rate Limit", StringComparison.OrdinalIgnoreCase))
                return new BuildException("Steam blocks logins for a while after several attempts; try again later") { Code = "steam-rate-limit" };
            if (line.Contains("No subscription", StringComparison.OrdinalIgnoreCase))
                return new BuildException("This Steam account does not own R.E.P.O.") { Code = "steam-not-owned" };
            if (line.Contains("Two-factor", StringComparison.OrdinalIgnoreCase) && line.Contains("mismatch", StringComparison.OrdinalIgnoreCase))
                return null;  // a wrong code: SteamCMD asks again
            if (line.Contains("Not enough disk space", StringComparison.OrdinalIgnoreCase) || line.Contains("Disk space", StringComparison.Ordinal))
                return new BuildException("Not enough disk space for the game") { Code = "disk-space" };
            if (line.StartsWith("ERROR", StringComparison.Ordinal) || line.Contains("ERROR!", StringComparison.Ordinal) ||
                line.Contains("FAILED", StringComparison.Ordinal))
                return new BuildException($"SteamCMD: {line.Trim()}") { Code = "steamcmd-failed" };
            return null;
        }
    }

    // "Update state (0x61) downloading, progress: 45.12 (551023104 / 1221000000)"
    [GeneratedRegex(@"Update state \(0x[0-9a-fA-F]+\)\s*([^,]+),\s*progress:\s*([\d.]+)")]
    private static partial Regex ProgressRegex();
}

internal enum SteamCmdPrompt { Password, EmailCode, AuthenticatorCode }

/// <summary>
/// SteamCMD's output, split into lines, with its questions recognised as they come: they end without a line break
/// ("password: "), so a line-based reader would wait forever
/// </summary>
internal sealed class SteamCmdReader(Func<SteamCmdPrompt, Task> onPrompt, Action<string> onLine)
{
    private readonly StringBuilder pending = new();

    public async Task FeedAsync(string text)
    {
        foreach (var c in text)
        {
            if (c == '\n')
            {
                var line = pending.ToString().TrimEnd('\r');
                pending.Clear();
                if (line.Trim().Length > 0)
                    onLine(line);
                continue;
            }
            pending.Append(c);
            if (PromptIn(pending.ToString()) is { } prompt)
            {
                onLine(pending.ToString().Trim());
                pending.Clear();
                await onPrompt(prompt);
            }
        }
    }

    public Task FlushAsync()
    {
        if (pending.ToString().Trim().Length > 0)
            onLine(pending.ToString());
        pending.Clear();
        return Task.CompletedTask;
    }

    internal static SteamCmdPrompt? PromptIn(string text)
    {
        var trimmed = text.TrimEnd();
        if (!trimmed.EndsWith(':'))
            return null;
        if (trimmed.EndsWith("password:", StringComparison.OrdinalIgnoreCase))
            return SteamCmdPrompt.Password;
        // The authenticator in the Steam Mobile app: "Two-factor code:"; an e-mailed code: "Steam Guard code:"
        if (trimmed.EndsWith("Two-factor code:", StringComparison.OrdinalIgnoreCase))
            return SteamCmdPrompt.AuthenticatorCode;
        if (trimmed.EndsWith("Steam Guard code:", StringComparison.OrdinalIgnoreCase))
            return SteamCmdPrompt.EmailCode;
        return null;
    }
}
