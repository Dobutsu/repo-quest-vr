namespace RepoQuest.Builder.App;

/// <summary>The command line (see Program.cs); players pass nothing, so every option is optional and unknown ones are
/// ignored (macOS may add its own)</summary>
public sealed class StartupOptions
{
    /// <summary>The arguments as given: an update restarts the new version with them</summary>
    public IReadOnlyList<string> Args { get; private init; } = [];

    /// <summary>A kit folder instead of the embedded kit (development)</summary>
    public string? KitDir { get; private init; }

    /// <summary>Where to look for updates instead of the configured source (a URL or a local folder)</summary>
    public string? UpdateSource { get; private init; }

    /// <summary>The workspace to use (testing); it is remembered like a workspace chosen in the window</summary>
    public string? Workspace { get; private init; }

    /// <summary>"ru" or "en" for this run only (not remembered)</summary>
    public string? Language { get; private init; }

    /// <summary>Render the window and every dialog to PNG files in this folder, then exit (layout checks)</summary>
    public string? Screenshot { get; private init; }

    public static StartupOptions Parse(string[] args)
    {
        string? kit = null, updateSource = null, workspace = null, language = null, screenshot = null;
        for (var i = 0; i < args.Length; i++)
        {
            var value = i + 1 < args.Length ? args[i + 1] : null;
            switch (args[i])
            {
                case "--kit" when value is not null:
                    kit = Path.GetFullPath(value);
                    i++;
                    break;
                case "--update-source" when value is not null:
                    updateSource = value;
                    i++;
                    break;
                case "--workspace" when value is not null:
                    workspace = Path.GetFullPath(value);
                    i++;
                    break;
                case "--lang" when value is "ru" or "en":
                    language = value;
                    i++;
                    break;
                case "--screenshot" when value is not null:
                    screenshot = Path.GetFullPath(value);
                    i++;
                    break;
            }
        }
        return new StartupOptions
        {
            Args = args,
            KitDir = kit,
            UpdateSource = updateSource,
            Workspace = workspace,
            Language = language,
            Screenshot = screenshot,
        };
    }
}
