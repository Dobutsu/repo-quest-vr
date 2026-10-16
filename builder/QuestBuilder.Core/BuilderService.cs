using RepoQuest.Builder.Game;
using RepoQuest.Builder.Pipeline;
using RepoQuest.Builder.Quest;
using RepoQuest.Builder.Setup;
using RepoQuest.Builder.Updates;

namespace RepoQuest.Builder;

/// <summary>Something the consent dialog lists (the UI names and explains it by <see cref="Id"/>)</summary>
/// <param name="Id">unity-hub, unity-editor, assetripper, python, long-paths, java, ovrport</param>
public sealed record ConsentItem(string Id, string? Version, long DownloadBytes, string? LicenseUrl);

public enum LicenseChoice { CheckAgain, Cancel }

/// <summary>What removing from the computer did: the folder of headset backups it kept, what it could not delete</summary>
public sealed record RemovalResult(string? KeptBackups, IReadOnlyList<string> Failed);

/// <summary>Questions only the player can answer; the window shows them as dialogs</summary>
public interface IBuilderPrompts
{
    /// <summary>What will be downloaded and installed, with links to the licenses; true when the player accepts</summary>
    Task<bool> AcceptDownloadsAsync(IReadOnlyList<ConsentItem> items);

    /// <summary>Once: playing online means logging in to Steam in the headset through an unofficial client</summary>
    Task ShowOnlineNoticeAsync();

    /// <summary>Once, on Windows: exclude <paramref name="workspace"/> from Defender's scanning to build faster?</summary>
    Task<bool> AskDefenderExclusionAsync(string workspace);

    /// <summary>Unity has no license: sign in to Unity Hub and add a free Personal license, then check again</summary>
    Task<LicenseChoice> AskForUnityLicenseAsync(bool afterFailure);

    /// <summary>The game is a version this builder was not tested with: build anyway?</summary>
    Task<bool> ConfirmUntestedGameAsync(string? gameVersion);
}

/// <summary>What the window shows before and between builds</summary>
public sealed record BuilderStatus(
    string? GamePath,
    string? GameVersion,
    bool GameTested,
    string WorkspacePath,
    long FreeBytes,
    long NeededBytes,
    bool UnityReady,
    bool UnityLicensed,
    IReadOnlyList<string> PendingSteps,
    string? ApkPath,
    string? ApkVersion,
    bool GameChangedSinceApk,
    bool BuilderChangedSinceApk);

/// <summary>
/// The player's builder: finds the game, chooses the workspace, installs what the build needs (with the player's
/// consent), runs the pipeline, installs the APK on the headset, updates itself. The window is a thin layer over it.
/// </summary>
public sealed class BuilderService
{
    /// <summary>Steps as the window lists them: Unity installation, Python, then the pipeline's steps</summary>
    public static IReadOnlyList<string> StepsFor(Headset headset) => headset == Headset.Pico
        ? ["unity-setup", "setup", "export", "bake", "project", "unity", "pico"]
        : ["unity-setup", "setup", "export", "bake", "project", "unity"];

    /// <summary>A first build: export ~2.5 GB, project with Unity's Library ~30 GB, APKs and tools</summary>
    private const long FirstBuildBytes = 40L << 30;
    private const long RebuildBytes = 6L << 30;

    private readonly AppSettings settings;
    private readonly IBuilderPrompts prompts;
    private readonly IBuildProgress progress;

    public BuilderService(AppSettings settings, IBuilderPrompts prompts, IBuildProgress progress)
    {
        this.settings = settings;
        this.prompts = prompts;
        this.progress = progress;
    }

    public Kit Kit { get; private set; } = null!;
    public Workspace Workspace { get; private set; } = null!;
    public BuildState State { get; private set; } = null!;
    public GameInstall? Game { get; private set; }
    public UnityInstall? Unity { get; private set; }

    /// <param name="kitDir">A kit folder (development); null: the kit inside the builder</param>
    public void Initialize(string? kitDir = null)
    {
        Directory.CreateDirectory(AppSettings.SettingsDir);
        Log.OpenFile(Path.Combine(AppSettings.SettingsDir, "builder.log"));
        SelfUpdater.CleanUpPrevious();
        OpenWorkspace(settings.WorkspacePath ?? AppSettings.DefaultWorkspace());
        Kit = kitDir is not null ? Kit.Load(kitDir) : Kit.ExtractEmbedded(Workspace);
        Unity = UnityInstall.Find(Kit.Manifest.Unity.Version);

        var gamePath = GameInstall.IsValid(State.GamePath) ? State.GamePath : GameLocator.Find();
        Game = gamePath is not null ? new GameInstall(gamePath) : null;
        Log.Info($"kit {Kit.Version}, workspace {Workspace.Root}, game {Game?.Path ?? "not found"}, unity {Unity?.Root ?? "not found"}");
    }

    /// <summary>The folder the player picked as the game; returns a problem code (see GameInstall.Problem) or null</summary>
    public string? SetGamePath(string path)
    {
        var problem = GameInstall.Problem(path);
        if (problem is not null && problem.StartsWith("folder-parent:", StringComparison.Ordinal))
        {
            path = problem["folder-parent:".Length..];  // the player picked a folder above the game: take the game
            problem = null;
        }
        if (problem is not null)
            return problem;
        Game = new GameInstall(path);
        State.GamePath = Game.Path;
        State.Save(Workspace.StatePath);
        return null;
    }

    /// <summary>Moves the builder to another workspace (the old one stays as it is); a problem code or null</summary>
    public string? SetWorkspace(string path)
    {
        var problem = AppSettings.WorkspaceProblem(path, Game?.Path);
        if (problem is not null)
            return problem;
        var game = Game?.Path;
        OpenWorkspace(path);
        if (game is not null && State.GamePath is null)
        {
            State.GamePath = game;
            State.Save(Workspace.StatePath);
        }
        Kit = Kit.HasEmbedded ? Kit.ExtractEmbedded(Workspace) : Kit;
        return null;
    }

    private void OpenWorkspace(string path)
    {
        Workspace = new Workspace(path);
        Workspace.Create();
        State = BuildState.Load(Workspace.StatePath);
        settings.WorkspacePath = Workspace.Root;
        settings.Save();
    }

    /// <summary>The headset the builder builds for (kept in the workspace's state; Quest unless the player chose Pico)</summary>
    public Headset Headset => BuildOptions.ParseHeadset(State.Headset);

    public void SetHeadset(Headset headset)
    {
        State.Headset = headset == Headset.Quest ? null : BuildOptions.HeadsetId(headset);
        State.Save(Workspace.StatePath);
    }

    public BuildOptions Options => new()
    {
        ApkName = $"REPO-Quest-{Kit.Version}",
        VersionName = Kit.Version,
        VersionCode = BuildOptions.VersionCodeOf(Kit.Version),
        Headset = Headset,
    };

    /// <summary>The APK for the chosen headset</summary>
    public string ApkPath => Options.HeadsetApkPath(Workspace);

    public BuilderStatus GetStatus()
    {
        Unity = UnityInstall.Find(Kit.Manifest.Unity.Version) ?? Unity;
        var pending = Game is null ? BuildPipeline.Steps.Select(s => s.Id).ToList() : PendingSteps(Game);
        var freshProject = !Directory.Exists(Workspace.Library);
        var needed = (freshProject ? FirstBuildBytes : RebuildBytes);
        // An APK of an earlier version still installs, but only one for the chosen headset
        var lastFits = (State.LastApkHeadset ?? "quest") == BuildOptions.HeadsetId(Headset);
        var apk = File.Exists(ApkPath) ? ApkPath : lastFits && State.LastApk is { } last && File.Exists(last) ? last : null;
        return new BuilderStatus(
            Game?.Path,
            Game?.Version(),
            Game is not null && IsTested(Game),
            Workspace.Root,
            SafeFreeSpace(Workspace.Root),
            needed,
            Unity is { HasAndroid: true },
            UnityLicense.LooksLicensed(),
            pending,
            apk,
            apk is null ? null : State.LastApkVersion,
            apk is not null && Game is not null && State.LastApkGame is not null && State.LastApkGame != Game.AssemblyHash(),
            apk is not null && State.LastApkVersion != Kit.Version);
    }

    private IReadOnlyList<string> PendingSteps(GameInstall game)
    {
        try
        {
            return BuildPipeline.Pending(StatusContext(game));
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            Log.Debug($"pending steps: {e.Message}");
            return BuildPipeline.Steps.Select(s => s.Id).ToList();
        }
    }

    /// <summary>A context good for computing stamps (no Python: nothing runs)</summary>
    private BuildContext StatusContext(GameInstall game) => new()
    {
        Workspace = Workspace,
        Kit = Kit,
        Game = game,
        Unity = Unity,
        Python = null!,
        Tools = ToolLauncher.ForThisProcess(),
        ThirdParty = new ThirdParty(Workspace, Kit),
        State = State,
        Options = Options,
        Progress = NullProgress.Instance,
    };

    private bool IsTested(GameInstall game) =>
        Kit.Manifest.TestedGames.Any(t => t.AssemblyCSharp == game.AssemblyHash());

    /// <summary>
    /// The whole build: consent, Unity (installed alongside the export and bake), the license, the pipeline.
    /// Returns the APK. Throws <see cref="BuildException"/> with a code the window explains, or
    /// <see cref="OperationCanceledException"/> when the player cancels.
    /// </summary>
    public async Task<string> BuildAsync(CancellationToken ct)
    {
        var game = Game ?? throw new BuildException("Choose the game's folder first") { Code = "game-missing" };
        if (!GameInstall.IsValid(game.Path))
            throw new BuildException("The game's folder no longer contains the game") { Code = "game-missing" };

        var status = GetStatus();
        var unityNeeded = !status.UnityReady;
        var needed = status.NeededBytes;
        if (status.FreeBytes < needed)
            throw new BuildException($"Not enough disk space: {needed >> 30} GB needed, {status.FreeBytes >> 30} GB free")
                { Code = "disk-space" };
        if (unityNeeded && Platform.IsWindows)
        {
            var systemFree = SafeFreeSpace(UnityInstall.DefaultRoots(Kit.Manifest.Unity.Version).First());
            var workspaceFree = status.FreeBytes - needed;
            if (systemFree < UnitySetup.EditorBytes && workspaceFree < UnitySetup.EditorBytes)
                throw new BuildException("Not enough disk space for Unity (about 20 GB)") { Code = "disk-space-unity" };
        }

        await AskConsentAsync(unityNeeded, ct);
        if (!State.OnlineNoticeShown)
        {
            await prompts.ShowOnlineNoticeAsync();
            State.OnlineNoticeShown = true;
            State.Save(Workspace.StatePath);
        }
        if (Platform.IsWindows && !State.DefenderAsked)
            await AskDefenderExclusionAsync(ct);
        if (!status.GameTested && !await prompts.ConfirmUntestedGameAsync(status.GameVersion))
            throw new OperationCanceledException();

        using var awake = SleepBlocker.Start();
        // Unity installs (Hub CLI) while AssetRipper exports and the patches are baked
        using var unityCancel = CancellationTokenSource.CreateLinkedTokenSource(ct);
        var unityTask = EnsureUnityAsync(unityCancel.Token);
        BuildContext context;
        try
        {
            context = await BuildSession.PrepareAsync(Workspace, Kit, game, null, Options, State, progress, ct);
            await BuildPipeline.RunAsync(context, ct, until: "bake");
        }
        catch
        {
            // Never leave the Unity installation running behind a failed build
            await unityCancel.CancelAsync();
            try
            {
                await unityTask;
            }
            catch (Exception e)
            {
                Log.Debug($"unity setup stopped: {e.Message}");
            }
            throw;
        }
        if (!unityTask.IsCompleted)
            Log.Info("waiting for the Unity installation");
        context.Unity = Unity = await unityTask;

        await EnsureLicenseAsync(afterFailure: false);
        try
        {
            await BuildPipeline.RunAsync(context, ct);
        }
        catch (BuildException e) when (e.Code == "unity-license")
        {
            await EnsureLicenseAsync(afterFailure: true);
            await BuildPipeline.RunAsync(context, ct);
        }
        return Options.HeadsetApkPath(Workspace);
    }

    private async Task AskConsentAsync(bool unityNeeded, CancellationToken ct)
    {
        var items = new List<ConsentItem>();
        if (unityNeeded && Platform.IsWindows)
        {
            if (UnityHub.Executable() is null)
                items.Add(new ConsentItem("unity-hub", null, 200L << 20, "https://unity.com/legal/terms-of-service"));
            items.Add(new ConsentItem("unity-editor", Kit.Manifest.Unity.Version, 6L << 30, "https://unity.com/legal/terms-of-service"));
            items.Add(new ConsentItem("android", null, 0, "https://developer.android.com/studio/terms"));
        }
        if (!State.DownloadsAccepted)
        {
            items.Add(new ConsentItem("assetripper", Kit.Manifest.Downloads.AssetRipper.Version, 45L << 20,
                "https://github.com/AssetRipper/AssetRipper/blob/master/License.md"));
            items.Add(new ConsentItem("python", Kit.Manifest.Python, 150L << 20, "https://docs.python.org/3/license.html"));
        }
        var pico = Headset == Headset.Pico && !State.PicoDownloadsAccepted;
        if (pico && Kit.Manifest.Downloads is { Java: { } java, Ovrport: { } ovrport })
        {
            items.Add(new ConsentItem("java", java.Version, 45L << 20, "https://openjdk.org/legal/gplv2+ce.html"));
            items.Add(new ConsentItem("ovrport", ovrport.Version, 25L << 20, "https://github.com/ovrport/app/blob/main/LICENSE"));
        }
        if (Platform.IsWindows && !WindowsLongPaths.IsEnabled())
            items.Add(new ConsentItem("long-paths", null, 0, null));
        if (items.Count == 0)
            return;

        if (!await prompts.AcceptDownloadsAsync(items))
            throw new OperationCanceledException();
        State.DownloadsAccepted = true;
        if (pico)
            State.PicoDownloadsAccepted = true;
        State.Save(Workspace.StatePath);
        if (items.Any(i => i.Id == "long-paths"))
            await WindowsLongPaths.EnableAsync(ct);
    }

    /// <summary>Optional, unlike long paths: a "no" only means a slower build, and the question is not repeated</summary>
    private async Task AskDefenderExclusionAsync(CancellationToken ct)
    {
        if (await prompts.AskDefenderExclusionAsync(Workspace.Root) && await WindowsDefender.AddExclusionAsync(Workspace.Root, ct))
            State.DefenderExcluded = Workspace.Root;
        State.DefenderAsked = true;
        State.Save(Workspace.StatePath);
    }

    private async Task<UnityInstall> EnsureUnityAsync(CancellationToken ct)
    {
        var existing = UnityInstall.Find(Kit.Manifest.Unity.Version);
        if (existing is { HasAndroid: true })
        {
            progress.StepFinished("unity-setup", StepStatus.UpToDate);
            return existing;
        }
        progress.StepStarted("unity-setup");
        try
        {
            var installed = await UnitySetup.EnsureAsync(Workspace, Kit.Manifest.Unity,
                new Progress<(string Stage, double? Fraction, string? Detail)>(p =>
                    progress.StepProgress("unity-setup", p.Fraction, p.Stage, p.Detail)), ct);
            progress.StepFinished("unity-setup", StepStatus.Done);
            return installed;
        }
        catch (OperationCanceledException)
        {
            progress.StepFinished("unity-setup", StepStatus.Cancelled);
            throw;
        }
        catch
        {
            progress.StepFinished("unity-setup", StepStatus.Failed);
            throw;
        }
    }

    private async Task EnsureLicenseAsync(bool afterFailure)
    {
        if (!afterFailure && UnityLicense.LooksLicensed())
            return;
        while (true)
        {
            if (await prompts.AskForUnityLicenseAsync(afterFailure) == LicenseChoice.Cancel)
                throw new OperationCanceledException();
            if (UnityLicense.LooksLicensed())
                return;
        }
    }

    /// <summary>Opens Unity Hub for the player to sign in (and to add the Personal license)</summary>
    public void OpenUnityHub()
    {
        var hub = UnityHub.Executable();
        if (hub is null)
            return;
        System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(hub) { UseShellExecute = true });
    }

    /// <summary>
    /// The fallback when the Hub's command line could not install Unity: the Hub's own install dialog for exactly this
    /// version (the player ticks Android Build Support with OpenJDK and SDK &amp; NDK Tools)
    /// </summary>
    public void OpenUnityInstallInHub()
    {
        var unity = Kit.Manifest.Unity;
        System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(
            $"unityhub://{unity.Version}/{unity.Changeset}") { UseShellExecute = true });
    }

    public Adb? Adb => Unity is { } unity && File.Exists(unity.Adb) ? new Adb(unity.Adb) : null;

    public async Task<IReadOnlyList<AdbDevice>> FindDevicesAsync(CancellationToken ct) =>
        Adb is { } adb ? await adb.DevicesAsync(ct) : [];

    public Task<InstallOutcome> InstallAsync(AdbDevice device, string apk, IProgress<string> status, CancellationToken ct) =>
        QuestInstaller.InstallAsync(Adb ?? throw new BuildException("adb is not installed yet (it comes with Unity)") { Code = "adb-missing" },
            device, apk, Workspace.Backups, status, ct);

    /// <summary>The builder added the workspace to Defender's exclusions (removal offers to take it off)</summary>
    public bool HasDefenderExclusion => State.DefenderExcluded is not null;

    /// <summary>
    /// Removes the game from the headset. Its data goes to the desktop first (the workspace may be removed next);
    /// returns that folder, or null when there was none.
    /// </summary>
    public Task<string?> UninstallFromHeadsetAsync(AdbDevice device, IProgress<string> status, CancellationToken ct)
    {
        var desktop = Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory);
        var backupRoot = Directory.Exists(desktop) ? Path.Combine(desktop, "REPO Quest saves") : Workspace.Backups;
        return QuestInstaller.UninstallAsync(Adb ?? throw new BuildException("adb is not installed (it comes with Unity)") { Code = "adb-missing" },
            device, backupRoot, status, ct);
    }

    /// <summary>
    /// Takes the builder off this computer: the Defender exclusion and everything it put in the workspace except the
    /// headset backups, then its own settings. Unity and Unity Hub stay: their own installers put them there, and
    /// Windows removes them (Settings → Apps). The service is unusable afterwards; the caller exits.
    /// </summary>
    public async Task<RemovalResult> RemoveFromComputerAsync(bool workspace, bool defender, CancellationToken ct)
    {
        var failed = new List<string>();
        if (defender && State.DefenderExcluded is { } excluded)
        {
            if (await WindowsDefender.RemoveExclusionAsync(excluded, ct))
                State.DefenderExcluded = null;
            else
                failed.Add($"Defender: {excluded}");
            if (!workspace)
                State.Save(Workspace.StatePath);
        }
        if (!workspace)
            return new RemovalResult(null, failed);

        Log.Info($"removing the builder's files from {Workspace.Root}");
        Log.CloseFile();
        foreach (var entry in Workspace.OwnedEntries)
        {
            try
            {
                if (Directory.Exists(entry))
                    FileOps.DeleteDirectory(entry);
                else
                    FileOps.DeleteFile(entry);
            }
            catch (Exception e) when (e is IOException or UnauthorizedAccessException)
            {
                Log.Warn($"could not delete {entry}: {e.Message}");
                failed.Add(entry);
            }
        }
        var kept = Directory.Exists(Workspace.Backups) && Directory.EnumerateFileSystemEntries(Workspace.Backups).Any()
            ? Workspace.Backups
            : null;
        if (kept is null)
            FileOps.DeleteDirectory(Workspace.Backups);
        if (Directory.Exists(Workspace.Root) && !Directory.EnumerateFileSystemEntries(Workspace.Root).Any())
            Directory.Delete(Workspace.Root);
        AppSettings.Delete();
        return new RemovalResult(kept, failed);
    }

    public async Task<ReleaseManifest?> CheckForUpdateAsync(string? updateSource, CancellationToken ct)
    {
        var source = UpdateSource.Resolve(updateSource);
        if (source is null)
            return null;
        try
        {
            return await source.CheckAsync(ct);
        }
        catch (BuildException e)
        {
            Log.Warn($"update check: {e.Message}");
            return null;
        }
    }

    /// <summary>Downloads the new version and starts it in place of this one; the caller exits right after</summary>
    public async Task ApplyUpdateAsync(ReleaseManifest manifest, string? updateSource, IProgress<DownloadProgress>? download,
        IEnumerable<string> restartArguments, CancellationToken ct)
    {
        var source = UpdateSource.Resolve(updateSource) ?? throw new BuildException("No update source");
        var zip = await source.DownloadAsync(manifest, Path.Combine(Workspace.Downloads, "updates"), download, ct);
        SelfUpdater.ApplyAndRestart(zip, restartArguments);
    }

    /// <summary>The logs as a zip on the desktop, for whoever helps</summary>
    public string CreateReport()
    {
        var desktop = Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory);
        var target = Directory.Exists(desktop) ? desktop : Workspace.Root;
        return ReportBuilder.Create(Workspace, target, Game?.Path, Kit.Version);
    }

    private static long SafeFreeSpace(string path)
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
}
