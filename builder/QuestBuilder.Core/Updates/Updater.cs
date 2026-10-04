using System.Diagnostics;
using System.IO.Compression;
using System.Reflection;

namespace RepoQuest.Builder.Updates;

/// <summary>
/// Where new versions come from: a URL (the release's download folder, later GitHub Releases) or a local folder
/// (testing without publishing: a folder with latest.json, latest.json.sig and the zips). Chosen by, in this order:
/// --update-source, update-source.txt next to the builder, the source compiled in (RepoQuestUpdateSource).
/// </summary>
public sealed class UpdateSource
{
    private UpdateSource(string location)
    {
        Location = location;
    }

    public string Location { get; }
    private bool IsUrl => Location.StartsWith("http://", StringComparison.OrdinalIgnoreCase) ||
                          Location.StartsWith("https://", StringComparison.OrdinalIgnoreCase);

    public static UpdateSource? Resolve(string? fromArguments)
    {
        var location = fromArguments;
        if (string.IsNullOrWhiteSpace(location))
        {
            var file = Path.Combine(AppContext.BaseDirectory, "update-source.txt");
            var beside = Environment.ProcessPath is { } exe ? Path.Combine(Path.GetDirectoryName(exe)!, "update-source.txt") : null;
            foreach (var candidate in new[] { beside, file }.OfType<string>())
            {
                if (File.Exists(candidate))
                {
                    location = File.ReadAllText(candidate).Trim();
                    break;
                }
            }
        }
        if (string.IsNullOrWhiteSpace(location))
            location = CompiledIn(typeof(UpdateSource).Assembly) ?? CompiledIn(Assembly.GetEntryAssembly());
        return string.IsNullOrWhiteSpace(location) ? null : new UpdateSource(location.TrimEnd('/', '\\'));
    }

    /// <summary>RepoQuestUpdateSource as scripts/release.sh compiles it in: into the window's project (the entry
    /// assembly) when it publishes the window</summary>
    private static string? CompiledIn(Assembly? assembly) =>
        assembly?.GetCustomAttributes<AssemblyMetadataAttribute>()
            .FirstOrDefault(a => a.Key == "RepoQuestUpdateSource")?.Value is { Length: > 0 } value ? value : null;

    /// <summary>The newest release if it is newer than this builder and properly signed; null otherwise</summary>
    public async Task<ReleaseManifest?> CheckAsync(CancellationToken ct)
    {
        var json = await ReadAsync("latest.json", ct);
        var publicKey = ReleaseSigning.EmbeddedPublicKey();
        if (publicKey is not null)
        {
            var signature = System.Text.Encoding.UTF8.GetString(await ReadAsync("latest.json.sig", ct));
            if (!ReleaseSigning.Verify(json, signature, publicKey))
                throw new BuildException("The update is not signed by the port's authors; it was not installed") { Code = "update-signature" };
        }
        else
        {
            Log.Warn("this builder has no release key: updates are not verified (development build)");
        }
        var manifest = ReleaseManifest.Parse(json);
        return ReleaseManifest.IsNewer(manifest.Version, Platform.BuilderVersion) ? manifest : null;
    }

    /// <summary>Downloads (or copies) this platform's builder zip of a release and checks its hash</summary>
    public async Task<string> DownloadAsync(ReleaseManifest manifest, string targetDir, IProgress<DownloadProgress>? progress,
        CancellationToken ct)
    {
        if (!manifest.Builder.TryGetValue(Platform.Rid, out var asset))
            throw new BuildException($"Version {manifest.Version} has no builder for {Platform.Rid}");
        var target = Path.Combine(targetDir, Path.GetFileName(asset.Url));
        if (IsUrl || Uri.TryCreate(asset.Url, UriKind.Absolute, out _))
            return await Downloader.DownloadAsync(UrlOf(asset.Url), target, asset.Sha256, progress, ct);

        FileOps.CopyFile(Path.Combine(Location, asset.Url), target);
        if (Hashing.Sha256File(target) != asset.Sha256)
            throw new BuildException($"{asset.Url} in {Location} is not the file the manifest lists");
        return target;
    }

    private async Task<byte[]> ReadAsync(string name, CancellationToken ct)
    {
        if (!IsUrl)
            return await File.ReadAllBytesAsync(Path.Combine(Location, name), ct);
        try
        {
            return await Downloader.Http.GetByteArrayAsync(UrlOf(name), ct);
        }
        catch (HttpRequestException e)
        {
            throw new BuildException($"Could not check for updates: {e.Message}", inner: e) { Code = "update-offline" };
        }
    }

    private string UrlOf(string relativeOrAbsolute) =>
        Uri.TryCreate(relativeOrAbsolute, UriKind.Absolute, out var absolute) && absolute.Scheme.StartsWith("http")
            ? absolute.ToString()
            : $"{Location}/{relativeOrAbsolute}";
}

/// <summary>
/// Replaces the running builder with a new version: Windows lets a running exe be renamed, not overwritten, so the
/// current one becomes RepoQuestBuilder.old.exe (deleted at the next start) and the new one takes its name.
/// </summary>
public static class SelfUpdater
{
    public static string CurrentExecutable =>
        Environment.ProcessPath ?? throw new InvalidOperationException("no process path");

    /// <summary>Puts the builder from a release zip in place of this one and starts it; the caller then exits</summary>
    public static void ApplyAndRestart(string zipPath, IEnumerable<string> restartArguments)
    {
        var exe = CurrentExecutable;
        var dir = Path.GetDirectoryName(exe)!;
        var name = Path.GetFileName(exe);
        var staged = Path.Combine(dir, name + ".new");
        using (var zip = ZipFile.OpenRead(zipPath))
        {
            var entry = zip.Entries.FirstOrDefault(e => e.Name.Equals(name, StringComparison.OrdinalIgnoreCase))
                        ?? zip.Entries.FirstOrDefault(e => e.Name.EndsWith(Platform.IsWindows ? ".exe" : "Builder", StringComparison.OrdinalIgnoreCase))
                        ?? throw new BuildException("The update has no builder inside");
            entry.ExtractToFile(staged, overwrite: true);
            // Other files of the release (README) are refreshed too
            foreach (var other in zip.Entries.Where(e => e != entry && e.Name.Length > 0 && !e.FullName.Contains('/')))
                other.ExtractToFile(Path.Combine(dir, other.Name), overwrite: true);
        }

        var old = OldPath(exe);
        FileOps.DeleteFile(old);
        File.Move(exe, old);
        File.Move(staged, exe);
        if (!OperatingSystem.IsWindows())
            File.SetUnixFileMode(exe, File.GetUnixFileMode(old));

        var start = new ProcessStartInfo(exe) { UseShellExecute = false, WorkingDirectory = dir };
        foreach (var argument in restartArguments)
            start.ArgumentList.Add(argument);
        Process.Start(start);
    }

    /// <summary>Removes the previous version left by an update (it was still running then)</summary>
    public static void CleanUpPrevious()
    {
        var old = OldPath(CurrentExecutable);
        for (var attempt = 0; attempt < 10 && File.Exists(old); attempt++)
        {
            try
            {
                File.Delete(old);
            }
            catch (Exception e) when (e is IOException or UnauthorizedAccessException)
            {
                Thread.Sleep(500);  // the old process may still be exiting
            }
        }
    }

    private static string OldPath(string exe) =>
        Path.Combine(Path.GetDirectoryName(exe)!, Path.GetFileNameWithoutExtension(exe) + ".old" + Path.GetExtension(exe));
}
