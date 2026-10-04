using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Markup.Xaml;
using RepoQuest.Builder.App.ViewModels;
using RepoQuest.Builder.App.Views;

namespace RepoQuest.Builder.App;

public partial class App : Application
{
    /// <summary>Set by Program.Main before Avalonia starts</summary>
    public static StartupOptions Options { get; set; } = StartupOptions.Parse([]);

    public override void Initialize() => AvaloniaXamlLoader.Load(this);

    public override void OnFrameworkInitializationCompleted()
    {
        if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
        {
            var window = new MainWindow();
            var viewModel = new MainViewModel(Options, window);
            window.DataContext = viewModel;
            // The builder starts its work once the window is on screen (kit, status, update check)
            window.Opened += async (_, _) =>
            {
                await viewModel.InitializeAsync();
                if (Options.Screenshot is { } folder)
                {
                    await window.SaveScreenshotsAsync(folder);
                    window.Shutdown();
                }
            };
            desktop.MainWindow = window;
        }
        base.OnFrameworkInitializationCompleted();
    }
}
