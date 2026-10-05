using Avalonia;
using RepoQuest.Builder.Setup;

namespace RepoQuest.Builder.App;

// The builder's window. Players start it without arguments; the options are for development and testing:
//
//   RepoQuestBuilder [--kit <dir>] [--update-source <url|dir>] [--workspace <dir>] [--lang ru|en]
//   RepoQuestBuilder --tool <name> <args>    one of the .NET tools (the builder runs itself this way)
//
// --kit: a kit folder (scripts/make-kit.sh -> work/kit) instead of the kit inside release builds
internal static class Program
{
    /// <summary>STA: the Windows file dialogs and clipboard are COM objects that need it</summary>
    [STAThread]
    public static int Main(string[] args)
    {
        // The builder starts itself as a child process to run its .NET tools: that is decided before Avalonia
        // starts, or every tool run would open a window
        if (ToolHost.TryRun(args, out var code))
            return code;

        // A crash ends up in builder.log, not only in the Windows event log
        AppDomain.CurrentDomain.UnhandledException += (_, e) => Builder.Log.Error($"crash: {e.ExceptionObject}");
        App.Options = StartupOptions.Parse(args);
        return BuildAvaloniaApp().StartWithClassicDesktopLifetime(args);
    }

    /// <summary>Also what the XAML previewer of an IDE calls</summary>
    public static AppBuilder BuildAvaloniaApp() =>
        AppBuilder.Configure<App>()
            .UsePlatformDetect()
            .WithInterFont()
            .LogToTrace();
}
