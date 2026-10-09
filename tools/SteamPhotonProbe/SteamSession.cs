using System.Diagnostics;
using System.Text.Json;
using QRCoder;
using SteamKit2;
using SteamKit2.Authentication;
using SteamKit2.Internal;

namespace SteamPhotonProbe;

/// <summary>
/// A logged-on Steam session without the Steam client, which is what the Quest build will have to rely on.
/// </summary>
internal sealed class SteamSession : IDisposable
{
    public const uint RepoAppId = 3241660;

    private readonly SteamClient client = new();
    private readonly CallbackManager manager;
    private readonly string sessionFile;
    private readonly string workDir;
    private string qrFile = "";
    private int qrCount;
    private readonly CancellationTokenSource pumpCts = new();
    private readonly TaskCompletionSource<bool> loggedOn = new(TaskCreationOptions.RunContinuationsAsynchronously);
    private readonly TaskCompletionSource connected = new(TaskCreationOptions.RunContinuationsAsynchronously);
    private Task? pump;

    public SteamID? SteamId { get; private set; }
    public string PersonaName { get; private set; } = "";
    public SteamMatchmaking Matchmaking => client.GetHandler<SteamMatchmaking>()!;

    /// <summary>Friends as last reported by Steam, including the lobby they are in (not exposed by SteamKit's callback)</summary>
    public PersonaTracker Friends { get; } = new();

    public SteamSession(string workDir)
    {
        sessionFile = Path.Combine(workDir, "steam-session.json");
        this.workDir = workDir;

        client.AddHandler(Friends);
        manager = new CallbackManager(client);
        manager.Subscribe<SteamClient.ConnectedCallback>(_ => connected.TrySetResult());
        manager.Subscribe<SteamClient.DisconnectedCallback>(cb =>
        {
            Console.WriteLine($"[steam] disconnected (user initiated: {cb.UserInitiated})");
            connected.TrySetException(new IOException("Disconnected from Steam before connecting"));
            loggedOn.TrySetResult(false);
        });
        manager.Subscribe<SteamUser.LoggedOnCallback>(cb =>
        {
            Console.WriteLine($"[steam] logon result: {cb.Result} / {cb.ExtendedResult}");
            loggedOn.TrySetResult(cb.Result == EResult.OK);
        });
        manager.Subscribe<SteamUser.AccountInfoCallback>(cb => PersonaName = cb.PersonaName);
    }

    public async Task<bool> LogOnAsync(bool forceQr)
    {
        pump = Task.Run(() =>
        {
            while (!pumpCts.IsCancellationRequested)
                manager.RunWaitCallbacks(TimeSpan.FromMilliseconds(200));
        });

        client.Connect();
        await connected.Task;
        Console.WriteLine("[steam] connected to CM");

        var saved = forceQr ? null : LoadSaved();
        string accountName, refreshToken;

        if (saved is not null)
        {
            (accountName, refreshToken) = saved.Value;
            Console.WriteLine($"[steam] reusing saved session for {accountName}");
        }
        else
        {
            var auth = await client.Authentication.BeginAuthSessionViaQRAsync(new AuthSessionDetails
            {
                DeviceFriendlyName = "RepoXR Quest probe",
            });

            auth.ChallengeURLChanged = () => ShowQr(auth.ChallengeURL);
            ShowQr(auth.ChallengeURL);
            Console.WriteLine("[steam] scan the QR code with the Steam mobile app (Steam Guard > Scan QR code)");

            var result = await auth.PollingWaitForResultAsync();
            (accountName, refreshToken) = (result.AccountName, result.RefreshToken);
            Save(accountName, refreshToken);
            TryDelete(qrFile);
        }

        client.GetHandler<SteamUser>()!.LogOn(new SteamUser.LogOnDetails
        {
            Username = accountName,
            AccessToken = refreshToken,
            ShouldRememberPassword = true,
        });

        if (!await loggedOn.Task)
            return false;

        SteamId = client.SteamID;
        client.GetHandler<SteamFriends>()!.SetPersonaState(EPersonaState.Online);
        SetPlayingRepo();

        // AccountInfo arrives right after logon; give it a moment so the nickname matches what REPO would send
        for (var i = 0; i < 20 && PersonaName == ""; i++)
            await Task.Delay(100);

        Console.WriteLine($"[steam] logged on as '{PersonaName}' ({SteamId?.ConvertToUInt64()})");
        return true;
    }

    /// <summary>Same ticket format as REPO's SteamManager.GetSteamAuthTicket: lowercase hex of the session ticket</summary>
    public async Task<(SteamAuthTicket.TicketInfo Ticket, string Hex)> GetRepoAuthTicketAsync()
    {
        var ticket = await client.GetHandler<SteamAuthTicket>()!.GetAuthSessionTicket(RepoAppId);
        return (ticket, Convert.ToHexString(ticket.Ticket).ToLowerInvariant());
    }

    public void RequestFriendsPresence()
    {
        var friends = client.GetHandler<SteamFriends>()!;
        var ids = Enumerable.Range(0, friends.GetFriendCount()).Select(friends.GetFriendByIndex)
            .Where(id => id.IsIndividualAccount).ToList();

        friends.RequestFriendInfo(ids,
            EClientPersonaStateFlag.PlayerName | EClientPersonaStateFlag.Presence |
            EClientPersonaStateFlag.GameExtraInfo | EClientPersonaStateFlag.GameDataBlob);
    }

    private void SetPlayingRepo()
    {
        var msg = new ClientMsgProtobuf<CMsgClientGamesPlayed>(EMsg.ClientGamesPlayed);
        msg.Body.games_played.Add(new CMsgClientGamesPlayed.GamePlayed { game_id = new GameID(RepoAppId) });
        client.Send(msg);
    }

    private void ShowQr(string url)
    {
        using var data = QRCodeGenerator.GenerateQrCode(url, QRCodeGenerator.ECCLevel.L);
        // Steam replaces the code about every 30 s, and Preview does not reload a file it has open: a new file each time
        TryDelete(qrFile);
        qrFile = Path.Combine(workDir, $"steam-login-qr-{++qrCount}.png");
        File.WriteAllBytes(qrFile, new PngByteQRCode(data).GetGraphic(12));
        Console.WriteLine(new AsciiQRCode(data).GetGraphic(1, drawQuietZones: true));
        Console.WriteLine($"[steam] QR also saved to {qrFile}");

        try
        {
            // As one argument: the checkout's path has a space in it
            Process.Start(new ProcessStartInfo("open") { ArgumentList = { qrFile } });
        }
        catch
        {
            // Not on macOS; the ASCII version above is enough
        }
    }

    private (string AccountName, string RefreshToken)? LoadSaved()
    {
        if (!File.Exists(sessionFile))
            return null;

        var doc = JsonDocument.Parse(File.ReadAllText(sessionFile)).RootElement;
        return (doc.GetProperty("AccountName").GetString()!, doc.GetProperty("RefreshToken").GetString()!);
    }

    private void Save(string accountName, string refreshToken)
    {
        File.WriteAllText(sessionFile, JsonSerializer.Serialize(new { AccountName = accountName, RefreshToken = refreshToken }));
        File.SetUnixFileMode(sessionFile, UnixFileMode.UserRead | UnixFileMode.UserWrite);
    }

    private static void TryDelete(string path)
    {
        try { File.Delete(path); } catch { /* best effort */ }
    }

    public void Dispose()
    {
        if (client.IsConnected)
        {
            client.GetHandler<SteamUser>()?.LogOff();
            Thread.Sleep(500);
            client.Disconnect();
        }

        pumpCts.Cancel();
        pump?.Wait(TimeSpan.FromSeconds(2));
    }
}

/// <summary>
/// Reads CMsgClientPersonaState directly because SteamKit's PersonaStateCallback drops game_lobby_id,
/// which is what "Join game" in the Steam friends list uses.
/// </summary>
internal sealed class PersonaTracker : ClientMsgHandler
{
    public sealed record Friend(ulong SteamId, string Name, uint AppId, ulong LobbyId);

    private readonly Dictionary<ulong, Friend> friends = [];

    public IReadOnlyCollection<Friend> Snapshot()
    {
        lock (friends)
            return friends.Values.ToList();
    }

    public override void HandleMsg(IPacketMsg packetMsg)
    {
        if (packetMsg.MsgType != EMsg.ClientPersonaState)
            return;

        var msg = new ClientMsgProtobuf<CMsgClientPersonaState>(packetMsg);
        lock (friends)
        {
            foreach (var f in msg.Body.friends)
            {
                friends.TryGetValue(f.friendid, out var old);
                // Partial updates omit fields; keep what we knew unless Steam sent a value (0 included)
                friends[f.friendid] = new Friend(
                    f.friendid,
                    string.IsNullOrEmpty(f.player_name) ? old?.Name ?? "" : f.player_name,
                    f.ShouldSerializegame_played_app_id() ? f.game_played_app_id : old?.AppId ?? 0,
                    f.ShouldSerializegame_lobby_id() ? f.game_lobby_id : old?.LobbyId ?? 0);
            }
        }
    }
}
