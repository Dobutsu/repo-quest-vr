using System;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Security.Cryptography;
using System.Threading.Tasks;
using SteamKit2;
using SteamKit2.Internal;

namespace RepoQuest.Steam
{
    /// <summary>A file in the account's Steam Cloud for an app</summary>
    public sealed class CloudFile
    {
        public string Name = "";
        public byte[] Data = Array.Empty<byte>();
        /// <summary>SHA-1 of the file's contents, lowercase hex</summary>
        public string Sha = "";
        /// <summary>When it was written, Unix seconds (Steam's own time stamp of the upload)</summary>
        public ulong Time;
    }

    /// <summary>
    /// The account's Steam Cloud files, read and written like the Steam client does for Auto-Cloud games: the game's
    /// files on the PC are synced by the Steam client (R.E.P.O. has no Steam Cloud code of its own), so the Quest talks
    /// to the same Cloud service. Files are named as Steam lists them for the app (for R.E.P.O. the path under
    /// AppData/LocalLow, with its root, e.g. "%WinAppDataLocalLow%semiwork/Repo/MetaSave.es3").
    ///
    /// Plain unified messages, as in <see cref="PasswordLogin"/>; the contents go over HTTPS to the hosts Steam names.
    /// An upload is a batch of one file, so the Steam client on the PC sees a new change number and downloads it the
    /// next time the game starts there.
    /// </summary>
    public sealed class CloudFiles
    {
        private const string MachineName = "RepoXR Quest";
        private const uint AllPlatforms = 0xFFFFFFFF;

        private static readonly HttpClient Http = new HttpClient { Timeout = TimeSpan.FromSeconds(30) };

        private readonly SteamUnifiedMessages unified;
        private readonly Func<Task<bool>> waitLoggedOn;
        private readonly TimeSpan timeout;
        private readonly ulong clientId;

        internal CloudFiles(SteamUnifiedMessages unified, Func<Task<bool>> waitLoggedOn, TimeSpan timeout)
        {
            this.unified = unified;
            this.waitLoggedOn = waitLoggedOn;
            this.timeout = timeout;
            var id = new byte[8];
            using (var random = RandomNumberGenerator.Create())
                random.GetBytes(id);
            clientId = BitConverter.ToUInt64(id, 0);
        }

        /// <summary>The app's files in the cloud, with their full names (root and path)</summary>
        public async Task<CloudFile[]> ListAsync(uint appId)
        {
            var response = await CallAsync<CCloud_GetAppFileChangelist_Request, CCloud_GetAppFileChangelist_Response>(
                "Cloud.GetAppFileChangelist#1", new CCloud_GetAppFileChangelist_Request { appid = appId });
            return response.files.Select(f => new CloudFile
            {
                Name = (f.path_prefix_index < response.path_prefixes.Count ? response.path_prefixes[(int)f.path_prefix_index] : "") + f.file_name,
                Sha = Hex(f.sha_file),
                Time = f.time_stamp,
            }).ToArray();
        }

        /// <summary>The file's SHA-1 and time without its contents (<see cref="CloudFile.Data"/> empty), or null if none</summary>
        public async Task<CloudFile> StatAsync(uint appId, string name)
        {
            var response = await RequestDownloadAsync(appId, name).ConfigureAwait(false);
            return response == null ? null : new CloudFile { Name = name, Sha = Hex(response.sha_file), Time = response.time_stamp };
        }

        /// <summary>The file's contents, or null if the cloud has no such file</summary>
        public async Task<CloudFile> DownloadAsync(uint appId, string name)
        {
            var response = await RequestDownloadAsync(appId, name).ConfigureAwait(false);
            if (response == null)
                return null;
            if (response.encrypted)
                throw new CloudException($"{name} is stored encrypted, which is not supported");

            var request = new HttpRequestMessage(HttpMethod.Get, (response.use_https ? "https://" : "http://") + response.url_host + response.url_path);
            foreach (var header in response.request_headers)
                request.Headers.TryAddWithoutValidation(header.name, header.value);
            byte[] data;
            using (var http = await Http.SendAsync(request).ConfigureAwait(false))
            {
                if (!http.IsSuccessStatusCode)
                    throw new CloudException($"downloading {name}: HTTP {(int)http.StatusCode}");
                data = await http.Content.ReadAsByteArrayAsync().ConfigureAwait(false);
            }

            // Steam keeps a file zipped when that makes it smaller (file_size is the stored size, raw_file_size the file's)
            if (response.file_size != response.raw_file_size)
                data = Unzip(data);
            var sha = Hex(Sha1(data));
            if (data.Length != response.raw_file_size || sha != Hex(response.sha_file))
                throw new CloudException($"downloading {name}: got {data.Length} bytes, SHA-1 {sha}; Steam lists {response.raw_file_size} bytes, {Hex(response.sha_file)}");
            return new CloudFile { Name = name, Data = data, Sha = sha, Time = response.time_stamp };
        }

        /// <summary>Writes the file to the cloud (replacing it), dated <paramref name="time"/> (Unix seconds)</summary>
        public async Task UploadAsync(uint appId, string name, byte[] data, ulong time)
        {
            var batch = await CallAsync<CCloud_BeginAppUploadBatch_Request, CCloud_BeginAppUploadBatch_Response>(
                "Cloud.BeginAppUploadBatch#1", new CCloud_BeginAppUploadBatch_Request
                {
                    appid = appId,
                    machine_name = MachineName,
                    files_to_upload = { name },
                    client_id = clientId,
                });

            var result = EResult.Fail;
            try
            {
                var sha = Sha1(data);
                var begin = await CallAsync<CCloud_ClientBeginFileUpload_Request, CCloud_ClientBeginFileUpload_Response>(
                    "Cloud.ClientBeginFileUpload#1", new CCloud_ClientBeginFileUpload_Request
                    {
                        appid = appId,
                        file_size = (uint)data.Length,
                        raw_file_size = (uint)data.Length,
                        file_sha = sha,
                        time_stamp = time,
                        filename = name,
                        platforms_to_sync = AllPlatforms,
                        can_encrypt = false,
                        upload_batch_id = batch.batch_id,
                    }, EResult.DuplicateRequest);

                // DuplicateRequest: the cloud already has exactly this file
                if (begin != null)
                {
                    var sent = true;
                    foreach (var block in begin.block_requests)
                        sent &= await PutBlockAsync(name, block, data).ConfigureAwait(false);

                    var commit = await CallAsync<CCloud_ClientCommitFileUpload_Request, CCloud_ClientCommitFileUpload_Response>(
                        "Cloud.ClientCommitFileUpload#1", new CCloud_ClientCommitFileUpload_Request
                        {
                            transfer_succeeded = sent,
                            appid = appId,
                            file_sha = sha,
                            filename = name,
                        });
                    if (!sent || !commit.file_committed)
                        throw new CloudException($"uploading {name}: {(sent ? "Steam did not commit the file" : "a block failed to upload")}");
                }
                result = EResult.OK;
            }
            finally
            {
                await CallAsync<CCloud_CompleteAppUploadBatch_Request, CCloud_CompleteAppUploadBatch_Response>(
                    "Cloud.CompleteAppUploadBatchBlocking#1", new CCloud_CompleteAppUploadBatch_Request
                    {
                        appid = appId,
                        batch_id = batch.batch_id,
                        batch_eresult = (uint)result,
                    }).ConfigureAwait(false);
            }
        }

        /// <summary>Where and what the file is (null: no such file, or deleted)</summary>
        private async Task<CCloud_ClientFileDownload_Response> RequestDownloadAsync(uint appId, string name)
        {
            var response = await CallAsync<CCloud_ClientFileDownload_Request, CCloud_ClientFileDownload_Response>(
                "Cloud.ClientFileDownload#1", new CCloud_ClientFileDownload_Request { appid = appId, filename = name },
                EResult.FileNotFound).ConfigureAwait(false);
            return response == null || response.is_explicit_delete || string.IsNullOrEmpty(response.url_host) ? null : response;
        }

        private static async Task<bool> PutBlockAsync(string name, ClientCloudFileUploadBlockDetails block, byte[] data)
        {
            // EHTTPMethod: 1 GET, 2 HEAD, 3 POST, 4 PUT, 5 DELETE
            var method = block.http_method == 3 ? HttpMethod.Post : HttpMethod.Put;
            var body = block.explicit_body_data != null && block.explicit_body_data.Length > 0
                ? block.explicit_body_data
                : data.Skip((int)block.block_offset).Take((int)block.block_length).ToArray();
            var request = new HttpRequestMessage(method, (block.use_https ? "https://" : "http://") + block.url_host + block.url_path)
            {
                Content = new ByteArrayContent(body),
            };
            foreach (var header in block.request_headers)
                if (!request.Headers.TryAddWithoutValidation(header.name, header.value))
                    request.Content.Headers.TryAddWithoutValidation(header.name, header.value);

            using (var http = await Http.SendAsync(request).ConfigureAwait(false))
            {
                if (http.IsSuccessStatusCode)
                    return true;
                SteamKitBackend.Log($"[Steam] cloud: uploading {name}: HTTP {(int)http.StatusCode} from {block.url_host}");
                return false;
            }
        }

        /// <summary>
        /// One Cloud service call. <paramref name="tolerated"/>: a result that is an answer rather than a failure
        /// (the call then returns null)
        /// </summary>
        private async Task<TResponse> CallAsync<TRequest, TResponse>(string method, TRequest request, EResult tolerated = EResult.OK)
            where TRequest : ProtoBuf.IExtensible, new()
            where TResponse : ProtoBuf.IExtensible, new()
        {
            if (!await waitLoggedOn().ConfigureAwait(false))
                throw new CloudException($"{method}: not logged on to Steam");

            var call = unified.SendMessage(method, request).ToTask();
            if (await Task.WhenAny(call, Task.Delay(timeout)).ConfigureAwait(false) != call)
                throw new CloudException($"{method}: no answer from Steam");
            var response = await call.ConfigureAwait(false);
            if (response.Result == tolerated && tolerated != EResult.OK)
                return default;
            if (response.Result != EResult.OK)
                throw new CloudException($"{method}: {response.Result}");
            return response.GetDeserializedResponse<TResponse>();
        }

        private static byte[] Unzip(byte[] data)
        {
            using (var zip = new System.IO.Compression.ZipArchive(new MemoryStream(data)))
            using (var entry = zip.Entries.Single().Open())
            using (var output = new MemoryStream())
            {
                entry.CopyTo(output);
                return output.ToArray();
            }
        }

        public static byte[] Sha1(byte[] data)
        {
            using (var sha = SHA1.Create())
                return sha.ComputeHash(data);
        }

        public static string Hex(byte[] bytes) =>
            bytes == null ? "" : BitConverter.ToString(bytes).Replace("-", "").ToLowerInvariant();
    }

    public sealed class CloudException : Exception
    {
        public CloudException(string message) : base(message)
        {
        }
    }
}
