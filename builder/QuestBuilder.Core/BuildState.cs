using System.Text.Json;

namespace RepoQuest.Builder;

/// <summary>
/// What the builder remembers between runs (state.json in the workspace): the game folder, the stamps of finished
/// pipeline steps, the last APK and the player's answers to one-time questions.
/// </summary>
public sealed class BuildState
{
    public string? GamePath { get; set; }

    /// <summary>Step id -> stamp of the inputs it last finished with (see <see cref="Pipeline.BuildPipeline"/>)</summary>
    public Dictionary<string, string> Stamps { get; set; } = new();

    public string? LastApk { get; set; }
    /// <summary>"quest" or "pico": the headset <see cref="LastApk"/> is for (null: quest, from before Pico)</summary>
    public string? LastApkHeadset { get; set; }
    public string? LastApkVersion { get; set; }
    /// <summary>Fingerprint of the game the last APK was built from (a game update means: rebuild)</summary>
    public string? LastApkGame { get; set; }
    public DateTime? LastApkTime { get; set; }

    /// <summary>The player accepted the downloads and licenses listed in the consent dialog</summary>
    public bool DownloadsAccepted { get; set; }
    /// <summary>The headset the builder builds for: "quest" (null) or "pico"</summary>
    public string? Headset { get; set; }
    /// <summary>The player accepted the downloads only a Pico build needs (Java, ovrport)</summary>
    public bool PicoDownloadsAccepted { get; set; }
    /// <summary>The note about Steam login in the headset was shown</summary>
    public bool OnlineNoticeShown { get; set; }
    /// <summary>Windows: the player was asked about excluding the workspace from Defender's scanning</summary>
    public bool DefenderAsked { get; set; }
    /// <summary>The folder the builder added to Defender's exclusions (null: none), to take it off again</summary>
    public string? DefenderExcluded { get; set; }
    /// <summary>"ru" or "en"; null: the system language</summary>
    public string? Language { get; set; }

    /// <summary>Durations of earlier Unity builds in seconds (for the time estimate)</summary>
    public List<int> UnityBuildDurations { get; set; } = new();

    public static BuildState Load(string path)
    {
        try
        {
            if (File.Exists(path))
                return JsonSerializer.Deserialize<BuildState>(File.ReadAllText(path), KitManifest.Json) ?? new BuildState();
        }
        catch (JsonException e)
        {
            Log.Warn($"{path} is damaged ({e.Message}); starting with a fresh state");
        }
        return new BuildState();
    }

    public void Save(string path) => FileOps.WriteTextAtomic(path, JsonSerializer.Serialize(this, KitManifest.Json));
}
