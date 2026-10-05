using System.ComponentModel;
using System.Diagnostics;
using System.Globalization;
using System.Text.RegularExpressions;

namespace RepoQuest.Builder.Setup;

/// <summary>
/// Installs Unity Hub and the exact editor the project needs (2022.3.62f3 with Android Build Support, its SDK/NDK and
/// OpenJDK) on Windows, through the Hub's headless command line. The player has accepted the licenses in the builder's
/// consent dialog before this runs.
/// </summary>
public static partial class UnitySetup
{
    private const string HubFeed = "https://public-cdn.cloud.unity3d.com/hub/prod/";

    /// <summary>Space the editor with Android support takes (measured: ~15 GB) plus the download</summary>
    public const long EditorBytes = 20L << 30;

    public static async Task<UnityInstall> EnsureAsync(Workspace workspace, UnityVersion version,
        IProgress<(string Stage, double? Fraction, string? Detail)> progress, CancellationToken ct)
    {
        var existing = UnityInstall.Find(version.Version);
        if (existing is { HasAndroid: true })
            return existing;
        if (!Platform.IsWindows)
            throw new BuildException($"Install Unity {version.Version} with Android Build Support (SDK, NDK, OpenJDK) " +
                                     "in Unity Hub") { Code = "unity-missing" };

        var hub = UnityHub.Executable();
        if (hub is null)
        {
            progress.Report(("hub-download", null, null));
            hub = await InstallHubAsync(workspace, progress, ct);
        }

        // The Hub installs editors under Program Files on the system drive; if that is too full, onto the
        // workspace's drive (the Hub's install path is put back afterwards)
        string? previousPath = null;
        var defaultRoot = UnityInstall.DefaultRoots(version.Version).First();
        if (existing is null && FreeBytes(defaultRoot) < EditorBytes)
        {
            previousPath = (await HubAsync(hub, ["install-path", "-g"], null, ct)).TailText.Trim();
            var target = Path.Combine(Path.GetPathRoot(workspace.Root)!, "Unity", "Hub", "Editor");
            Directory.CreateDirectory(target);
            await HubAsync(hub, ["install-path", "-s", target], null, ct);
        }

        ProcessResult result;
        try
        {
            progress.Report(("editor", null, null));
            // "install" also adds the modules to an editor the Hub knows; for one installed elsewhere, install-modules
            string[] arguments = existing is null
                ? ["install", "--version", version.Version, "--changeset", version.Changeset, "--module", .. AndroidModules]
                : ["install-modules", "--version", version.Version, "--module", .. AndroidModules];
            // The exit code is not the answer: with every module already there the Hub fails ("No modules found to
            // install"), so what counts is what is installed afterwards
            result = await ProcessRunner.RunAsync(HubSpec(hub, arguments), line => ReportHubLine(line, progress), ct);
        }
        finally
        {
            if (previousPath is { Length: > 0 })
                await HubAsync(hub, ["install-path", "-s", previousPath], null, CancellationToken.None);
        }

        var installed = UnityInstall.Find(version.Version);
        if (installed is null || !installed.HasAndroid)
            throw new BuildException($"Unity Hub did not install Unity {version.Version} with Android support. " +
                                     "Install it in Unity Hub (Installs → Install Editor → Archive) and try again.",
                    $"Hub exit code {result.ExitCode}\n{result.TailText}")
                { Code = "unity-install-failed" };
        return installed;
    }

    /// <summary>Downloads the Hub installer named in the Hub's update feed (latest.yml: version, sha512), checks it and
    /// runs it silently; Windows asks for administrator rights (UAC)</summary>
    private static async Task<string> InstallHubAsync(Workspace workspace,
        IProgress<(string Stage, double? Fraction, string? Detail)> progress, CancellationToken ct)
    {
        var feed = await Downloader.GetStringAsync(HubFeed + "latest.yml", ct);
        var entry = HubInstallerRegex().Match(feed);
        if (!entry.Success)
            throw new BuildException("Could not read Unity Hub's download feed", feed) { Code = "hub-feed" };
        var file = entry.Groups[1].Value;
        var sha512 = entry.Groups[2].Value;
        var installer = Path.Combine(workspace.Downloads, Path.GetFileName(file));
        await Downloader.DownloadAsync(HubFeed + file, installer, null,
            new Progress<DownloadProgress>(p => progress.Report(("hub-download",
                p.Total is { } total ? p.Done / (double)total : null, $"{p.Done >> 20}/{(p.Total ?? 0) >> 20} MB"))), ct);
        if (Hashing.Sha512Base64File(installer) != sha512)
        {
            File.Delete(installer);
            throw new BuildException("The downloaded Unity Hub installer is damaged; try again") { Code = "hub-hash" };
        }
        await VerifyPublisherAsync(installer, ct);

        progress.Report(("hub-install", null, null));
        var setup = new ProcessStartInfo(installer, "/S") { UseShellExecute = true, Verb = "runas" };
        Process? process;
        try
        {
            process = Process.Start(setup);
        }
        catch (Win32Exception e) when (e.NativeErrorCode == 1223)
        {
            throw new BuildException("Installing Unity Hub needs administrator rights (the Windows prompt was declined)")
                { Code = "hub-uac" };
        }
        if (process is null)
            throw new BuildException("Could not start the Unity Hub installer") { Code = "hub-install" };
        await process.WaitForExitAsync(ct);

        return UnityHub.Executable()
               ?? throw new BuildException("Unity Hub was not installed", $"installer exit code {process.ExitCode}")
                   { Code = "hub-install" };
    }

    /// <summary>The installer must be signed by Unity Technologies (Windows Authenticode)</summary>
    private static async Task VerifyPublisherAsync(string file, CancellationToken ct)
    {
        var script = "$s = Get-AuthenticodeSignature -LiteralPath $env:REPOQUEST_FILE; " +
                     "\"$($s.Status)|$($s.SignerCertificate.Subject)\"";
        var result = await ProcessRunner.RunAsync(new ProcessSpec
        {
            FileName = "powershell.exe",
            Arguments = ["-NoProfile", "-NonInteractive", "-Command", script],
            Environment = new Dictionary<string, string?> { ["REPOQUEST_FILE"] = file },
            LogPrefix = "[signature] ",
        }, ct: ct);
        var answer = result.Tail.LastOrDefault(l => l.Contains('|')) ?? "";
        if (!answer.StartsWith("Valid|", StringComparison.Ordinal) || !answer.Contains("Unity Technologies", StringComparison.Ordinal))
        {
            File.Delete(file);
            throw new BuildException("The downloaded Unity Hub installer is not signed by Unity", answer) { Code = "hub-signature" };
        }
    }

    /// <summary>
    /// Android Build Support, its SDK and NDK, and OpenJDK. Newer Hubs name the JDK module with its version
    /// ("Couldn't find module "android-open-jdk". Did you mean: android-open-jdk-11.0.14.1+1"); the Hub skips the
    /// name it does not know and installs the rest, so both are asked for.
    /// </summary>
    private static readonly string[] AndroidModules =
        ["android", "android-sdk-ndk-tools", "android-open-jdk", "android-open-jdk-11.0.14.1+1"];

    private static Task<ProcessResult> HubAsync(string hub, string[] arguments, Action<string>? onLine, CancellationToken ct) =>
        ProcessRunner.RunCheckedAsync(HubSpec(hub, arguments), "Unity Hub failed", onLine, ct);

    private static ProcessSpec HubSpec(string hub, string[] arguments) => new()
    {
        FileName = hub,
        Arguments = ["--", "--headless", .. arguments],
        LogPrefix = "[hub] ",
    };

    /// <summary>The Hub's headless output: download/install lines with percentages where it prints them ("0.00%", with a
    /// point whatever the system's language)</summary>
    private static void ReportHubLine(string line, IProgress<(string Stage, double? Fraction, string? Detail)> progress)
    {
        var percent = PercentRegex().Match(line);
        var stage = line.Contains("nstall", StringComparison.Ordinal) ? "editor-install" : "editor";
        progress.Report((stage, percent.Success ? double.Parse(percent.Groups[1].Value, CultureInfo.InvariantCulture) / 100 : null,
            line.Length > 80 ? line[..80] : line));
    }

    private static long FreeBytes(string path)
    {
        try
        {
            return FileOps.FreeSpace(path);
        }
        catch (Exception)
        {
            return long.MaxValue;
        }
    }

    // latest.yml: "- url: 3.22.2/UnityHubSetup-3.22.2-x64.exe\n    sha512: <base64>"
    [GeneratedRegex(@"url:\s*(\S*UnityHubSetup-[\d.]+-x64\.exe)\s*\n\s*sha512:\s*(\S+)")]
    private static partial Regex HubInstallerRegex();

    [GeneratedRegex(@"(\d{1,3}(?:\.\d+)?)\s*%")]
    private static partial Regex PercentRegex();
}
