namespace RepoQuest.Builder.Setup;

/// <summary>An installed Unity editor with Android build support</summary>
public sealed class UnityInstall
{
    public UnityInstall(string root)
    {
        Root = Path.GetFullPath(root);
    }

    /// <summary>The version folder: the one with Unity.app (macOS) or Editor\ (Windows)</summary>
    public string Root { get; }

    public string Executable => Platform.IsMac
        ? Path.Combine(Root, "Unity.app", "Contents", "MacOS", "Unity")
        : Path.Combine(Root, "Editor", "Unity.exe");

    /// <summary>Android Build Support: PlaybackEngines/AndroidPlayer next to Unity.app (macOS), in Editor\Data (Windows)</summary>
    public string AndroidPlayer => Platform.IsMac
        ? Path.Combine(Root, "PlaybackEngines", "AndroidPlayer")
        : Path.Combine(Root, "Editor", "Data", "PlaybackEngines", "AndroidPlayer");

    public string Adb => Path.Combine(AndroidPlayer, "SDK", "platform-tools", Platform.Exe("adb"));

    public bool HasEditor => File.Exists(Executable);

    /// <summary>Android Build Support with its SDK, NDK and OpenJDK (the Hub's child modules)</summary>
    public bool HasAndroid =>
        Directory.Exists(Path.Combine(AndroidPlayer, "SDK", "platforms")) &&
        Directory.Exists(Path.Combine(AndroidPlayer, "NDK")) &&
        Directory.Exists(Path.Combine(AndroidPlayer, "OpenJDK"));

    public override string ToString() => Root;

    /// <summary>Where the Hub puts editors by default</summary>
    public static IEnumerable<string> DefaultRoots(string version)
    {
        if (Platform.IsMac)
        {
            yield return Path.Combine("/Applications/Unity/Hub/Editor", version);
        }
        else if (Platform.IsWindows)
        {
            foreach (var programFiles in new[]
                     {
                         Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles),
                         Environment.GetEnvironmentVariable("ProgramW6432") ?? "",
                     }.Where(p => p.Length > 0).Distinct())
                yield return Path.Combine(programFiles, "Unity", "Hub", "Editor", version);
        }
    }

    /// <summary>The Android parts that are not there (empty when Android Build Support is complete)</summary>
    public IEnumerable<string> MissingAndroidParts() =>
        new[] { Path.Combine("SDK", "platforms"), "NDK", "OpenJDK" }
            .Where(part => !Directory.Exists(Path.Combine(AndroidPlayer, part)))
            .Select(part => Path.Combine(AndroidPlayer, part));

    /// <summary>
    /// An installed editor of exactly this version, if there is one (Hub's default and custom locations); with two
    /// copies, the one with Android Build Support
    /// </summary>
    public static UnityInstall? Find(string version, IEnumerable<string>? extraRoots = null)
    {
        var roots = DefaultRoots(version).Concat(UnityHub.CustomEditorRoots(version)).Concat(extraRoots ?? []);
        var editors = roots.Select(r => new UnityInstall(r)).Where(u => u.HasEditor).ToList();
        return editors.FirstOrDefault(u => u.HasAndroid) ?? editors.FirstOrDefault();
    }
}
