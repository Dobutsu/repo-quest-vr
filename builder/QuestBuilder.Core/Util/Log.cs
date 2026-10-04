using System.Text;

namespace RepoQuest.Builder;

public enum LogLevel { Debug, Info, Warning, Error }

/// <summary>
/// The builder's log: every line goes to the log file (logs/builder.log in the workspace) and to subscribers (the
/// window's log view, the console). Debug lines (output of tools, Python, Unity) only go to the file and to
/// subscribers that ask for them.
/// </summary>
public static class Log
{
    private static readonly object Gate = new();
    private static StreamWriter? file;

    public static event Action<LogLevel, string>? Message;

    public static string? FilePath { get; private set; }

    public static void OpenFile(string path)
    {
        lock (Gate)
        {
            file?.Dispose();
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            file = new StreamWriter(new FileStream(path, FileMode.Append, FileAccess.Write, FileShare.ReadWrite),
                new UTF8Encoding(false)) { AutoFlush = true };
            FilePath = path;
            file.WriteLine();
            file.WriteLine($"==== {DateTime.Now:yyyy-MM-dd HH:mm:ss} REPO Quest Builder {Platform.BuilderVersion} ({Platform.Rid}, " +
                           $"{Environment.OSVersion.VersionString})");
        }
    }

    public static void Debug(string text) => Write(LogLevel.Debug, text);
    public static void Info(string text) => Write(LogLevel.Info, text);
    public static void Warn(string text) => Write(LogLevel.Warning, text);
    public static void Error(string text) => Write(LogLevel.Error, text);

    public static void Write(LogLevel level, string text)
    {
        lock (Gate)
        {
            file?.WriteLine($"{DateTime.Now:HH:mm:ss} {Prefix(level)}{text}");
        }
        Message?.Invoke(level, text);
    }

    private static string Prefix(LogLevel level) => level switch
    {
        LogLevel.Debug => "  ",
        LogLevel.Warning => "WARN ",
        LogLevel.Error => "ERROR ",
        _ => "",
    };
}
