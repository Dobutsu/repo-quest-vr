using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Input.Platform;
using Avalonia.Interactivity;
using Avalonia.Media.Imaging;
using Avalonia.Platform.Storage;
using Avalonia.Threading;
using RepoQuest.Builder.App.Localization;
using RepoQuest.Builder.App.ViewModels;
using RepoQuest.Builder.Quest;
using RepoQuest.Builder.Setup;

namespace RepoQuest.Builder.App.Views;

public partial class MainWindow : Window, IWindowServices
{
    private bool closeConfirmed;

    public MainWindow()
    {
        InitializeComponent();
        Prompts = new WindowPrompts(this);
        Closing += OnClosing;
        DataContextChanged += (_, _) =>
        {
            if (DataContext is MainViewModel vm)
                vm.Log.Appended += ScrollLogToEnd;
        };
    }

    public IBuilderPrompts Prompts { get; }

    private MainViewModel? ViewModel => DataContext as MainViewModel;

    private async void OnClosing(object? sender, WindowClosingEventArgs e)
    {
        if (closeConfirmed || ViewModel is not { IsBusy: true } vm)
        {
            ViewModel?.Stop();
            return;
        }
        e.Cancel = true;
        if (await vm.CanCloseAsync())
        {
            closeConfirmed = true;
            vm.Stop();
            Close();
        }
    }

    private void ScrollLogToEnd()
    {
        if (LogList.ItemCount > 0)
            LogList.ScrollIntoView(LogList.ItemCount - 1);
    }

    private async void OnCopyLog(object? sender, RoutedEventArgs e)
    {
        if (ViewModel is { } vm && Clipboard is { } clipboard)
            await clipboard.SetTextAsync(vm.Log.Text);
    }

    public async Task<string?> PickFolderAsync(string title, string? startAt)
    {
        var options = new FolderPickerOpenOptions { Title = title, AllowMultiple = false };
        if (startAt is not null && Directory.Exists(startAt))
            options.SuggestedStartLocation = await StorageProvider.TryGetFolderFromPathAsync(startAt);
        var folders = await StorageProvider.OpenFolderPickerAsync(options);
        return folders.FirstOrDefault()?.TryGetLocalPath();
    }

    public Task ShowErrorAsync(Exception error, Func<string>? createReport, Action? installInHub) =>
        Dialogs.Error(error, createReport is null ? null : () => RevealReport(createReport), installInHub).ShowAsync(this);

    public Task ShowMessageAsync(string title, params string[] paragraphs) =>
        Dialogs.Message(title, paragraphs).ShowAsync(this);

    public Task<bool> ConfirmAsync(string title, string text, string yes, string no) =>
        Dialogs.Confirm(title, text, yes, no).ShowAsync(this);

    public async Task<AdbDevice?> PickDeviceAsync(IReadOnlyList<AdbDevice> devices)
    {
        var choice = await Dialogs.PickDevice(devices).ShowAsync(this);
        return choice >= 0 ? devices[choice] : null;
    }

    public Task<bool> ShowInstallHelpAsync(bool unauthorized) => Dialogs.InstallHelp(unauthorized).ShowAsync(this);

    public void Shutdown()
    {
        closeConfirmed = true;
        if (Application.Current?.ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
            desktop.Shutdown();
    }

    private static void RevealReport(Func<string> createReport)
    {
        try
        {
            var path = createReport();
            if (OperatingSystem.IsWindows())
                System.Diagnostics.Process.Start("explorer.exe", $"/select,\"{path}\"");
            else if (OperatingSystem.IsMacOS())
                System.Diagnostics.Process.Start("open", ["-R", path]);
        }
        catch (Exception e)
        {
            Log.Warn($"report: {e.Message}");
        }
    }

    private static void OpenHub()
    {
        var hub = UnityHub.Executable();
        if (hub is null)
        {
            Dialog<bool>.OpenUrl("https://unity.com/download");
            return;
        }
        System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(hub) { UseShellExecute = true });
    }

    /// <summary>
    /// --screenshot: the window (as it is, then mid-build) and every dialog rendered to PNG files in both languages,
    /// for checking the layout without a display; the builder then exits
    /// </summary>
    public async Task SaveScreenshotsAsync(string folder)
    {
        Directory.CreateDirectory(folder);
        var vm = ViewModel!;
        foreach (var language in new[] { "ru", "en" })
        {
            Loc.Instance.SetLanguage(language);
            await Task.Delay(800);
            Render(this, Path.Combine(folder, $"main-{language}.png"));
            vm.ShowSampleBuild();
            await Task.Delay(800);
            Render(this, Path.Combine(folder, $"building-{language}.png"));

            var samples = new (string Name, Window Dialog)[]
            {
                ("consent", Dialogs.Consent([
                    new ConsentItem("unity-hub", null, 200L << 20, "https://unity.com/legal/terms-of-service"),
                    new ConsentItem("unity-editor", "2022.3.62f3", 6L << 30, "https://unity.com/legal/terms-of-service"),
                    new ConsentItem("android", null, 0, "https://developer.android.com/studio/terms"),
                    new ConsentItem("assetripper", "2.0.0", 45L << 20, "https://github.com/AssetRipper/AssetRipper/blob/master/License.md"),
                    new ConsentItem("python", "3.13", 150L << 20, "https://docs.python.org/3/license.html"),
                    new ConsentItem("long-paths", null, 0, null),
                ])),
                ("notice", Dialogs.OnlineNotice()),
                ("license", Dialogs.License(afterFailure: true, OpenHub)),
                ("untested", Dialogs.Untested("v0.4.5.0")),
                ("error-hub", Dialogs.Error(new BuildException("Unity Hub did not install Unity 2022.3.62f3",
                    "[hub] Error: install failed (exit code 1)") { Code = "unity-install-failed" }, () => { }, () => { })),
                ("error-unexpected", Dialogs.Error(new InvalidOperationException("Something odd"), () => { }, null)),
                ("install-none", Dialogs.InstallHelp(unauthorized: false)),
                ("install-unauthorized", Dialogs.InstallHelp(unauthorized: true)),
                ("installed", Dialogs.Message(Loc.T("install.done.title"), Loc.T("install.done.text"), Loc.T("install.done.reinstalled"))),
                ("cancel", Dialogs.Confirm(Loc.T("cancel.title"), Loc.T("cancel.text"), Loc.T("cancel.stop"), Loc.T("cancel.continue"))),
            };
            foreach (var (name, dialog) in samples)
            {
                dialog.Show(this);
                await Task.Delay(500);
                Render(dialog, Path.Combine(folder, $"{name}-{language}.png"));
                dialog.Close();
            }
        }
    }

    private static void Render(Window window, string path)
    {
        var size = new PixelSize((int)Math.Ceiling(window.Bounds.Width), (int)Math.Ceiling(window.Bounds.Height));
        using var bitmap = new RenderTargetBitmap(size, new Vector(96, 96));
        bitmap.Render(window);
        bitmap.Save(path, new PngBitmapEncoderOptions());
    }

    /// <summary>The builder's questions to the player, as dialogs over this window (asked from background threads)</summary>
    private sealed class WindowPrompts(MainWindow window) : IBuilderPrompts
    {
        public Task<bool> AcceptDownloadsAsync(IReadOnlyList<ConsentItem> items) =>
            OnUiThread(() => Dialogs.Consent(items).ShowAsync(window));

        public Task ShowOnlineNoticeAsync() => OnUiThread(() => Dialogs.OnlineNotice().ShowAsync(window));

        public Task<LicenseChoice> AskForUnityLicenseAsync(bool afterFailure) =>
            OnUiThread(() => Dialogs.License(afterFailure, OpenHub).ShowAsync(window));

        public Task<bool> ConfirmUntestedGameAsync(string? gameVersion) =>
            OnUiThread(() => Dialogs.Untested(gameVersion).ShowAsync(window));

        private static Task<T> OnUiThread<T>(Func<Task<T>> show) =>
            Dispatcher.UIThread.CheckAccess() ? show() : Dispatcher.UIThread.InvokeAsync(show);

        private static Task OnUiThread(Func<Task> show) =>
            Dispatcher.UIThread.CheckAccess() ? show() : Dispatcher.UIThread.InvokeAsync(show);
    }
}
