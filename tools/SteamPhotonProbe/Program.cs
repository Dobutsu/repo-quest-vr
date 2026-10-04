using System.Text.Json;
using SteamKit2;
using SteamPhotonProbe;

// Spike 1: can a device without the Steam client (the Quest) join a vanilla PC friend's R.E.P.O. game?
//
//   login   [--force-qr]                         log in via QR, save refresh token to work/steam-session.json
//   friends                                      list friends currently in R.E.P.O. and their lobby ids
//   photon  [--friend <name> | --lobby <id>]     Steam ticket -> Photon auth (REPO AppId, Steam custom auth)
//           [--join-room] [--region eu]          optionally join the friend's Photon room and leave again

const string DefaultPunVersion = "2.52";

var command = args.FirstOrDefault() ?? "photon";
var opts = ParseOptions(args.Skip(1).ToArray());
var workDir = FindWorkDir();
var inventory = JsonDocument.Parse(File.ReadAllText(Path.Combine(workDir, "inventory.json"))).RootElement;

// --no-auth skips Steam entirely: shows whether REPO's Photon app accepts anonymous clients
var anonymous = command == "photon" && opts.ContainsKey("no-auth");

using var steam = new SteamSession(workDir);
if (!anonymous && !await steam.LogOnAsync(opts.ContainsKey("force-qr")))
    return Exit(2, "Steam logon failed");

switch (command)
{
    case "login":
        return Exit(0, "Steam session saved");

    case "friends":
    {
        foreach (var f in await PlayingRepoAsync())
            Console.WriteLine($"{f.Name,-32} steamid={f.SteamId} lobby={(f.LobbyId == 0 ? "-" : f.LobbyId)}");
        return 0;
    }

    case "photon":
    {
        var region = opts.GetValueOrDefault("region", "eu");
        var buildName = inventory.GetProperty("game_version").GetString()!;
        string? roomName = null;

        var lobbyId = anonymous ? new SteamID() : await ResolveLobbyAsync();
        if (lobbyId != 0)
        {
            var data = await steam.Matchmaking.GetLobbyData(SteamSession.RepoAppId, lobbyId);
            var meta = data.Lobby.Metadata;
            Console.WriteLine($"[steam] lobby {lobbyId}: {string.Join(", ", meta.Select(kv => $"{kv.Key}={kv.Value}"))}");

            if (!meta.TryGetValue("RoomName", out roomName) || string.IsNullOrEmpty(roomName))
                return Exit(3, "Lobby has no RoomName (not a R.E.P.O. lobby, or host already left)");

            region = meta.GetValueOrDefault("Region", region);
            buildName = meta.GetValueOrDefault("BuildName", buildName);
            if (meta.GetValueOrDefault("HasPassword") == "1")
                Console.WriteLine("[steam] note: lobby is password protected; REPO checks the password after joining");
        }

        var appId = opts.GetValueOrDefault("app-id") ??
                    inventory.GetProperty("photon_server_settings").GetProperty("guids_in_order")[0].GetString()!;
        var appVersion = $"{buildName}_{opts.GetValueOrDefault("pun-version", DefaultPunVersion)}";

        if (anonymous)
        {
            var anonProbe = new PhotonProbe(null);
            anonProbe.Connect(appId, appVersion, region, 0, "probe", null);
            var anonOutcome = anonProbe.Run(TimeSpan.FromSeconds(30));
            Console.WriteLine($"RESULT (anonymous): {anonOutcome}{(anonProbe.Failure is null ? "" : $" ({anonProbe.Failure})")}");
            return 0;
        }

        var (ticket, hex) = await steam.GetRepoAuthTicketAsync();
        Console.WriteLine($"[steam] auth session ticket for app {SteamSession.RepoAppId}: {ticket.Ticket.Length} bytes");

        using (ticket)
        {
            var probe = new PhotonProbe(opts.ContainsKey("join-room") ? roomName : null);
            if (!probe.Connect(appId, appVersion, region, steam.SteamId!.ConvertToUInt64(), steam.PersonaName, hex))
                return Exit(4, "ConnectUsingSettings refused to start");

            var outcome = probe.Run(TimeSpan.FromSeconds(45));
            Console.WriteLine($"RESULT: {outcome}{(probe.Failure is null ? "" : $" ({probe.Failure})")}");
            return outcome is PhotonProbe.Outcome.Failed or PhotonProbe.Outcome.Pending ? 1 : 0;
        }
    }

    default:
        return Exit(64, $"unknown command '{command}'");
}

async Task<IReadOnlyCollection<PersonaTracker.Friend>> PlayingRepoAsync()
{
    steam.RequestFriendsPresence();
    await Task.Delay(TimeSpan.FromSeconds(4));
    return steam.Friends.Snapshot().Where(f => f.AppId == SteamSession.RepoAppId).OrderBy(f => f.Name).ToList();
}

async Task<SteamID> ResolveLobbyAsync()
{
    if (opts.TryGetValue("lobby", out var raw))
        return new SteamID(ulong.Parse(raw));

    if (!opts.TryGetValue("friend", out var name))
        return new SteamID();

    var friend = (await PlayingRepoAsync())
        .FirstOrDefault(f => f.Name.Contains(name, StringComparison.OrdinalIgnoreCase));

    if (friend is null)
        throw new InvalidOperationException($"No friend matching '{name}' is playing R.E.P.O. right now");
    if (friend.LobbyId == 0)
        throw new InvalidOperationException($"{friend.Name} is playing but not in a joinable lobby");

    return new SteamID(friend.LobbyId);
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

// Walks up from the current directory, then from the probe's own build output (inside the repo), so
// `dotnet run --project <path>` works from any directory
static string FindWorkDir()
{
    foreach (var start in new[] { Environment.CurrentDirectory, AppContext.BaseDirectory })
    {
        for (var dir = new DirectoryInfo(start); dir is not null; dir = dir.Parent)
        {
            var candidate = Path.Combine(dir.FullName, "work");
            if (File.Exists(Path.Combine(candidate, "inventory.json")))
                return candidate;
        }
    }

    throw new DirectoryNotFoundException("work/inventory.json not found; run scripts/inventory.py first");
}

static int Exit(int code, string message)
{
    Console.WriteLine(code == 0 ? message : $"ERROR: {message}");
    return code;
}
