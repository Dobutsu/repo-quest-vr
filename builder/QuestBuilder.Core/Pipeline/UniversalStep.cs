using System.IO.Compression;

namespace RepoQuest.Builder.Pipeline;

/// <summary>
/// One APK for Meta Quest and Pico 4 / 4 Ultra: ovrport (github.com/ovrport/app, GPL-3.0) converts Unity's Quest APK.
/// It swaps the OpenXR loader for its own, which loads the headset maker's runtime by ro.product.brand (Meta's on a
/// Quest, Pico's on a Pico, where it also maps the Touch controller bindings to Pico's), and adds the manifest entries
/// Pico needs next to the Quest's; the game's own libraries stay as Unity built them. ovrport signs with a key it makes
/// once in <see cref="Workspace.Ovrport"/>, so a new APK installs over the old one (the first universal APK replaces a
/// Quest-only one signed with Unity's key: the installer keeps the data). The game does the rest at runtime
/// (unity-tools/Runtime: ShaderWarmup for the Pico 4's GPU hang, QuestSpaceWarp's frame rate cap on Pico).
/// </summary>
public sealed partial class UniversalStep : BuildStep
{
    /// <summary>ovrport's OpenXR loader release (an "instance"): the newest one not marked experimental, tested 16.10</summary>
    private const string Instance = "3.3.0";

    /// <summary>ovrport's recommended patches, without patch_replace_icon_label (it looks the package up in Meta's
    /// store, where ours is not) and patch_mark_as_debuggable (Android runs debuggable apps slower, on Quest too)</summary>
    private const string Patches =
        "patch_copy_libraries;patch_copy_ovrplugin_vrapi;patch_fix_min_android_sdk;patch_generate_config;" +
        "patch_remove_localized_names;patch_clean_up_frida;patch_oculus_unity;patch_oculus_unreal;patch_vr_metadata;" +
        "patch_launcher_entry;patch_remove_uses_library;patch_fix_unreal_crash;patch_meta_xr_audio;" +
        "patch_mark_allow_backup";

    public override string Id => "universal";

    public override bool AppliesTo(BuildContext context) => context.Options.Universal;

    public override string Stamp(BuildContext context, IReadOnlyDictionary<string, string> earlierStamps)
    {
        var downloads = context.Kit.Manifest.Downloads;
        return Hashing.Combine(Id, earlierStamps.GetValueOrDefault("unity"), downloads.Ovrport?.Sha256,
            downloads.Java?.Version, Instance, Patches, context.Options.UniversalApkPath(context.Workspace));
    }

    public override bool ResultExists(BuildContext context) => File.Exists(context.Options.UniversalApkPath(context.Workspace));

    public override async Task RunAsync(BuildContext context, CancellationToken ct)
    {
        var workspace = context.Workspace;
        var input = context.Options.ApkPath(workspace);
        var output = context.Options.UniversalApkPath(workspace);
        if (!File.Exists(input))
            throw new BuildException($"Unity's APK to convert is missing: {input}");

        Report(context, null, "download");
        var java = await context.ThirdParty.JavaAsync(ct);
        var jar = await context.ThirdParty.OvrportJarAsync(ct);

        Report(context, null, "patch");
        Directory.CreateDirectory(workspace.Ovrport);
        FileOps.DeleteFile(output);
        // ovrport unpacks the APK into <workspace>/dec. Unity's APK has resource names that differ only in case
        // (res/Dm.xml and res/dM.xml), and on a Mac's case-insensitive disk one of each pair was lost (builder 1.5.0,
        // checked against a Windows run): on a Mac ovrport works on a case-sensitive disk image instead
        await using var volume = Platform.IsMac
            ? await CaseSensitiveVolume.MountAsync(Path.Combine(workspace.Tools, "ovrport-volume"), ct)
            : null;
        var ovrportWorkspace = workspace.Ovrport;
        if (volume is not null)
        {
            ovrportWorkspace = Path.Combine(volume.Root, "ovrport-ws");
            FileOps.CopyDirectory(workspace.Ovrport, ovrportWorkspace);
        }
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
                $"--workspace={ovrportWorkspace}", $"--version={Instance}", $"--patches={Patches}",
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
            throw new BuildException($"ovrport could not make the universal APK (exit code {result.ExitCode})",
                $"log: {log}\n{result.TailText}") { Code = "universal-convert" };
        if (volume is not null)
            KeepOvrportFiles(ovrportWorkspace, workspace.Ovrport);
        if (MissingCaseTwins(input, output) is { Count: > 0 } missing)
        {
            FileOps.DeleteFile(output);
            throw new BuildException("ovrport's APK lacks files whose names differ only in case",
                string.Join('\n', missing)) { Code = "universal-convert" };
        }

        var state = context.State;
        state.LastApk = output;
        state.LastApkHeadset = "universal";
        context.SaveState();
    }

    /// <summary>What ovrport keeps in its workspace between runs: its signing key (the same one for every APK, so a new
    /// one installs over the old) and the loader releases it downloaded</summary>
    private static void KeepOvrportFiles(string from, string to)
    {
        foreach (var file in Directory.EnumerateFiles(from, "*", SearchOption.AllDirectories))
        {
            var relative = Path.GetRelativePath(from, file);
            if (relative.StartsWith("dec" + Path.DirectorySeparatorChar, StringComparison.Ordinal))
                continue;
            var target = Path.Combine(to, relative);
            if (File.Exists(target) && !relative.EndsWith(".json", StringComparison.OrdinalIgnoreCase))
                continue;
            Directory.CreateDirectory(Path.GetDirectoryName(target)!);
            File.Copy(file, target, overwrite: true);
        }
    }

    /// <summary>Entries of <paramref name="input"/> that have a twin differing only in case and are missing from
    /// <paramref name="output"/> (ovrport keeps every resource; it only adds and replaces)</summary>
    internal static List<string> MissingCaseTwins(string input, string output)
    {
        using var inZip = ZipFile.OpenRead(input);
        using var outZip = ZipFile.OpenRead(output);
        var twins = inZip.Entries.Select(e => e.FullName)
            .GroupBy(n => n, StringComparer.OrdinalIgnoreCase)
            .Where(g => g.Count() > 1)
            .SelectMany(g => g)
            .ToList();
        if (twins.Count == 0)
            return [];
        var present = outZip.Entries.Select(e => e.FullName).ToHashSet(StringComparer.Ordinal);
        return twins.Where(n => !present.Contains(n)).ToList();
    }

    /// <summary>A case-sensitive APFS disk image, mounted for one ovrport run and deleted after it (macOS). It mounts
    /// where macOS puts it (/Volumes/RepoQuest-ovrport, " 1" etc. when taken): hdiutil -mountpoint into a folder on an
    /// external disk failed with "Permission denied" (2026-11-26)</summary>
    private sealed partial class CaseSensitiveVolume : IAsyncDisposable
    {
        private const string VolumeName = "RepoQuest-ovrport";
        private readonly string image;

        private CaseSensitiveVolume(string root, string image)
        {
            Root = root;
            this.image = image;
        }

        public string Root { get; }

        /// <param name="imageBase">The image's path without ".sparseimage"</param>
        public static async Task<CaseSensitiveVolume> MountAsync(string imageBase, CancellationToken ct)
        {
            var image = imageBase + ".sparseimage";
            // A run stopped before it could clean up leaves the volume attached (detach takes its mount point)
            if (Directory.Exists("/Volumes/" + VolumeName))
                await Hdiutil(["detach", "-force", "/Volumes/" + VolumeName], null, ct);
            FileOps.DeleteFile(image);
            // Sparse: it takes only what ovrport writes (the unpacked APK, ~1-2 GB), up to the size given
            await Hdiutil(["create", "-size", "16g", "-type", "SPARSE", "-fs", "Case-sensitive APFS", "-volname",
                VolumeName, imageBase], "Could not create a case-sensitive disk image for ovrport", ct);
            var lines = new List<string>();
            var attach = await ProcessRunner.RunAsync(new ProcessSpec
            {
                FileName = "/usr/bin/hdiutil",
                Arguments = ["attach", "-plist", "-nobrowse", "-noautoopen", image],
                LogPrefix = "[hdiutil] ",
            }, line =>
            {
                lock (lines)
                    lines.Add(line);
            }, ct);
            var mount = MountPointRegex().Match(string.Join('\n', lines));
            if (attach.ExitCode != 0 || !mount.Success)
            {
                FileOps.DeleteFile(image);
                throw new BuildException($"Could not mount the disk image for ovrport (exit code {attach.ExitCode})",
                    attach.TailText);
            }
            return new CaseSensitiveVolume(mount.Groups[1].Value, image);
        }

        public async ValueTask DisposeAsync()
        {
            if ((await Hdiutil(["detach", Root], null, CancellationToken.None)).ExitCode != 0)
                await Hdiutil(["detach", "-force", Root], null, CancellationToken.None);
            FileOps.DeleteFile(image);
        }

        private static async Task<ProcessResult> Hdiutil(string[] arguments, string? failure, CancellationToken ct)
        {
            var spec = new ProcessSpec { FileName = "/usr/bin/hdiutil", Arguments = arguments, LogPrefix = "[hdiutil] " };
            return failure is null
                ? await ProcessRunner.RunAsync(spec, ct: ct)
                : await ProcessRunner.RunCheckedAsync(spec, failure, ct: ct);
        }

        [System.Text.RegularExpressions.GeneratedRegex(@"<key>mount-point</key>\s*<string>([^<]+)</string>")]
        private static partial System.Text.RegularExpressions.Regex MountPointRegex();
    }
}
