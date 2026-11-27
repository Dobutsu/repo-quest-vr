using System.Text.Json;
using RepoQuest.Builder.Pipeline;

namespace RepoQuest.Builder.Setup;

public static class UniversalPreparation
{
    public const string StepId = "universal-setup";
    public const string DefaultVersion = "3.3.0";

    public static async Task EnsureAsync(Workspace workspace, Kit kit, IBuildProgress progress, CancellationToken ct)
    {
        workspace.Create();
        var stage = "java";
        var thirdParty = new ThirdParty(workspace, kit);
        thirdParty.Progress = new Progress<(string Name, DownloadProgress Progress)>(p =>
        {
            var total = p.Progress.Total;
            if (total is > 0)
                progress.StepProgress(StepId, (double)p.Progress.Done / total.Value, detail:
                    $"{p.Name} {p.Progress.Done >> 20}/{total.Value >> 20} MB");
        });

        progress.StepStarted(StepId);
        try
        {
            ct.ThrowIfCancellationRequested();
            progress.StepProgress(StepId, null, stage);
            var java = await thirdParty.JavaAsync(ct);
            await ProcessRunner.RunCheckedAsync(new ProcessSpec
            {
                FileName = java, Arguments = ["-version"], LogPrefix = "[ovrport setup] ",
            }, "Java could not start", ct: ct);

            stage = "ovrport";
            progress.StepProgress(StepId, null, stage);
            var jar = await thirdParty.OvrportJarAsync(ct);

            stage = "openxr";
            progress.StepProgress(StepId, null, stage);
            var version = DefaultVersion;
            if (!LibrariesReady(workspace, version))
                await InstallAsync(workspace, java, jar, ct);
            if (!LibrariesReady(workspace, version))
                throw new BuildException($"OpenXR {version} is missing or incomplete.");

            ct.ThrowIfCancellationRequested();
            progress.StepFinished(StepId, StepStatus.Done);
        }
        catch (OperationCanceledException)
        {
            progress.StepFinished(StepId, StepStatus.Cancelled);
            throw;
        }
        catch (Exception e)
        {
            progress.StepFinished(StepId, StepStatus.Failed);
            throw new BuildException($"Could not prepare {stage} for the Quest and Pico APK.",
                $"{e.Message}\n{(e as BuildException)?.Details}\n{Path.Combine(workspace.Logs, "builder.log")}", e)
                { Code = $"universal-setup-{stage}" };
        }
    }

    private static async Task InstallAsync(Workspace workspace, string java, string jar, CancellationToken ct)
    {
        Directory.CreateDirectory(workspace.Ovrport);
        // ovrport treats any existing version directory as installed, even after an incomplete extraction.
        FileOps.DeleteDirectory(Path.Combine(workspace.Ovrport, "libraries", DefaultVersion));
        var result = await ProcessRunner.RunAsync(new ProcessSpec
        {
            FileName = java,
            Arguments = ["-jar", jar, "install", $"--workspace={workspace.Ovrport}", $"--version={DefaultVersion}"],
            LogPrefix = "[ovrport setup] ",
        }, ct: ct);
        if (result.ExitCode != 0 || !LibrariesReady(workspace, DefaultVersion))
            throw new BuildException($"ovrport could not install OpenXR {DefaultVersion}.", result.TailText);
    }

    public static bool LibrariesReady(Workspace workspace, string version)
    {
        var index = Path.Combine(workspace.Ovrport, "installed.json");
        if (!File.Exists(index))
            return false;

        try
        {
            using var installed = JsonDocument.Parse(File.ReadAllText(index));
            return installed.RootElement.TryGetProperty("releases", out var releases) &&
                releases.EnumerateArray().Any(release =>
                    release.TryGetProperty("version", out var value) && value.GetString() == version) &&
                LoaderFilesExist(workspace, version);
        }
        catch (Exception e) when (e is JsonException or InvalidOperationException)
        {
            return false;
        }
    }

    internal static bool LoaderFilesExist(Workspace workspace, string version)
    {
        var lib = Path.Combine(workspace.Ovrport, "libraries", version, "lib", "arm64-v8a");
        return new[] { "libopenxr_loader.so", "libopenxr_loader_meta.so", "libopenxr_loader_pico.so" }
            .All(name => new FileInfo(Path.Combine(lib, name)) is { Exists: true, Length: > 0 });
    }
}
