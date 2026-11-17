using System.Net;
using System.Net.Sockets;
using System.Text;

namespace RepoQuest.Builder.Tests;

public sealed class DownloaderTests : IDisposable
{
    private readonly string temp = Directory.CreateTempSubdirectory("repoquest-tests").FullName;

    public void Dispose() => Directory.Delete(temp, recursive: true);

    [Fact]
    public async Task Download_ResumesAfterAConnectionThatStopsSendingData()
    {
        // The first connection sends the headers and half the body, then goes silent without closing (what a
        // stuck proxy or a dropped route looks like); the retry asks for the rest with a Range header
        var body = Encoding.ASCII.GetBytes(new string('x', 1000));
        using var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        var port = ((IPEndPoint)listener.LocalEndpoint).Port;
        var connections = 0;
        var server = Task.Run(async () =>
        {
            var stalled = new List<TcpClient>();
            while (true)
            {
                var client = await listener.AcceptTcpClientAsync();
                connections++;
                var stream = client.GetStream();
                var request = await ReadHeadersAsync(stream);
                if (connections == 1)
                {
                    await stream.WriteAsync(Encoding.ASCII.GetBytes(
                        $"HTTP/1.1 200 OK\r\nContent-Length: {body.Length}\r\n\r\n"));
                    await stream.WriteAsync(body.AsMemory(0, 500));
                    stalled.Add(client);  // kept open, never written to again
                    continue;
                }
                Assert.Contains("Range: bytes=500-", request);
                await stream.WriteAsync(Encoding.ASCII.GetBytes(
                    $"HTTP/1.1 206 Partial Content\r\nContent-Range: bytes 500-999/1000\r\nContent-Length: 500\r\n" +
                    "Connection: close\r\n\r\n"));
                await stream.WriteAsync(body.AsMemory(500));
                client.Close();
                return;
            }
        });

        var previous = Downloader.StallTimeout;
        Downloader.StallTimeout = TimeSpan.FromSeconds(1);
        try
        {
            var target = Path.Combine(temp, "file.bin");
            await Downloader.DownloadAsync($"http://127.0.0.1:{port}/file.bin", target, Hashing.Sha256(Encoding.ASCII.GetString(body)))
                .WaitAsync(TimeSpan.FromSeconds(30));
            Assert.Equal(body, await File.ReadAllBytesAsync(target));
            Assert.Equal(2, connections);
            await server.WaitAsync(TimeSpan.FromSeconds(5));
        }
        finally
        {
            Downloader.StallTimeout = previous;
        }
    }

    private static async Task<string> ReadHeadersAsync(NetworkStream stream)
    {
        var text = new StringBuilder();
        var buffer = new byte[1];
        while (!text.ToString().EndsWith("\r\n\r\n") && await stream.ReadAsync(buffer) == 1)
            text.Append((char)buffer[0]);
        return text.ToString();
    }
}
