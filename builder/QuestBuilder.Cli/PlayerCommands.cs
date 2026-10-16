using RepoQuest.Builder.Pipeline;
using RepoQuest.Builder.Updates;

namespace RepoQuest.Builder.Cli;

/// <summary>
/// The players' flow without the window, through the same BuilderService the window drives: for testing the whole
/// path (embedded kit, consent, Unity, license, pipeline, install, self-update) on the Mac.
///   player-build [--workspace &lt;dir&gt;] [--kit &lt;dir&gt;] [--game &lt;dir&gt;] [--headset quest|pico] [--yes]
///   update [--update-source &lt;url|dir&gt;] [--workspace &lt;dir&gt;] [--then-build] [--yes]
///   install [--workspace &lt;dir&gt;] [--kit &lt;dir&gt;]
///   uninstall [--workspace &lt;dir&gt;] [--headset] [--files] [--defender]
/// </summary>
internal static class PlayerCommands
{
    public static async Task<int> BuildAsync(Options options)
    {
        var service = Start(options);
        if (options.Get("game") is { } game && service.SetGamePath(game) is { } problem)
            throw new BuildException($"{game}: {problem}");
        if (options.Has("headset"))
            service.SetHeadset(BuildOptions.ParseHeadset(options.Get("headset")));
        using var cancel = CancelOnCtrlC();
        var apk = await service.BuildAsync(cancel.Token);
        Console.WriteLine($"APK: {apk}");
        return 0;
    }

    public static async Task<int> UpdateAsync(Options options, string[] rawArgs)
    {
        var service = Start(options);
        var source = options.Get("update-source");
        var manifest = await service.CheckForUpdateAsync(source, CancellationToken.None);
        if (manifest is null)
        {
            Console.WriteLine($"no newer version than {Platform.BuilderVersion}");
            return 0;
        }
        Console.WriteLine($"update {Platform.BuilderVersion} -> {manifest.Version}: {manifest.NotesFor("en")}");
        // The new version starts with the same workspace and, with --then-build, rebuilds right away
        var restart = new List<string>();
        if (options.Has("then-build"))
        {
            restart.Add("player-build");
            if (options.Get("workspace") is { } workspace)
                restart.AddRange(["--workspace", workspace]);
            if (options.Has("yes"))
                restart.Add("--yes");
        }
        else
        {
            restart.Add("locate");
        }
        await service.ApplyUpdateAsync(manifest, source, null, restart, CancellationToken.None);
        Console.WriteLine("updated; the new version was started");
        return 0;
    }

    public static async Task<int> InstallAsync(Options options)
    {
        var service = Start(options);
        var apk = service.GetStatus().ApkPath ?? throw new BuildException("no APK built yet");
        var devices = await service.FindDevicesAsync(CancellationToken.None);
        var device = devices.FirstOrDefault(d => d.Ready && (d.IsQuest || d.IsPico)) ?? devices.FirstOrDefault()
                     ?? throw new BuildException("no headset connected (adb devices is empty)");
        var outcome = await service.InstallAsync(device, apk, new Progress<string>(s => Console.WriteLine($"[install] {s}")),
            CancellationToken.None);
        Console.WriteLine($"installed {Path.GetFileName(apk)} on {device.Model ?? device.Serial}: {outcome}");
        return 0;
    }

    /// <summary>The window's "Uninstall…": the game from the headset (saves to the desktop first), the builder's files
    /// in the workspace, the Defender exclusion; only what is asked for</summary>
    public static async Task<int> UninstallAsync(Options options)
    {
        var service = Start(options);
        if (options.Has("headset"))
        {
            var devices = await service.FindDevicesAsync(CancellationToken.None);
            var device = devices.FirstOrDefault(d => d.Ready && (d.IsQuest || d.IsPico)) ?? devices.FirstOrDefault()
                         ?? throw new BuildException("no headset connected (adb devices is empty)");
            var saves = await service.UninstallFromHeadsetAsync(device, new Progress<string>(s => Console.WriteLine($"[uninstall] {s}")),
                CancellationToken.None);
            Console.WriteLine($"removed from {device.Model ?? device.Serial}; saves: {saves ?? "none"}");
        }
        if (options.Has("files") || options.Has("defender"))
        {
            var result = await service.RemoveFromComputerAsync(options.Has("files"), options.Has("defender"), CancellationToken.None);
            Console.WriteLine($"removed; kept: {result.KeptBackups ?? "nothing"}; failed: {(result.Failed.Count == 0 ? "nothing" : string.Join(", ", result.Failed))}");
        }
        return 0;
    }

    private static BuilderService Start(Options options)
    {
        var settings = AppSettings.Load();
        if (options.Get("workspace") is { } workspace)
            settings.WorkspacePath = Path.GetFullPath(workspace);
        var service = new BuilderService(settings, new ConsolePrompts(options.Has("yes")), new ConsoleProgress());
        service.Initialize(options.Get("kit"));
        var status = service.GetStatus();
        Console.WriteLine($"builder {Platform.BuilderVersion}, kit {service.Kit.Version}");
        Console.WriteLine($"game:      {status.GamePath ?? "not found"} ({status.GameVersion ?? "?"}{(status.GameTested ? "" : ", untested")})");
        Console.WriteLine($"workspace: {status.WorkspacePath}, {status.FreeBytes >> 30} GB free, {status.NeededBytes >> 30} GB needed");
        Console.WriteLine($"unity:     {(status.UnityReady ? "ready" : "missing")}, license {(status.UnityLicensed ? "found" : "missing")}");
        Console.WriteLine($"pending:   {string.Join(", ", status.PendingSteps)}");
        return service;
    }

    private static CancellationTokenSource CancelOnCtrlC()
    {
        var cancel = new CancellationTokenSource();
        Console.CancelKeyPress += (_, e) =>
        {
            e.Cancel = true;
            cancel.Cancel();
        };
        return cancel;
    }

    /// <summary>The window's questions as console prompts; --yes answers them for unattended tests</summary>
    private sealed class ConsolePrompts(bool yes) : IBuilderPrompts
    {
        public Task<bool> AcceptDownloadsAsync(IReadOnlyList<ConsentItem> items)
        {
            Console.WriteLine("The build downloads and installs:");
            foreach (var item in items)
                Console.WriteLine($"  {item.Id} {item.Version} {(item.DownloadBytes > 0 ? $"~{item.DownloadBytes >> 20} MB" : "")} {item.LicenseUrl}");
            return Task.FromResult(Ask("Accept these licenses?"));
        }

        public Task ShowOnlineNoticeAsync()
        {
            Console.WriteLine("Online play: log in to Steam in the headset; it is an unofficial Steam client (risk for the account).");
            return Task.CompletedTask;
        }

        public Task<bool> AskDefenderExclusionAsync(string workspace) =>
            Task.FromResult(Ask($"Exclude {workspace} from Windows Defender scanning to build faster (administrator rights)?"));

        public Task<LicenseChoice> AskForUnityLicenseAsync(bool afterFailure)
        {
            Console.WriteLine("Unity needs a license: sign in to Unity Hub and add a free Personal license.");
            return Task.FromResult(Ask("Check again?") ? LicenseChoice.CheckAgain : LicenseChoice.Cancel);
        }

        public Task<bool> ConfirmUntestedGameAsync(string? gameVersion) =>
            Task.FromResult(Ask($"Game version {gameVersion ?? "?"} was not tested with this builder. Build anyway?"));

        private bool Ask(string question)
        {
            if (yes)
            {
                Console.WriteLine($"{question} yes (--yes)");
                return true;
            }
            Console.Write($"{question} [y/N] ");
            return Console.ReadLine()?.Trim().ToLowerInvariant() is "y" or "yes" or "д" or "да";
        }
    }
}
