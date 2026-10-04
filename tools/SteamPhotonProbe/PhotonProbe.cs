using ExitGames.Client.Photon;
using Photon.Realtime;

namespace SteamPhotonProbe;

/// <summary>
/// Connects to Photon exactly like REPO's NetworkConnect does (Steam custom auth, alternative UDP ports),
/// using the game's own PhotonRealtime/Photon3Unity3D assemblies.
/// </summary>
internal sealed class PhotonProbe : IConnectionCallbacks, IMatchmakingCallbacks
{
    public enum Outcome { Pending, ConnectedToMaster, JoinedRoom, LeftRoom, Failed }

    private readonly LoadBalancingClient client = new(ConnectionProtocol.Udp);
    private readonly string? roomToJoin;

    public Outcome State { get; private set; } = Outcome.Pending;
    public string? Failure { get; private set; }

    // Set once the probe has its answer and hangs up itself; that disconnect is not a failure
    private bool closing;

    public PhotonProbe(string? roomToJoin)
    {
        this.roomToJoin = roomToJoin;
        client.AddCallbackTarget(this);
        client.StateChanged += (from, to) => Console.WriteLine($"[photon] {from} -> {to}");
    }

    public bool Connect(string appId, string appVersion, string region, ulong steamId, string nickName, string? ticketHex)
    {
        client.NickName = nickName;
        if (ticketHex is not null)
        {
            client.AuthValues = new AuthenticationValues
            {
                UserId = steamId.ToString(),
                AuthType = CustomAuthenticationType.Steam,
            };
            client.AuthValues.AddAuthParameter("ticket", ticketHex);
        }

        // DataDirector.PhotonSetAppId
        client.ServerPortOverrides = PhotonPortDefinition.AlternativeUdpPorts;

        var settings = new AppSettings
        {
            AppIdRealtime = appId,
            AppVersion = appVersion,
            FixedRegion = region,
            UseNameServer = true,
            Protocol = ConnectionProtocol.Udp,
        };

        Console.WriteLine($"[photon] connecting: region={region} appVersion={appVersion} userId={steamId}");
        return client.ConnectUsingSettings(settings);
    }

    /// <summary>Drives the client until the probe reaches a terminal state or times out</summary>
    public Outcome Run(TimeSpan timeout)
    {
        var deadline = DateTime.UtcNow + timeout;
        while (DateTime.UtcNow < deadline && State is not (Outcome.Failed or Outcome.LeftRoom))
        {
            client.Service();
            Thread.Sleep(30);

            if (State == Outcome.ConnectedToMaster && roomToJoin is null)
                break;
        }

        if (State == Outcome.Pending)
            Fail($"timed out in state {client.State}");

        var result = State;
        closing = true;
        client.Disconnect();
        for (var i = 0; i < 30 && client.IsConnected; i++)
        {
            client.Service();
            Thread.Sleep(30);
        }

        return result;
    }

    private void Fail(string reason)
    {
        Failure = reason;
        State = Outcome.Failed;
        Console.WriteLine($"[photon] FAILED: {reason}");
    }

    // IConnectionCallbacks

    public void OnConnected() { }

    public void OnConnectedToMaster()
    {
        Console.WriteLine($"[photon] OnConnectedToMaster: authenticated, region={client.CloudRegion}, userId={client.UserId}");
        State = Outcome.ConnectedToMaster;

        if (roomToJoin is not null)
        {
            Console.WriteLine($"[photon] joining room '{roomToJoin}'");
            client.OpJoinRoom(new EnterRoomParams { RoomName = roomToJoin });
        }
    }

    public void OnDisconnected(DisconnectCause cause)
    {
        if (!closing && State is Outcome.Pending or Outcome.ConnectedToMaster)
            Fail($"disconnected: {cause}");
    }

    public void OnRegionListReceived(RegionHandler regionHandler) { }

    public void OnCustomAuthenticationResponse(Dictionary<string, object> data) =>
        Console.WriteLine($"[photon] custom auth response: {string.Join(", ", data.Select(kv => $"{kv.Key}={kv.Value}"))}");

    public void OnCustomAuthenticationFailed(string debugMessage) => Fail($"custom authentication failed: {debugMessage}");

    // IMatchmakingCallbacks

    public void OnJoinedRoom()
    {
        var room = client.CurrentRoom;
        Console.WriteLine($"[photon] OnJoinedRoom: '{room.Name}' players={room.PlayerCount}/{room.MaxPlayers}");
        foreach (var player in room.Players.Values)
            Console.WriteLine($"[photon]   player #{player.ActorNumber} '{player.NickName}'{(player.IsMasterClient ? " (host)" : "")}");

        State = Outcome.JoinedRoom;

        // Leave straight away so the host's game is not left waiting for a client that never loads a level
        client.OpLeaveRoom(false);
    }

    public void OnLeftRoom()
    {
        if (State == Outcome.JoinedRoom)
            State = Outcome.LeftRoom;
    }

    public void OnJoinRoomFailed(short returnCode, string message) => Fail($"join room failed: {returnCode} {message}");
    public void OnCreatedRoom() { }
    public void OnCreateRoomFailed(short returnCode, string message) { }
    public void OnJoinRandomFailed(short returnCode, string message) { }
    public void OnFriendListUpdate(List<FriendInfo> friendList) { }
}
