using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using RepoQuest.Cloud;

namespace Steamworks
{
    /// <summary>
    /// Supplies one invitation's lobby metadata to the game's normal join handlers, without a Steam connection.
    /// Photon remains responsible for actual room membership, capacity and gameplay traffic.
    /// </summary>
    public sealed class CloudSteamBackend : ISteamBackend
    {
        private readonly OfflineSteamBackend identity;
        private CloudJoinCode invitation;
        private bool joined;
        private readonly Dictionary<string, string> metadata = new Dictionary<string, string>();
        public ulong HostedLobbyId { get; private set; }
        public bool IsHosting => HostedLobbyId != 0;
        private int capacity;
        public CloudSteamBackend(ulong id, string name)
        {
            if (id == 0) throw new ArgumentOutOfRangeException(nameof(id));
            identity = new OfflineSteamBackend { Id = id, Name = name };
        }

        public void Prepare(CloudJoinCode code) { Clear(); invitation = code ?? throw new ArgumentNullException(nameof(code)); }
        public void Clear() { invitation = null; joined = false; HostedLobbyId = 0; capacity = 0; metadata.Clear(); }
        public bool Initialize(uint appId) => true;
        public void Shutdown() => Clear();
        public ulong SteamId => identity.SteamId;
        public string PersonaName => identity.PersonaName;
        public void SetName(string name) => identity.Name = name;
        public string GetPersonaName(ulong id) => id == SteamId ? PersonaName : id == invitation?.OwnerId ? "Host" : "";
        public byte[] GetAuthSessionTicket(out uint handle) => identity.GetAuthSessionTicket(out handle);
        public void CancelAuthTicket(uint handle) { }
        public Task<ulong?> CreateLobby(int maxMembers)
        {
            if (maxMembers < 2 || maxMembers > 255) return Task.FromResult<ulong?>(null);
            Clear();
            // Local-universe lobby identity, unique per session. No real Steam lobby is created.
            HostedLobbyId = 0x0280000000000000UL | (BitConverter.ToUInt64(Guid.NewGuid().ToByteArray(), 0) & 0x000FFFFFFFFFFFFFUL);
            capacity = maxMembers;
            joined = true;
            return Task.FromResult<ulong?>(HostedLobbyId);
        }
        public Task<ulong?> JoinLobby(ulong lobbyId)
        {
            joined = invitation != null && invitation.LobbyId == lobbyId;
            return Task.FromResult<ulong?>(joined ? lobbyId : (ulong?)null);
        }
        public void LeaveLobby(ulong id)
        {
            if (IsHosting && HostedLobbyId == id) Clear();
            else if (invitation?.LobbyId == id) joined = false;
        }
        public string GetLobbyData(ulong id, string key)
        {
            if (IsHosting && id == HostedLobbyId) return metadata.TryGetValue(key, out var value) ? value : "";
            if (invitation == null || invitation.LobbyId != id) return "";
            switch (key)
            {
                case "RoomName": return invitation.Room;
                case "Region": return invitation.Region;
                case "BuildName": return invitation.Build;
                case "HasPassword": return "0";
                default: return "";
            }
        }
        public bool SetLobbyData(ulong id, string key, string value)
        {
            if (!IsHosting || id != HostedLobbyId) return false;
            metadata[key] = value ?? "";
            return true;
        }
        public bool SetLobbyType(ulong id, LobbyVisibility visibility) => IsHosting && id == HostedLobbyId;
        public bool SetLobbyJoinable(ulong id, bool value) => IsHosting && id == HostedLobbyId;
        public ulong GetLobbyOwner(ulong id) => IsHosting && id == HostedLobbyId ? SteamId : invitation?.LobbyId == id ? invitation.OwnerId : 0;
        public IReadOnlyList<ulong> GetLobbyMembers(ulong id) => IsHosting && id == HostedLobbyId ? new[] { SteamId } : invitation?.LobbyId == id && joined
            ? new[] { invitation.OwnerId, SteamId } : Array.Empty<ulong>();
        public int GetLobbyMemberLimit(ulong id) => IsHosting && id == HostedLobbyId ? capacity : invitation?.LobbyId == id ? invitation.Capacity : 0;
        public void SetLobbyMemberLimit(ulong id, int limit)
        {
            if (IsHosting && id == HostedLobbyId && limit >= 2 && limit <= 255) capacity = limit;
        }
        public bool SetRichPresence(string key, string value) => true;
    }
}
