using System.Runtime.InteropServices;

namespace RepoQuest.Builder;

public static class Platform
{
    public static bool IsWindows => OperatingSystem.IsWindows();

    public static bool IsMac => OperatingSystem.IsMacOS();

    /// <summary>Runtime identifier of this machine, the key of per-platform downloads in kit.json</summary>
    public static string Rid => IsWindows ? "win-x64"
        : RuntimeInformation.OSArchitecture == Architecture.Arm64 ? "osx-arm64" : "osx-x64";

    public static string Exe(string name) => IsWindows ? name + ".exe" : name;

    /// <summary>Version of the builder (and of its kit and the APK it builds)</summary>
    public static string BuilderVersion { get; } =
        typeof(Platform).Assembly.GetName().Version is { } v ? $"{v.Major}.{v.Minor}.{v.Build}" : "0.0.0";
}
