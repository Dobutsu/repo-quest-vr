namespace RepoQuest.Builder.Pipeline;

/// <summary>
/// For Pico 4 / 4 Ultra: ovrport (github.com/ovrport/app, GPL-3.0) converts the Quest APK. It swaps the OpenXR loader
/// for its own, which picks Pico's runtime and maps the Touch controller bindings to Pico's, and adds the manifest
/// entries Pico needs; the game's own libraries stay as Unity built them. ovrport signs with a key it makes once in
/// <see cref="Workspace.Ovrport"/>, so a new APK installs over the old one. The game itself does the rest at runtime
/// (unity-tools/Runtime/ShaderWarmup.cs: Pico 4 hangs its GPU when the menu draws everything at once).
/// </summary>
public sealed class PicoStep : BuildStep
{
    /// <summary>ovrport's OpenXR loader release (an "instance"): the newest one not marked experimental, tested 16.10</summary>
    private const string Instance = "3.3.0";

    /// <summary>ovrport's recommended patches, without patch_replace_icon_label (it looks the package up in Meta's
    /// store, where ours is not)</summary>
    private const string Patches =
        "patch_copy_libraries;patch_copy_ovrplugin_vrapi;patch_fix_min_android_sdk;patch_generate_config;" +
        "patch_remove_localized_names;patch_clean_up_frida;patch_oculus_unity;patch_oculus_unreal;patch_vr_metadata;" +
        "patch_launcher_entry;patch_remove_uses_library;patch_fix_unreal_crash;patch_meta_xr_audio;" +
        "patch_mark_as_debuggable;patch_mark_allow_backup";

    public override string Id => "pico";

    public override bool AppliesTo(BuildContext context) => context.Options.Headset == Headset.Pico;

    public override string Stamp(BuildContext context, IReadOnlyDictionary<string, string> earlierStamps)
    {
        var downloads = context.Kit.Manifest.Downloads;
        return Hashing.Combine(Id, earlierStamps.GetValueOrDefault("unity"), downloads.Ovrport?.Sha256,
            downloads.Java?.Version, Instance, Patches, context.Options.HeadsetApkPath(context.Workspace));
    }

    public override bool ResultExists(BuildContext context) => File.Exists(context.Options.HeadsetApkPath(context.Workspace));

    public override async Task RunAsync(BuildContext context, CancellationToken ct)
    {
        var workspace = context.Workspace;
        var input = context.Options.ApkPath(workspace);
        var output = context.Options.HeadsetApkPath(workspace);
        if (!File.Exists(input))
            throw new BuildException($"The Quest APK to convert is missing: {input}");

        Report(context, null, "download");
        var java = await context.ThirdParty.JavaAsync(ct);
        var jar = await context.ThirdParty.OvrportJarAsync(ct);

        Report(context, null, "patch");
        Directory.CreateDirectory(workspace.Ovrport);
        FileOps.DeleteFile(output);
        var log = Path.Combine(workspace.Logs, "ovrport.log");
        using var writer = new StreamWriter(log, append: false);
        var result = await ProcessRunner.RunAsync(new ProcessSpec
        {
            FileName = java,
            // The APK (~450 MB) is unpacked and repacked in memory
            Arguments =
            [
                "-Xmx4g", "-jar", jar, "patch",
                $"--input={input}", $"--output={workspace.Build}", $"--output-name={Path.GetFileName(output)}",
                $"--workspace={workspace.Ovrport}", $"--version={Instance}", $"--patches={Patches}",
            ],
            WorkingDirectory = workspace.Build,
            LogPrefix = null,
        }, line =>
        {
            lock (writer)
                writer.WriteLine(line);
            // Progress bars redraw with \r; the lines worth keeping say what it uses and makes
            if (line.StartsWith("Using", StringComparison.Ordinal) || line.StartsWith("Output file", StringComparison.Ordinal)
                || line.Contains("rror", StringComparison.Ordinal))
                Log.Info($"[ovrport] {line.Trim()}");
        }, ct);
        if (result.ExitCode != 0 || !File.Exists(output))
            throw new BuildException($"ovrport could not convert the APK for Pico (exit code {result.ExitCode})",
                $"log: {log}\n{result.TailText}") { Code = "pico-convert" };

        var state = context.State;
        state.LastApk = output;
        state.LastApkHeadset = "pico";
        context.SaveState();
    }
}
