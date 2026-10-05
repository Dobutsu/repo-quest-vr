using System.Net;
using System.Net.Sockets;
using System.Text.RegularExpressions;

namespace RepoQuest.Builder.Pipeline;

/// <summary>
/// Exports the game into a Unity project with AssetRipper (the free GUI build, driven headless through its local web
/// API: settings, load the game folder, export). Both commands answer only when they are finished.
/// </summary>
public sealed partial class ExportStep : BuildStep
{
    public override string Id => "export";

    /// <summary>
    /// The export settings our project has always been made with (work/assetripper.log of the first export). Boolean
    /// settings left out of the form are switched off, as they were then (EnableStaticMeshSeparation,
    /// PreferOriginalTextureExtension, ...).
    /// </summary>
    private static readonly (string Key, string Value)[] Settings =
    [
        ("ScriptContentLevel", "Level2"),
        ("StreamingAssetsMode", "Extract"),
        ("BundledAssetsExportMode", "DirectExport"),
        ("AudioExportFormat", "Default"),
        ("ImageExportFormat", "Png"),
        ("LightmapTextureExportFormat", "Yaml"),
        ("ScriptExportMode", "DllExportWithoutRenaming"),
        ("ScriptLanguageVersion", "AutoSafe"),
        ("ShaderExportMode", "Decompile"),
        ("SpriteExportMode", "Yaml"),
        ("TextExportMode", "Parse"),
    ];

    public override string Stamp(BuildContext context, IReadOnlyDictionary<string, string> earlierStamps) =>
        Hashing.Combine(Id, context.Game.DataFingerprint(), context.Kit.Manifest.Downloads.AssetRipper.Version,
            string.Join(';', Settings.Select(s => $"{s.Key}={s.Value}")));

    public override bool ResultExists(BuildContext context) =>
        File.Exists(Path.Combine(context.Workspace.ExportedProject, "ProjectSettings", "ProjectSettings.asset"));

    public override async Task RunAsync(BuildContext context, CancellationToken ct)
    {
        var workspace = context.Workspace;
        Report(context, null, "download");
        var assetRipper = await context.ThirdParty.AssetRipperAsync(ct);

        ProcessRunner.KillLeftovers(assetRipper);
        // AssetRipper asks before deleting a non-empty export folder; ours is always replaced
        FileOps.DeleteDirectory(workspace.Export);
        var port = FreePort();
        var log = Path.Combine(workspace.Logs, "assetripper.log");
        FileOps.DeleteFile(log);

        var listening = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        using var stop = CancellationTokenSource.CreateLinkedTokenSource(ct);
        Report(context, 0, "load");
        var server = ProcessRunner.RunAsync(new ProcessSpec
        {
            FileName = assetRipper,
            Arguments = ["--headless", "--port", port.ToString(), "--log", "--log-path", log],
            WorkingDirectory = Path.GetDirectoryName(assetRipper),
            LogPrefix = "[assetripper] ",
        }, line =>
        {
            // Not the banner that announces this line ("... a line will appear: \"Now listening on:\" ...")
            if (line.Contains("Now listening on: http", StringComparison.Ordinal))
                listening.TrySetResult();
            if (ExportProgressRegex().Match(line) is { Success: true } m)
                Report(context, double.Parse(m.Groups[1].Value) / double.Parse(m.Groups[2].Value), "export",
                    $"{m.Groups[1].Value}/{m.Groups[2].Value}");
        }, stop.Token);

        try
        {
            var started = await Task.WhenAny(listening.Task, server, Task.Delay(TimeSpan.FromMinutes(2), ct));
            if (started != listening.Task)
            {
                ct.ThrowIfCancellationRequested();
                throw new BuildException("AssetRipper did not start", Log.FilePath) { Code = "assetripper-start" };
            }

            using var http = new HttpClient(new SocketsHttpHandler { AllowAutoRedirect = false })
            {
                BaseAddress = new Uri($"http://127.0.0.1:{port}"),
                Timeout = Timeout.InfiniteTimeSpan,
            };
            await PostAsync(http, "/Settings/Update", Settings, ct);
            Log.Info($"AssetRipper: loading {context.Game.Path}");
            await PostAsync(http, "/LoadFolder", [("Path", context.Game.Path)], ct);
            Log.Info($"AssetRipper: exporting to {workspace.Export}");
            await PostAsync(http, "/Export/UnityProject", [("Path", workspace.Export)], ct);
        }
        finally
        {
            await stop.CancelAsync();
            try
            {
                await server;
            }
            catch (OperationCanceledException)
            {
            }
        }

        if (!ResultExists(context) || !Directory.Exists(Path.Combine(workspace.ExportedProject, "Assets", "Plugins")))
            throw new BuildException("AssetRipper did not export the game (no project in the export folder)", log)
                { Code = "export-empty" };
    }

    private static async Task PostAsync(HttpClient http, string path, IEnumerable<(string Key, string Value)> form,
        CancellationToken ct)
    {
        using var content = new FormUrlEncodedContent(form.Select(f => new KeyValuePair<string, string>(f.Key, f.Value)));
        HttpResponseMessage response;
        try
        {
            response = await http.PostAsync(path, content, ct);
        }
        catch (HttpRequestException e)
        {
            throw new BuildException($"AssetRipper stopped responding ({path}: {e.Message})", inner: e) { Code = "assetripper" };
        }
        using var _ = response;
        if (response.StatusCode is not (HttpStatusCode.Redirect or HttpStatusCode.OK or HttpStatusCode.SeeOther))
            throw new BuildException($"AssetRipper refused {path}: {(int)response.StatusCode} {response.ReasonPhrase}",
                await response.Content.ReadAsStringAsync(ct)) { Code = "assetripper" };
    }

    private static int FreePort()
    {
        var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        var port = ((IPEndPoint)listener.LocalEndpoint).Port;
        listener.Stop();
        return port;
    }

    [GeneratedRegex(@"ExportProgress : \((\d+)/(\d+)\)")]
    private static partial Regex ExportProgressRegex();
}
