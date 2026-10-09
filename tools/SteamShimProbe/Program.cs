using System.Text.Json;
using RepoQuest.Steam;
using SteamKit2;
using SteamKit2.Internal;
using SteamPhotonProbe;
using ShimClient = Steamworks.SteamClient;
using ShimFriends = Steamworks.SteamFriends;
using ShimMatchmaking = Steamworks.SteamMatchmaking;
using ShimUser = Steamworks.SteamUser;

// Stage 0 of multiplayer on Quest: the APK's Steam shim with its SteamKit2 backend, used like R.E.P.O. uses it.
// Needs a saved QR login (tools/SteamPhotonProbe login) in work/steam-session.json.
//
//   status                                 log on, print identity and friends playing R.E.P.O.
//   ticket                                 auth session ticket -> Photon master server (Steam custom auth)
//   join   --friend <name> [--room]        "Join game" on a friend: Steam lobby -> lobby data -> Photon
//                                          (--room also joins the friend's Photon room, then leaves)
//   listen [--minutes 5]                   stay online and print incoming messages (to see what an invite looks like)
//
// The headset's own login page, from the Mac (a separate session file, so work/steam-session.json stays as it is):
//   login  [--account <name>]              account name + password (asked on the console, or the password from
//                                          stdin), Steam Guard on the console; saves work/steam-session-password.json
//                                          and logs on with it the way the game does after a login
//   logout                                 log on with that file, revoke its token (the game's LOG OUT), clear it,
//                                          then check that Steam refuses the old token
//
// Steam Cloud (the files the Steam client syncs for R.E.P.O., as the headset's cosmetics sync reads and writes them):
//   cloud-list                             the app's cloud files: name, size, SHA-1, time
//   cloud-get --name <cloud name> [--out <file>]   download one (default: into work/cloud/)
//   cloud-put --name <cloud name> --file <file> --yes   replace it in the cloud (the PC downloads it at its next
//                                          game start); without --yes only prints what it would do

const uint RepoAppId = 3241660;
const string PunVersion = "2.52";

var command = args.FirstOrDefault() ?? "status";
var opts = ParseOptions(args.Skip(1).ToArray());
var workDir = FindWorkDir();
var inventory = JsonDocument.Parse(File.ReadAllText(Path.Combine(workDir, "inventory.json"))).RootElement;

SteamKitBackend.Log = message => Console.WriteLine($"{DateTime.Now:HH:mm:ss.fff} {message}");
var passwordSession = Path.Combine(workDir, "steam-session-password.json");
if (command == "login")
    return await PasswordLoginAsync();
if (command == "logout")
    return await LogOutAsync();

var backend = new SteamKitBackend(Path.Combine(workDir, "steam-session.json"));
if (!backend.HasLogin)
    return Exit(2, "no saved Steam login: run tools/SteamPhotonProbe login first");
if (command == "listen")
    backend.AddHandler(new MessageTrace());

Steamworks.SteamBackend.Current = backend;
ShimClient.Init(RepoAppId);
Console.WriteLine($"[game] SteamClient.IsValid={ShimClient.IsValid} SteamId={ShimClient.SteamId} Name='{ShimClient.Name}' logged on={backend.IsLoggedOn}");
if (!backend.IsLoggedOn)
    return Exit(3, backend.LoginRejected ? "Steam refused the saved login" : "not logged on");

try
{
    switch (command)
    {
        case "status":
            foreach (var f in await FriendsInGameAsync())
                Console.WriteLine($"  {f.Name,-32} steamid={f.SteamId} lobby={(f.LobbyId == 0 ? "-" : f.LobbyId)}");
            return 0;

        case "ticket":
        {
            var outcome = ConnectPhoton(opts.GetValueOrDefault("region", "eu"), GameVersion(), null);
            return outcome == PhotonProbe.Outcome.ConnectedToMaster ? 0 : 1;
        }

        case "join":
            return await JoinFriendAsync();

        case "cloud-list":
            foreach (var file in (await backend.Cloud.ListAsync(RepoAppId)).OrderBy(f => f.Name))
                Console.WriteLine($"  {file.Name,-90} {file.Sha} {DateTimeOffset.FromUnixTimeSeconds((long)file.Time).LocalDateTime:yyyy-MM-dd HH:mm:ss}");
            return 0;

        case "cloud-get":
        {
            if (!opts.TryGetValue("name", out var name))
                return Exit(64, "cloud-get needs --name <cloud name>");
            var file = await backend.Cloud.DownloadAsync(RepoAppId, name);
            if (file is null)
                return Exit(4, $"no {name} in the cloud");
            var output = opts.GetValueOrDefault("out", Path.Combine(workDir, "cloud", Path.GetFileName(name.Replace('\\', '/'))));
            Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(output))!);
            File.WriteAllBytes(output, file.Data);
            Console.WriteLine($"[cloud] {name}: {file.Data.Length} bytes, SHA-1 {file.Sha}, " +
                              $"{DateTimeOffset.FromUnixTimeSeconds((long)file.Time).LocalDateTime:yyyy-MM-dd HH:mm:ss} -> {output}");
            return 0;
        }

        case "cloud-put":
        {
            if (!opts.TryGetValue("name", out var name) || !opts.TryGetValue("file", out var path))
                return Exit(64, "cloud-put needs --name <cloud name> --file <file>");
            var data = File.ReadAllBytes(path);
            var time = (ulong)new DateTimeOffset(File.GetLastWriteTimeUtc(path)).ToUnixTimeSeconds();
            Console.WriteLine($"[cloud] {path} ({data.Length} bytes, SHA-1 {CloudFiles.Hex(CloudFiles.Sha1(data))}) -> {name}");
            if (!opts.ContainsKey("yes"))
                return Exit(0, "dry run: add --yes to upload");
            await backend.Cloud.UploadAsync(RepoAppId, name, data, time);
            var check = await backend.Cloud.DownloadAsync(RepoAppId, name);
            Console.WriteLine($"[cloud] uploaded; the cloud now has SHA-1 {check?.Sha ?? "nothing"}");
            return check?.Sha == CloudFiles.Hex(CloudFiles.Sha1(data)) ? 0 : 1;
        }

        case "listen":
        {
            var minutes = double.Parse(opts.GetValueOrDefault("minutes", "5"));
            Console.WriteLine($"[probe] online for {minutes} min; invite this account from Steam now");
            ShimFriends.OnGameLobbyJoinRequested += (lobby, friend) =>
                Console.WriteLine($"[game] OnGameLobbyJoinRequested lobby={lobby.Id} friend={friend}");
            await Task.Delay(TimeSpan.FromMinutes(minutes));
            return 0;
        }

        default:
            return Exit(64, $"unknown command '{command}'");
    }
}
finally
{
    ShimClient.Shutdown();
}

// The game's path from a join request to the Photon room: SteamManager.OnGameLobbyJoinRequested -> JoinSteamLobby
// -> SteamMatchmaking.JoinLobbyAsync -> OnLobbyEntered -> lobby data -> NetworkConnect (ticket, region, RoomName)
async Task<int> JoinFriendAsync()
{
    if (!opts.TryGetValue("friend", out var name))
        return Exit(64, "join needs --friend <name>");

    var friend = (await FriendsInGameAsync()).FirstOrDefault(f => f.Name.Contains(name, StringComparison.OrdinalIgnoreCase));
    if (friend is null)
        return Exit(4, $"no friend matching '{name}' is playing R.E.P.O.");
    if (friend.LobbyId == 0)
        return Exit(4, $"{friend.Name} is playing but not in a joinable lobby");

    var requested = new TaskCompletionSource<Steamworks.Data.Lobby>();
    var entered = new TaskCompletionSource<Steamworks.Data.Lobby>();
    ShimFriends.OnGameLobbyJoinRequested += (lobby, _) => requested.TrySetResult(lobby);
    ShimMatchmaking.OnLobbyEntered += lobby => entered.TrySetResult(lobby);
    ShimMatchmaking.OnLobbyMemberJoined += (lobby, member) => Console.WriteLine($"[game] member joined: {member}");
    ShimMatchmaking.OnLobbyMemberLeave += (lobby, member) => Console.WriteLine($"[game] member left: {member}");

    // What the friends list's "Join game" (or our VR menu) does
    backend.JoinFriend(friend.SteamId);
    var lobbyToJoin = await requested.Task.WaitAsync(TimeSpan.FromSeconds(5));
    Console.WriteLine($"[game] join requested: lobby {lobbyToJoin.Id} of {friend.Name}");

    var joined = await ShimMatchmaking.JoinLobbyAsync(lobbyToJoin.Id);
    if (joined is null)
        return Exit(5, "JoinLobbyAsync returned null");

    var lobby = await entered.Task.WaitAsync(TimeSpan.FromSeconds(5));
    string Data(string key) => lobby.GetData(key);
    Console.WriteLine($"[game] entered lobby {lobby.Id}: owner={lobby.Owner} members={lobby.MemberCount}/{lobby.MaxMembers}");
    Console.WriteLine($"[game]   RoomName={Data("RoomName")} Region={Data("Region")} BuildName={Data("BuildName")} HasPassword={Data("HasPassword")}");
    foreach (var member in lobby.Members)
        Console.WriteLine($"[game]   member {member}");

    var roomName = Data("RoomName");
    if (string.IsNullOrEmpty(roomName))
        return Exit(6, "lobby has no RoomName (host not in a game yet?)");
    if (Data("BuildName") != GameVersion())
        Console.WriteLine($"[game] NOTE: host runs {Data("BuildName")}, this build is {GameVersion()}: Photon will not match");

    var outcome = ConnectPhoton(Data("Region"), Data("BuildName"), opts.ContainsKey("room") ? roomName : null);
    lobby.Leave();
    return outcome is PhotonProbe.Outcome.JoinedRoom or PhotonProbe.Outcome.LeftRoom or PhotonProbe.Outcome.ConnectedToMaster ? 0 : 1;
}

// SteamManager.SendSteamAuthTicket + NetworkConnect: ticket as lowercase hex, UserId = SteamID, AppVersion "<build>_2.52"
PhotonProbe.Outcome ConnectPhoton(string region, string buildName, string? room)
{
    using var ticket = ShimUser.GetAuthSessionTicket(ShimClient.SteamId);
    Console.WriteLine($"[game] auth ticket: {ticket.Data.Length} bytes, handle {ticket.Handle}");
    if (ticket.Data.Length == 0)
    {
        Console.WriteLine("RESULT: no ticket");
        return PhotonProbe.Outcome.Failed;
    }

    var hex = string.Concat(ticket.Data.Select(b => b.ToString("x2")));
    var appId = inventory.GetProperty("photon_server_settings").GetProperty("guids_in_order")[0].GetString()!;
    var probe = new PhotonProbe(room);
    probe.Connect(appId, $"{buildName}_{PunVersion}", region, ShimClient.SteamId, ShimClient.Name, hex);
    var outcome = probe.Run(TimeSpan.FromSeconds(45));
    Console.WriteLine($"RESULT: {outcome}{(probe.Failure is null ? "" : $" ({probe.Failure})")}");
    return outcome;
}

async Task<IReadOnlyList<FriendInfo>> FriendsInGameAsync()
{
    backend.RefreshFriends();
    await Task.Delay(TimeSpan.FromSeconds(4));
    return backend.FriendsInGame();
}

string GameVersion() => inventory.GetProperty("game_version").GetString()!;

// What the headset's login page does (RepoXR.Standalone SteamLoginFlow), with the console as its UI
async Task<int> PasswordLoginAsync()
{
    var store = SessionStore.Load(passwordSession);
    store.EnsureDeviceIds();
    var account = opts.GetValueOrDefault("account") ?? Prompt("Steam account name: ", secret: false);
    var password = Prompt("Password: ", secret: true);

    using var login = PasswordLogin.Start(account, password, store.GuardData, store.MachineId);
    PasswordLogin.Step? shown = null;
    while (login.State is not (PasswordLogin.Step.Succeeded or PasswordLogin.Step.Failed))
    {
        var step = login.State;
        if (step != shown)
        {
            Console.WriteLine($"[login] {step}");
            if (step == PasswordLogin.Step.WaitingForApproval)
                Console.WriteLine(login.ApprovalByEmail
                    ? "[login] confirm the sign-in with the link Steam emailed you"
                    : "[login] approve the sign-in in the Steam app" + (login.Code == PasswordLogin.CodeKind.App ? " (or press Enter to type its code)" : ""));
            shown = step;
        }

        if (step == PasswordLogin.Step.WaitingForCode)
        {
            var label = login.CodeRejected ? "Wrong code, try again: "
                : login.Code == PasswordLogin.CodeKind.Email ? $"Code Steam emailed to ...@{login.EmailDomain}: "
                : "Steam Guard code from the Steam app: ";
            login.SubmitCode(Prompt(label, secret: false));
            await WaitWhileAsync(() => login.State == PasswordLogin.Step.WaitingForCode && !login.CodeRejected);
            shown = null;
        }
        else if (step == PasswordLogin.Step.WaitingForApproval && login.Code == PasswordLogin.CodeKind.App &&
                 !Console.IsInputRedirected && Console.KeyAvailable)
        {
            Console.ReadLine();
            login.PreferCode();
        }

        await Task.Delay(100);
    }

    if (login.State == PasswordLogin.Step.Failed)
        return Exit(2, $"{login.FailureResult}: {login.FailureMessage.Replace("\n\n", " ")}");

    store.SetLogin(login.AccountName, login.RefreshToken, login.SteamId, login.GuardData);
    store.Save();
    if (!OperatingSystem.IsWindows())
        File.SetUnixFileMode(passwordSession, UnixFileMode.UserRead | UnixFileMode.UserWrite);

    // As the game does after a login on the headset: start the backend without blocking, wait for the logon
    var fresh = new SteamKitBackend(passwordSession);
    fresh.Start(RepoAppId);
    await WaitWhileAsync(() => !(fresh.IsLoggedOn && fresh.PersonaKnown) && !fresh.LoginRejected, seconds: 30);
    Console.WriteLine($"[game] logged on: {fresh.IsLoggedOn}, '{fresh.PersonaName}' ({fresh.SteamId})");
    fresh.Shutdown();
    return fresh.IsLoggedOn ? Exit(0, $"login saved to {passwordSession}") : Exit(3, "the new login did not log on");
}

// The game's LOG OUT: revoke the token, clear the login; then Steam must refuse the old token
async Task<int> LogOutAsync()
{
    var session = new SteamKitBackend(passwordSession);
    if (!session.HasLogin)
        return Exit(2, $"no login in {passwordSession}");

    var before = passwordSession + ".before-logout";
    File.Copy(passwordSession, before, overwrite: true);
    try
    {
        session.Start(RepoAppId);
        await WaitWhileAsync(() => !session.IsLoggedOn && !session.LoginRejected, seconds: 30);
        if (!session.IsLoggedOn)
            return Exit(3, "could not log on to sign out");

        await session.LogOutAsync();
        var store = SessionStore.Load(passwordSession);
        store.ClearLogin();
        store.Save();
        Console.WriteLine($"[game] logged out; identity kept: {store.SteamId} '{store.PersonaName}'");

        var check = new SteamKitBackend(before);
        check.Start(RepoAppId);
        await WaitWhileAsync(() => !check.IsLoggedOn && !check.LoginRejected, seconds: 30);
        check.Shutdown();
        return check.LoginRejected ? Exit(0, "Steam refuses the old token now")
            : Exit(4, check.IsLoggedOn ? "the old token still logs on" : "no answer from Steam for the old token");
    }
    finally
    {
        File.Delete(before);
    }
}

static async Task WaitWhileAsync(Func<bool> condition, double seconds = 60)
{
    var until = DateTime.UtcNow.AddSeconds(seconds);
    while (condition() && DateTime.UtcNow < until)
        await Task.Delay(100);
}

static string Prompt(string label, bool secret)
{
    Console.Write(label);
    if (!secret || Console.IsInputRedirected)
    {
        var line = Console.ReadLine() ?? "";
        if (secret)
            Console.WriteLine();
        return line.Trim();
    }

    var text = new System.Text.StringBuilder();
    while (true)
    {
        var key = Console.ReadKey(intercept: true);
        if (key.Key == ConsoleKey.Enter)
            break;
        if (key.Key == ConsoleKey.Backspace)
        {
            if (text.Length > 0)
                text.Length--;
        }
        else if (!char.IsControl(key.KeyChar))
            text.Append(key.KeyChar);
    }

    Console.WriteLine();
    return text.ToString();
}

static Dictionary<string, string> ParseOptions(string[] argv)
{
    var result = new Dictionary<string, string>();
    for (var i = 0; i < argv.Length; i++)
    {
        if (!argv[i].StartsWith("--"))
            continue;
        var key = argv[i][2..];
        var hasValue = i + 1 < argv.Length && !argv[i + 1].StartsWith("--");
        result[key] = hasValue ? argv[++i] : "";
    }
    return result;
}

static string FindWorkDir()
{
    foreach (var start in new[] { Environment.CurrentDirectory, AppContext.BaseDirectory })
        for (var dir = new DirectoryInfo(start); dir is not null; dir = dir.Parent)
        {
            var candidate = Path.Combine(dir.FullName, "work");
            if (File.Exists(Path.Combine(candidate, "inventory.json")))
                return candidate;
        }
    throw new DirectoryNotFoundException("work/inventory.json not found");
}

static int Exit(int code, string message)
{
    Console.WriteLine(code == 0 ? message : $"ERROR: {message}");
    return code;
}

/// <summary>Prints what arrives from Steam, minus the constant noise; friend messages and invites in full</summary>
internal sealed class MessageTrace : ClientMsgHandler
{
    private static readonly HashSet<EMsg> Noise = new() { EMsg.ClientPersonaState, EMsg.Multi, EMsg.ClientHeartBeat, EMsg.ClientServerTimestampResponse };

    public override void HandleMsg(IPacketMsg packetMsg)
    {
        if (Noise.Contains(packetMsg.MsgType))
            return;

        var line = $"{DateTime.Now:HH:mm:ss.fff} [msg] {packetMsg.MsgType} ({packetMsg.GetData().Length} bytes)";
        try
        {
            switch (packetMsg.MsgType)
            {
                case EMsg.ServiceMethod:
                case EMsg.ServiceMethodSendToClient:
                {
                    var header = new ClientMsgProtobuf<CMsgClientHeartBeat>(packetMsg).ProtoHeader;
                    line += $" {header.target_job_name}";
                    if (header.target_job_name?.StartsWith("FriendMessagesClient.IncomingMessage") == true)
                    {
                        var body = new ClientMsgProtobuf<CFriendMessages_IncomingMessage_Notification>(packetMsg).Body;
                        line += $" from={body.steamid_friend} type={body.chat_entry_type} message=\"{body.message}\"";
                    }
                    break;
                }
                case EMsg.ClientFriendMsgIncoming:
                {
                    var body = new ClientMsgProtobuf<CMsgClientFriendMsgIncoming>(packetMsg).Body;
                    line += $" from={body.steamid_from} type={body.chat_entry_type} message=\"{System.Text.Encoding.UTF8.GetString(body.message ?? Array.Empty<byte>()).TrimEnd('\0')}\"";
                    break;
                }
            }
        }
        catch (Exception e)
        {
            line += $" (undecoded: {e.Message})";
        }

        Console.WriteLine(line);
    }
}
