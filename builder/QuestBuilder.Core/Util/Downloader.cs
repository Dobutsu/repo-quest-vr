using System.Net;
using System.Net.Http.Headers;

namespace RepoQuest.Builder;

public readonly record struct DownloadProgress(long Done, long? Total);

/// <summary>HTTP downloads that resume after a broken connection and check the SHA-256 pinned in kit.json</summary>
public static class Downloader
{
    public static HttpClient Http { get; } = CreateClient();

    /// <summary>How long a download may go without receiving a byte before it is treated as broken</summary>
    internal static TimeSpan StallTimeout { get; set; } = TimeSpan.FromSeconds(60);

    private static HttpClient CreateClient()
    {
        var client = new HttpClient(new SocketsHttpHandler
        {
            ConnectTimeout = TimeSpan.FromSeconds(30),
            AutomaticDecompression = DecompressionMethods.None,
            AllowAutoRedirect = true,
        })
        {
            Timeout = Timeout.InfiniteTimeSpan,
        };
        client.DefaultRequestHeaders.UserAgent.Add(new ProductInfoHeaderValue("RepoQuestBuilder", Platform.BuilderVersion));
        return client;
    }

    /// <summary>
    /// Downloads <paramref name="url"/> to <paramref name="target"/> unless a file with the expected hash is already
    /// there. A partial download (.part) is resumed. A file whose SHA-256 does not match is deleted and reported.
    /// </summary>
    public static async Task<string> DownloadAsync(string url, string target, string? sha256,
        IProgress<DownloadProgress>? progress = null, CancellationToken ct = default)
    {
        if (File.Exists(target) && (sha256 is null || Hashing.Sha256File(target) == sha256))
            return target;
        Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(target))!);
        var part = target + ".part";
        Log.Info($"downloading {url}");

        for (var attempt = 1; ; attempt++)
        {
            try
            {
                await DownloadOnceAsync(url, part, progress, ct);
                break;
            }
            catch (Exception e) when (e is HttpRequestException or IOException && attempt < 4 && !ct.IsCancellationRequested)
            {
                Log.Warn($"download interrupted ({e.Message}), retrying ({attempt}/3)");
                await Task.Delay(TimeSpan.FromSeconds(3 * attempt), ct);
            }
            catch (Exception e) when (e is HttpRequestException or IOException)
            {
                throw new BuildException($"Could not download {url}: {e.Message}", inner: e) { Code = "download" };
            }
        }

        if (sha256 is not null)
        {
            var actual = Hashing.Sha256File(part);
            if (actual != sha256)
            {
                File.Delete(part);
                throw new BuildException($"{Path.GetFileName(target)} is not the expected file (SHA-256 {actual}, " +
                                         $"expected {sha256})", url) { Code = "download-hash" };
            }
        }
        File.Move(part, target, overwrite: true);
        return target;
    }

    private static async Task DownloadOnceAsync(string url, string part, IProgress<DownloadProgress>? progress,
        CancellationToken ct)
    {
        // A connection that stops delivering without closing would hang forever (the client has no overall timeout,
        // big files take long): no bytes for StallTimeout counts as a broken connection, which is retried and resumed
        using var stall = CancellationTokenSource.CreateLinkedTokenSource(ct);
        stall.CancelAfter(StallTimeout);
        try
        {
            await DownloadOnceAsync(url, part, progress, stall, ct);
        }
        catch (OperationCanceledException) when (!ct.IsCancellationRequested)
        {
            throw new IOException($"no data for {StallTimeout.TotalSeconds:0} s");
        }
    }

    private static async Task DownloadOnceAsync(string url, string part, IProgress<DownloadProgress>? progress,
        CancellationTokenSource stall, CancellationToken ct)
    {
        var existing = File.Exists(part) ? new FileInfo(part).Length : 0;
        using var request = new HttpRequestMessage(HttpMethod.Get, url);
        if (existing > 0)
            request.Headers.Range = new RangeHeaderValue(existing, null);
        using var response = await Http.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, stall.Token);
        if (response.StatusCode == HttpStatusCode.RequestedRangeNotSatisfiable)
        {
            File.Delete(part);  // the server's file changed or the part is complete garbage: start over
            throw new IOException("range not satisfiable");
        }
        response.EnsureSuccessStatusCode();

        var resumed = response.StatusCode == HttpStatusCode.PartialContent;
        if (!resumed)
            existing = 0;
        var total = response.Content.Headers.ContentLength is { } length ? existing + length : (long?)null;

        await using var source = await response.Content.ReadAsStreamAsync(stall.Token);
        await using var output = new FileStream(part, resumed ? FileMode.Append : FileMode.Create, FileAccess.Write,
            FileShare.None, 1 << 16, useAsync: true);
        var buffer = new byte[1 << 16];
        var done = existing;
        var lastReport = DateTime.MinValue;
        int read;
        while ((read = await source.ReadAsync(buffer, stall.Token)) > 0)
        {
            stall.CancelAfter(StallTimeout);
            await output.WriteAsync(buffer.AsMemory(0, read), ct);
            done += read;
            if (DateTime.UtcNow - lastReport > TimeSpan.FromMilliseconds(250))
            {
                progress?.Report(new DownloadProgress(done, total));
                lastReport = DateTime.UtcNow;
            }
        }
        progress?.Report(new DownloadProgress(done, total));
    }

    public static async Task<string> GetStringAsync(string url, CancellationToken ct = default)
    {
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(ct);
        timeout.CancelAfter(StallTimeout);
        try
        {
            return await Http.GetStringAsync(url, timeout.Token);
        }
        catch (HttpRequestException e)
        {
            throw new BuildException($"Could not download {url}: {e.Message}", inner: e) { Code = "download" };
        }
        catch (OperationCanceledException e) when (!ct.IsCancellationRequested)
        {
            throw new BuildException($"Could not download {url}: no answer for {StallTimeout.TotalSeconds:0} s",
                inner: e) { Code = "download" };
        }
    }
}
