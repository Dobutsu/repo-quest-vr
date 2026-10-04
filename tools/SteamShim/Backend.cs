using System;
using System.Collections.Generic;
using System.Threading.Tasks;

namespace Steamworks
{
    /// <summary>
    /// What the Facepunch-compatible API needs from Steam. The offline backend lets the game run without Steam
    /// (singleplayer); a SteamKit2 backend logs into the player's account for lobbies and Photon auth tickets.
    /// </summary>
    public interface ISteamBackend
    {
        bool Initialize(uint appId);
        void Shutdown();

        ulong SteamId { get; }
        string PersonaName { get; }
        string GetPersonaName(ulong steamId);

        /// <summary>The ticket bytes; <paramref name="handle"/> identifies it for CancelAuthTicket (0: nothing to cancel)</summary>
        byte[] GetAuthSessionTicket(out uint handle);
        void CancelAuthTicket(uint handle);

        Task<ulong?> CreateLobby(int maxMembers);
        Task<ulong?> JoinLobby(ulong lobbyId);
        void LeaveLobby(ulong lobbyId);
        string GetLobbyData(ulong lobbyId, string key);
        bool SetLobbyData(ulong lobbyId, string key, string value);
        bool SetLobbyType(ulong lobbyId, LobbyVisibility visibility);
        bool SetLobbyJoinable(ulong lobbyId, bool joinable);
        ulong GetLobbyOwner(ulong lobbyId);
        IReadOnlyList<ulong> GetLobbyMembers(ulong lobbyId);
        int GetLobbyMemberLimit(ulong lobbyId);
        void SetLobbyMemberLimit(ulong lobbyId, int limit);

        bool SetRichPresence(string key, string value);
    }

    public enum LobbyVisibility
    {
        Private,
        FriendsOnly,
        Public,
    }

    public static class SteamBackend
    {
        private static ISteamBackend current = new OfflineSteamBackend();

        /// <summary>Set before the game calls SteamClient.Init (e.g. from a RuntimeInitializeOnLoadMethod)</summary>
        public static ISteamBackend Current
        {
            get => current;
            set => current = value ?? throw new ArgumentNullException(nameof(value));
        }
    }

    /// <summary>No Steam at all: a local identity, and lobbies that cannot be created or joined</summary>
    public sealed class OfflineSteamBackend : ISteamBackend
    {
        // Individual account id space, local universe; never collides with a real account (universe 1)
        private const ulong LocalSteamId = 0x0240_0001_0000_0001;

        public string Name { get; set; } = "Quest Player";

        /// <summary>
        /// The account last logged in on the device, if any: the game keeps singleplayer upgrades by SteamID, so a
        /// logout must not change it
        /// </summary>
        public ulong Id { get; set; } = LocalSteamId;

        public bool Initialize(uint appId) => true;
        public void Shutdown() { }

        public ulong SteamId => Id;
        public string PersonaName => Name;
        public string GetPersonaName(ulong steamId) => steamId == Id ? Name : "";

        public byte[] GetAuthSessionTicket(out uint handle)
        {
            handle = 0;
            return Array.Empty<byte>();
        }
        public void CancelAuthTicket(uint handle) { }

        public Task<ulong?> CreateLobby(int maxMembers) => Task.FromResult<ulong?>(null);
        public Task<ulong?> JoinLobby(ulong lobbyId) => Task.FromResult<ulong?>(null);
        public void LeaveLobby(ulong lobbyId) { }
        public string GetLobbyData(ulong lobbyId, string key) => "";
        public bool SetLobbyData(ulong lobbyId, string key, string value) => false;
        public bool SetLobbyType(ulong lobbyId, LobbyVisibility visibility) => false;
        public bool SetLobbyJoinable(ulong lobbyId, bool joinable) => false;
        public ulong GetLobbyOwner(ulong lobbyId) => 0;
        public IReadOnlyList<ulong> GetLobbyMembers(ulong lobbyId) => Array.Empty<ulong>();
        public int GetLobbyMemberLimit(ulong lobbyId) => 0;
        public void SetLobbyMemberLimit(ulong lobbyId, int limit) { }

        public bool SetRichPresence(string key, string value) => true;
    }
}
