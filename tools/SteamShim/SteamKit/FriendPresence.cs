using System.Collections.Generic;
using System.Linq;
using SteamKit2;
using SteamKit2.Internal;

namespace RepoQuest.Steam
{
    /// <summary>A friend as Steam last reported them; LobbyId is what "Join game" in the Steam friends list uses</summary>
    public sealed class FriendInfo
    {
        public FriendInfo(ulong steamId, string name, uint appId, ulong lobbyId)
        {
            SteamId = steamId;
            Name = name;
            AppId = appId;
            LobbyId = lobbyId;
        }

        public ulong SteamId { get; }
        public string Name { get; }
        public uint AppId { get; }
        public ulong LobbyId { get; }
    }

    /// <summary>
    /// Reads CMsgClientPersonaState directly: SteamKit's PersonaStateCallback drops game_lobby_id, the lobby a friend
    /// is in (the same trick as tools/SteamPhotonProbe).
    /// </summary>
    internal sealed class FriendPresence : ClientMsgHandler
    {
        private readonly Dictionary<ulong, FriendInfo> friends = new Dictionary<ulong, FriendInfo>();

        public IReadOnlyList<FriendInfo> Snapshot()
        {
            lock (friends)
                return friends.Values.ToList();
        }

        public string NameOf(ulong steamId)
        {
            lock (friends)
                return friends.TryGetValue(steamId, out var friend) ? friend.Name : "";
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
                    // Partial updates omit fields: keep what we knew unless Steam sent a value (0 included)
                    friends[f.friendid] = new FriendInfo(
                        f.friendid,
                        string.IsNullOrEmpty(f.player_name) ? old?.Name ?? "" : f.player_name,
                        f.ShouldSerializegame_played_app_id() ? f.game_played_app_id : old?.AppId ?? 0,
                        f.ShouldSerializegame_lobby_id() ? f.game_lobby_id : old?.LobbyId ?? 0);
                }
            }
        }
    }
}
