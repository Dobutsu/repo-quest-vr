using System;

namespace RepoQuest.Cloud
{
    /// <summary>Routing shared by PC and Android; the RQ1 wire format is independent of the host platform.</summary>
    public static class CloudRoomRouting
    {
        public static CloudJoinCode HostedInvitation(ulong lobbyId, ulong ownerId, Func<string, string> data)
        {
            if (!PhotonAccess.InRoom || !(bool)PhotonAccess.Get(PhotonAccess.Network, "IsMasterClient"))
                throw new InvalidOperationException("Waiting for a hosted Photon room.");
            var room = PhotonAccess.Get(PhotonAccess.Network, "CurrentRoom");
            if (!(bool)PhotonAccess.Get(room, "IsOpen"))
                throw new InvalidOperationException("The game has started. Share a code from the waiting lobby before starting.");
            if (data("HasPassword") != "0" && !string.Equals(data("HasPassword"), "False", StringComparison.OrdinalIgnoreCase))
                throw new InvalidOperationException("Use a lobby without a password for Cloud co-op.");
            if (data("RoomName") != (string)PhotonAccess.Get(room, "Name"))
                throw new InvalidOperationException("Waiting for lobby metadata to match the Photon room.");
            return new CloudJoinCode
            {
                Region = data("Region"), Room = data("RoomName"), Build = data("BuildName"),
                AppVersion = (string)PhotonAccess.Get(PhotonAccess.Client, "AppVersion"),
                RealtimeAppId = (string)PhotonAccess.Get(PhotonAccess.Settings, "AppIdRealtime"),
                VoiceAppId = (string)PhotonAccess.Get(PhotonAccess.Settings, "AppIdVoice"),
                LobbyId = lobbyId, OwnerId = ownerId, Capacity = Convert.ToInt32(PhotonAccess.Get(room, "MaxPlayers")),
            };
        }

        public static void ValidateGuestJoin(CloudJoinCode invitation, string appVersion, string room)
        {
            if (appVersion != invitation.AppVersion)
                throw new InvalidOperationException("Host and guest game/PUN versions differ. Use matching builds.");
            if (room != invitation.Room)
                throw new InvalidOperationException("The game selected a different room. Check the lobby adapter and create a new invitation.");
        }

        public static void PublishHostIdentity(object parameters)
        {
            var options = PhotonAccess.Get(parameters, "RoomOptions");
            if (options != null) PhotonAccess.Set(options, "PublishUserId", true);
        }

        public static void ValidateHostIdentity(CloudJoinCode invitation, string userId)
        {
            // Older PC hosts did not publish UserId. Retain their actor-based monitoring after arrival.
            if (!string.IsNullOrEmpty(userId) && userId != invitation.OwnerId.ToString(System.Globalization.CultureInfo.InvariantCulture))
                throw new InvalidOperationException("The host has left. The session has ended.");
        }

        // Some game/mod versions use JoinOrCreate for guests. A stale invitation must never create a new room.
        public static bool JoinExisting(object client, object parameters) => (bool)client.GetType()
            .GetMethod("OpJoinRoom", new[] { parameters.GetType() }).Invoke(client, new[] { parameters });
    }
}
