using System;
using System.Collections.Generic;
using System.Text;
using System.Text.RegularExpressions;
using SteamKit2;
using SteamKit2.Internal;

namespace RepoQuest.Steam
{
    /// <summary>
    /// Game invites from friends. Which way Steam delivers them to a SteamKit session is not pinned down yet, so every
    /// known way is read:
    ///   ClientFriendMsgIncoming (legacy chat) with entry type InviteGame, or a message with invite BBCode
    ///   FriendMessagesClient.IncomingMessage#1 (new chat) with "[lobbyinvite lobbyid=...]" or "+connect_lobby ..."
    ///   ClientChatInvite (what a PC friend's in-game invite arrives as: chat = the lobby, patron = the friend)
    ///   ClientMMSInviteToLobby
    /// The lobby comes from the message when it names one, otherwise it is the friend's current lobby (presence).
    ///
    /// With tracing on (the session's .debug flag) it also logs the name of every incoming message, so a new delivery
    /// path shows up in the log. Ordinary chat text is never logged, only its length.
    /// </summary>
    internal sealed class InviteWatcher : ClientMsgHandler
    {
        private static readonly Regex LobbyInBbCode = new Regex(@"lobbyid\s*=\s*""?(\d+)", RegexOptions.IgnoreCase);
        private static readonly Regex LobbyInConnect = new Regex(@"connect_lobby\s+(\d+)", RegexOptions.IgnoreCase);

        private static readonly HashSet<EMsg> Quiet = new HashSet<EMsg>
        {
            EMsg.Multi, EMsg.ClientPersonaState, EMsg.ClientHeartBeat, EMsg.ServiceMethodResponse,
            EMsg.ClientPICSChangesSinceResponse, EMsg.ClientPICSProductInfoResponse,
        };

        /// <summary>(friend, lobby or 0); raised on SteamKit's network thread</summary>
        public event Action<ulong, ulong> Invited;

        public bool Trace { get; set; }

        /// <summary>Invites to other games are ignored; 0 until the game initializes Steam</summary>
        public uint AppId { get; set; }

        public override void HandleMsg(IPacketMsg packetMsg)
        {
            try
            {
                Handle(packetMsg);
            }
            catch (Exception e)
            {
                SteamKitBackend.Log($"[Steam] reading {packetMsg.MsgType} failed: {e.Message}");
            }
        }

        private void Handle(IPacketMsg packetMsg)
        {
            switch (packetMsg.MsgType)
            {
                case EMsg.ClientFriendMsgIncoming:
                {
                    var msg = new ClientMsgProtobuf<CMsgClientFriendMsgIncoming>(packetMsg).Body;
                    var text = msg.message == null ? "" : Encoding.UTF8.GetString(msg.message).TrimEnd('\0');
                    OnMessage("ClientFriendMsgIncoming", msg.steamid_from, msg.chat_entry_type, text);
                    return;
                }

                case EMsg.ServiceMethod:
                case EMsg.ServiceMethodSendToClient:
                {
                    var job = new ClientMsgProtobuf(packetMsg).ProtoHeader.target_job_name ?? "";
                    if (job.StartsWith("FriendMessagesClient.IncomingMessage", StringComparison.Ordinal))
                    {
                        var msg = new ClientMsgProtobuf<CFriendMessages_IncomingMessage_Notification>(packetMsg).Body;
                        OnMessage(job, msg.steamid_friend, msg.chat_entry_type, msg.message ?? "");
                    }
                    else if (Trace)
                    {
                        SteamKitBackend.Log($"[Steam] <- {packetMsg.MsgType} {job}");
                    }
                    return;
                }

                // What the invite from a friend's game (Steam overlay "Invite") arrives as: the lobby is the "chat"
                case EMsg.ClientChatInvite:
                {
                    var msg = new ClientMsgProtobuf<CMsgClientChatInvite>(packetMsg).Body;
                    SteamKitBackend.Log($"[Steam] ClientChatInvite: from {msg.steam_id_patron}, chat {msg.steam_id_chat}, " +
                                        $"type {msg.chatroom_type}, game {msg.game_id}");
                    if (AppId != 0 && msg.game_id != 0 && (uint)msg.game_id != AppId)
                        return;
                    Invited?.Invoke(msg.steam_id_patron, msg.steam_id_chat);
                    return;
                }

                case EMsg.ClientMMSInviteToLobby:
                {
                    var msg = new ClientMsgProtobuf<CMsgClientMMSInviteToLobby>(packetMsg).Body;
                    SteamKitBackend.Log($"[Steam] ClientMMSInviteToLobby: app {msg.app_id}, lobby {msg.steam_id_lobby}, " +
                                        $"invited {msg.steam_id_user_invited}");
                    // Who invited is not part of this message; the lobby is enough to join
                    Invited?.Invoke(0, msg.steam_id_lobby);
                    return;
                }
            }

            if (Trace && !Quiet.Contains(packetMsg.MsgType))
                SteamKitBackend.Log($"[Steam] <- {packetMsg.MsgType}");
        }

        private void OnMessage(string source, ulong from, int entryType, string text)
        {
            var lobby = FindLobby(text);
            var isInvite = entryType == (int)EChatEntryType.InviteGame || lobby != 0 ||
                           text.IndexOf("invite]", StringComparison.OrdinalIgnoreCase) >= 0 ||
                           text.IndexOf("[gameinvite", StringComparison.OrdinalIgnoreCase) >= 0 ||
                           text.IndexOf("[lobbyinvite", StringComparison.OrdinalIgnoreCase) >= 0;

            if (!isInvite)
            {
                if (Trace)
                    SteamKitBackend.Log($"[Steam] <- {source}: from {from}, entry type {entryType}, {text.Length} chars");
                return;
            }

            SteamKitBackend.Log($"[Steam] invite via {source}: from {from}, entry type {entryType}, lobby {lobby}, text: {text}");
            Invited?.Invoke(from, lobby);
        }

        private static ulong FindLobby(string text)
        {
            var match = LobbyInBbCode.Match(text);
            if (!match.Success)
                match = LobbyInConnect.Match(text);
            return match.Success && ulong.TryParse(match.Groups[1].Value, out var lobby) ? lobby : 0;
        }
    }
}
