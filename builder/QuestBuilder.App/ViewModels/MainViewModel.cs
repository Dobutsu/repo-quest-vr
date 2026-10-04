using System.Collections.ObjectModel;
using System.Diagnostics;
using Avalonia.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using RepoQuest.Builder.App.Localization;
using RepoQuest.Builder.Pipeline;
using RepoQuest.Builder.Quest;
using RepoQuest.Builder.Updates;

namespace RepoQuest.Builder.App.ViewModels;

/// <summary>What the window needs from its view: folder pickers and dialogs</summary>
public interface IWindowServices
{
    Task<string?> PickFolderAsync(string title, string? startAt);
    Task ShowErrorAsync(Exception error, Func<string>? createReport, Action? installInHub);
    Task ShowMessageAsync(string title, params string[] paragraphs);
    Task<bool> ConfirmAsync(string title, string text, string yes, string no);
    Task<AdbDevice?> PickDeviceAsync(IReadOnlyList<AdbDevice> devices);
    /// <summary>No headset (or one that has not allowed USB debugging): help; true = try again</summary>
    Task<bool> ShowInstallHelpAsync(bool unauthorized);
    IBuilderPrompts Prompts { get; }
    void Shutdown();
}

/// <summary>
/// The main window's state and commands over <see cref="BuilderService"/>. Core work runs on background threads;
/// its progress arrives through <see cref="IBuildProgress"/> on any thread and is moved to the UI thread here.
/// </summary>
public sealed partial class MainViewModel : ObservableObject, IBuildProgress
{
    private readonly StartupOptions options;
    private readonly IWindowServices view;
    private readonly AppSettings settings = AppSettings.Load();
    private readonly DispatcherTimer clock;
    private BuilderService? service;
    private CancellationTokenSource? work;
    private DateTime? buildStarted;
    private ReleaseManifest? update;
    private BuilderStatus? status;

    public MainViewModel(StartupOptions options, IWindowServices view)
    {
        this.options = options;
        this.view = view;
        if (options.Workspace is not null)
            settings.WorkspacePath = options.Workspace;
        Loc.Instance.SetLanguage(options.Language ?? settings.Language ?? "en"); // English unless the player chose Russian
        Loc.Instance.LanguageChanged += OnLanguageChanged;
        Steps = new ObservableCollection<StepViewModel>(BuilderService.Steps.Select(id => new StepViewModel(id)));
        clock = new DispatcherTimer { Interval = TimeSpan.FromSeconds(1) };
        clock.Tick += (_, _) => OnTick();
        clock.Start();
    }

    public ObservableCollection<StepViewModel> Steps { get; }
    public LogViewModel Log { get; } = new();

    public string VersionText => Loc.F("app.version", Platform.BuilderVersion);

    public bool IsRussian
    {
        get => Loc.Instance.Language == "ru";
        set
        {
            if (value)
                SetLanguage("ru");
        }
    }

    public bool IsEnglish
    {
        get => Loc.Instance.Language == "en";
        set
        {
            if (value)
                SetLanguage("en");
        }
    }

    // State shown in the window (see Apply for how a status turns into these)
    [ObservableProperty] private bool isReady;
    [ObservableProperty] private bool isChecking = true;
    [ObservableProperty] [NotifyPropertyChangedFor(nameof(IsIdle))] private bool isBusy;
    [ObservableProperty] private bool isBuilding;
    [ObservableProperty] private string? gamePathText;
    [ObservableProperty] private string? gameDetailText;
    [ObservableProperty] private string? gameProblemText;
    [ObservableProperty] private bool gameFound;
    [ObservableProperty] private string? workspacePathText;
    [ObservableProperty] private string? spaceText;
    [ObservableProperty] private bool spaceLow;
    [ObservableProperty] private string? workspaceProblemText;
    [ObservableProperty] private bool hasApk;
    [ObservableProperty] private string? lastApkText;
    [ObservableProperty] private string? busyText;
    [ObservableProperty] private bool showSuccess;
    [ObservableProperty] private string? successPath;
    [ObservableProperty] private bool showGameChanged;
    [ObservableProperty] private bool showBuilderChanged;
    [ObservableProperty] private string? untestedText;
    [ObservableProperty] private bool hasUpdate;
    [ObservableProperty] private string? updateTitle;
    [ObservableProperty] private string? updateNotes;
    [ObservableProperty] private string? updateProgressText;

    public bool IsIdle => !IsBusy;

    public async Task InitializeAsync()
    {
        try
        {
            service = new BuilderService(settings, view.Prompts, this);
            var kit = options.KitDir;
            await Task.Run(() => service.Initialize(kit));
            IsReady = true;
        }
        catch (Exception e)
        {
            Builder.Log.Error($"startup: {e}");
            IsChecking = false;
            await view.ShowErrorAsync(new BuildException(Loc.F("startup.failed", e.Message), e.ToString()), null, null);
            return;
        }
        await RefreshAsync();
        _ = CheckForUpdateAsync(quiet: true);
    }

    /// <summary>Reads the builder's status (hashes game files: a second or so) and shows it</summary>
    private async Task RefreshAsync()
    {
        if (service is null)
            return;
        IsChecking = true;
        try
        {
            status = await Task.Run(service.GetStatus);
            Apply(status, resetSteps: !IsBuilding);
        }
        catch (Exception e)
        {
            Builder.Log.Warn($"status: {e.Message}");
        }
        finally
        {
            IsChecking = false;
        }
    }

    private void Apply(BuilderStatus s, bool resetSteps)
    {
        GameFound = s.GamePath is not null;
        GamePathText = s.GamePath ?? Loc.T("game.notFound");
        GameDetailText = s.GamePath is null
            ? null
            : Loc.F("game.version", s.GameVersion ?? Loc.T("common.unknown")) + " · " +
              Loc.T(s.GameTested ? "game.tested" : "game.untested");
        WorkspacePathText = s.WorkspacePath;
        SpaceText = s.FreeBytes == long.MaxValue
            ? Loc.T("ws.spaceUnknown")
            : Loc.F("ws.space", Loc.Size(s.FreeBytes), Loc.Size(s.NeededBytes));
        SpaceLow = s.FreeBytes < s.NeededBytes;
        HasApk = s.ApkPath is not null;
        LastApkText = s.ApkPath is null ? null : Loc.F("build.lastApk", s.ApkPath);
        ShowGameChanged = s.GameChangedSinceApk;
        ShowBuilderChanged = s.BuilderChangedSinceApk && !s.GameChangedSinceApk;
        UntestedText = s.GamePath is not null && !s.GameTested ? Loc.F("banner.untested", s.GameVersion ?? "?") : null;

        if (!resetSteps)
            return;
        foreach (var step in Steps)
        {
            var done = step.Id switch
            {
                "unity-setup" => s.UnityReady,
                "setup" => false,
                _ => s.GamePath is not null && !s.PendingSteps.Contains(step.Id),
            };
            step.Reset(done ? StepStatus.UpToDate : StepStatus.Pending);
        }
    }

    [RelayCommand]
    private async Task ChooseGameAsync()
    {
        if (service is null)
            return;
        var path = await view.PickFolderAsync(Loc.T("game.pickerTitle"), service.Game?.Path);
        if (path is null)
            return;
        var problem = service.SetGamePath(path);
        GameProblemText = problem is null ? null : Loc.TryT($"problem.{problem}") ?? problem;
        await RefreshAsync();
    }

    [RelayCommand]
    private async Task ChangeWorkspaceAsync()
    {
        if (service is null)
            return;
        var path = await view.PickFolderAsync(Loc.T("ws.pickerTitle"), service.Workspace.Root);
        if (path is null)
            return;
        string? problem;
        try
        {
            problem = await Task.Run(() => service.SetWorkspace(path));
        }
        catch (Exception e)
        {
            await view.ShowErrorAsync(e, null, null);
            return;
        }
        WorkspaceProblemText = problem is null ? null : Loc.TryT($"problem.{problem}") ?? problem;
        await RefreshAsync();
    }

    [RelayCommand]
    private async Task BuildAsync()
    {
        if (service is null || IsBusy)
            return;
        foreach (var step in Steps)
            step.Reset(StepStatus.Pending);
        ShowSuccess = false;
        IsBusy = IsBuilding = true;
        buildStarted = DateTime.UtcNow;
        work = new CancellationTokenSource();
        var token = work.Token;
        try
        {
            var apk = await Task.Run(() => service.BuildAsync(token), token);
            SuccessPath = apk;
            ShowSuccess = true;
        }
        catch (OperationCanceledException)
        {
            Builder.Log.Info("build cancelled");
        }
        catch (Exception e)
        {
            Builder.Log.Error(e is BuildException b ? $"{b.Message}\n{b.Details}" : e.ToString());
            await view.ShowErrorAsync(e, service.CreateReport, service.OpenUnityInstallInHub);
        }
        finally
        {
            IsBusy = IsBuilding = false;
            buildStarted = null;
            BusyText = null;
            work.Dispose();
            work = null;
        }
        await RefreshAsync();
    }

    [RelayCommand]
    private async Task CancelBuildAsync()
    {
        if (work is null)
            return;
        if (await view.ConfirmAsync(Loc.T("cancel.title"), Loc.T("cancel.text"), Loc.T("cancel.stop"), Loc.T("cancel.continue")))
        {
            BusyText = Loc.T("build.stopping");
            await work.CancelAsync();
        }
    }

    [RelayCommand]
    private async Task InstallAsync()
    {
        if (service is null || IsBusy)
            return;
        var apk = status?.ApkPath ?? SuccessPath;
        if (apk is null || !File.Exists(apk))
            return;
        IsBusy = true;
        try
        {
            while (true)
            {
                BusyText = Loc.T("install.searching");
                var devices = await Task.Run(() => service.FindDevicesAsync(CancellationToken.None));
                var ready = devices.Where(d => d.Ready).ToList();
                if (ready.Count == 0)
                {
                    BusyText = null;
                    if (await view.ShowInstallHelpAsync(unauthorized: devices.Any(d => d.Unauthorized)))
                        continue;
                    return;
                }
                var device = ready.Count == 1 ? ready[0] : await view.PickDeviceAsync(ready);
                if (device is null)
                    return;

                var progress = new Progress<string>(code => BusyText = Loc.TryT($"install.{code}") ?? code);
                var outcome = await Task.Run(() => service.InstallAsync(device, apk, progress, CancellationToken.None));
                BusyText = null;
                var text = new List<string> { Loc.T("install.done.text") };
                if (outcome == InstallOutcome.ReinstalledWithData)
                    text.Add(Loc.T("install.done.reinstalled"));
                await view.ShowMessageAsync(Loc.T("install.done.title"), [.. text]);
                return;
            }
        }
        catch (Exception e)
        {
            Builder.Log.Error(e is BuildException b ? $"{b.Message}\n{b.Details}" : e.ToString());
            await view.ShowErrorAsync(e, service.CreateReport, null);
        }
        finally
        {
            IsBusy = false;
            BusyText = null;
        }
    }

    [RelayCommand]
    private void OpenApkFolder()
    {
        var apk = SuccessPath ?? status?.ApkPath;
        if (apk is not null)
            Reveal(apk);
    }

    [RelayCommand]
    private async Task ReportAsync()
    {
        if (service is null)
            return;
        try
        {
            Reveal(await Task.Run(service.CreateReport));
        }
        catch (Exception e)
        {
            await view.ShowErrorAsync(e, null, null);
        }
    }

    [RelayCommand]
    private Task CheckUpdatesAsync() => CheckForUpdateAsync(quiet: false);

    private async Task CheckForUpdateAsync(bool quiet)
    {
        if (service is null)
            return;
        if (UpdateSource.Resolve(options.UpdateSource) is null)
        {
            if (!quiet)
                await view.ShowMessageAsync(Loc.T("update.title"), Loc.T("update.notConfigured"));
            return;
        }
        var found = await Task.Run(() => service.CheckForUpdateAsync(options.UpdateSource, CancellationToken.None));
        update = found;
        HasUpdate = found is not null;
        ShowUpdateTexts();
        if (found is null && !quiet)
            await view.ShowMessageAsync(Loc.T("update.title"), Loc.T("update.latest"));
    }

    [RelayCommand]
    private async Task UpdateNowAsync()
    {
        if (service is null || update is null || IsBusy)
            return;
        IsBusy = true;
        try
        {
            var progress = new Progress<DownloadProgress>(p =>
                UpdateProgressText = Loc.F("banner.update.downloading",
                    p.Total is { } total && total > 0 ? $"{p.Done * 100 / total}%" : Loc.Size(p.Done)));
            var manifest = update;
            await Task.Run(() => service.ApplyUpdateAsync(manifest, options.UpdateSource, progress, options.Args,
                CancellationToken.None));
            // The new version is starting in our place
            view.Shutdown();
        }
        catch (Exception e)
        {
            UpdateProgressText = null;
            await view.ShowErrorAsync(e, service.CreateReport, null);
        }
        finally
        {
            IsBusy = false;
        }
    }

    /// <summary>Called by the window before it closes: false keeps it open</summary>
    public async Task<bool> CanCloseAsync()
    {
        if (!IsBusy)
            return true;
        if (!await view.ConfirmAsync(Loc.T("close.title"), Loc.T("close.text"), Loc.T("close.stop"), Loc.T("close.keep")))
            return false;
        work?.Cancel();
        ProcessRunner.KillAll();
        return true;
    }

    public void Stop()
    {
        clock.Stop();
        Log.Stop();
    }

    /// <summary>--screenshot: a build in its last step, for checking the layout</summary>
    public void ShowSampleBuild()
    {
        foreach (var step in Steps)
        {
            if (step.Id == "unity")
            {
                step.Start();
                step.Report(0.62, "il2cpp", "10523/16994");
            }
            else
            {
                step.Start();
                step.Finish(step.Id == "unity-setup" ? StepStatus.UpToDate : StepStatus.Done);
            }
        }
        IsBusy = IsBuilding = true;
        BusyText = BuildLine(TimeSpan.FromSeconds(34 * 60 + 12));
    }

    /// <summary>The line under the window during a build: total time and what the running step does (the list may
    /// be scrolled away from it)</summary>
    private string BuildLine(TimeSpan elapsed)
    {
        var line = Loc.F("build.running", StepViewModel.Format(elapsed));
        return Steps.FirstOrDefault(s => s.IsRunning) is { } step ? $"{line} · {step.Title}: {step.StateText}" : line;
    }

    // IBuildProgress: from any thread, applied on the UI thread
    void IBuildProgress.StepStarted(string step) => OnStep(step, s => s.Start());

    void IBuildProgress.StepProgress(string step, double? fraction, string? stage, string? detail) =>
        OnStep(step, s => s.Report(fraction, stage, detail));

    void IBuildProgress.StepFinished(string step, StepStatus status) => OnStep(step, s => s.Finish(status));

    private void OnStep(string id, Action<StepViewModel> change) => Dispatcher.UIThread.Post(() =>
    {
        if (Steps.FirstOrDefault(s => s.Id == id) is { } step)
            change(step);
    });

    private void OnTick()
    {
        foreach (var step in Steps)
            step.Tick();
        if (IsBuilding && buildStarted is { } start && work is not null)
            BusyText = BuildLine(DateTime.UtcNow - start);
    }

    private void SetLanguage(string language)
    {
        Loc.Instance.SetLanguage(language);
        settings.Language = language;
        try
        {
            settings.Save();
        }
        catch (IOException)
        {
        }
    }

    private void OnLanguageChanged()
    {
        OnPropertyChanged(nameof(IsRussian));
        OnPropertyChanged(nameof(IsEnglish));
        OnPropertyChanged(nameof(VersionText));
        foreach (var step in Steps)
            step.Refresh();
        if (status is not null)
            Apply(status, resetSteps: false);
        ShowUpdateTexts();
    }

    private void ShowUpdateTexts()
    {
        UpdateTitle = update is null ? null : Loc.F("banner.update.title", update.Version);
        UpdateNotes = update?.NotesFor(Loc.Instance.Language);
    }

    /// <summary>Shows a file in the system's file manager</summary>
    private static void Reveal(string path)
    {
        try
        {
            if (OperatingSystem.IsWindows())
                Process.Start(new ProcessStartInfo("explorer.exe", $"/select,\"{path}\"") { UseShellExecute = true });
            else if (OperatingSystem.IsMacOS())
                Process.Start("open", ["-R", path]);
            else
                Process.Start(new ProcessStartInfo(Path.GetDirectoryName(path)!) { UseShellExecute = true });
        }
        catch (Exception e)
        {
            Builder.Log.Warn($"could not open {path}: {e.Message}");
        }
    }
}
