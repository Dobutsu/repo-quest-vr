using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Steamworks.Data;

// The subset of Facepunch.Steamworks 2.x that R.E.P.O. and RepoXR call, with identical signatures (the game's IL
// binds to them by name). Behaviour comes from SteamBackend.Current.

namespace Steamworks
{
    public enum Result
    {
        None = 0,
        OK = 1,
        Fail = 2,
        NoConnection = 3,
        InvalidPassword = 5,
        LoggedInElsewhere = 6,
        InvalidParam = 8,
        Timeout = 16,
        AccessDenied = 15,
        LimitExceeded = 25,
    }

    public struct SteamId
    {
        public ulong Value;

        public uint AccountId => (uint)(Value & 0xFFFFFFFFu);
        public bool IsValid => Value != 0;

        public static implicit operator SteamId(ulong value) => new SteamId { Value = value };
        public static implicit operator ulong(SteamId value) => value.Value;

        public override string ToString() => Value.ToString();
    }

    public struct AppId
    {
        public uint Value;

        public static implicit operator AppId(uint value) => new AppId { Value = value };
        public static implicit operator AppId(int value) => new AppId { Value = (uint)value };
        public static implicit operator uint(AppId value) => value.Value;

        public override string ToString() => Value.ToString();
    }

    public class AuthTicket : IDisposable
    {
        public byte[] Data;
        public uint Handle;

        public void Cancel()
        {
            if (Handle != 0)
                SteamBackend.Current.CancelAuthTicket(Handle);
            Handle = 0;
            Data = null;
        }

        public void Dispose() => Cancel();
    }

    public struct Friend
    {
        public SteamId Id;

        public Friend(SteamId steamid)
        {
            Id = steamid;
        }

        public bool IsMe => Id.Value == SteamClient.SteamId.Value;
        public string Name => SteamBackend.Current.GetPersonaName(Id.Value);

        public override string ToString() => $"{Name} ({Id})";
    }

    public static class SteamClient
    {
        public static bool IsValid { get; private set; }
        public static bool IsLoggedOn => IsValid;
        public static SteamId SteamId => SteamBackend.Current.SteamId;
        public static string Name => SteamBackend.Current.PersonaName;
        public static AppId AppId { get; internal set; }

        public static void Init(uint appid, bool asyncCallbacks = true)
        {
            AppId = appid;
            IsValid = SteamBackend.Current.Initialize(appid);
            if (!IsValid)
                throw new Exception("Steam backend failed to initialize");
        }

        public static void Shutdown()
        {
            if (IsValid)
                SteamBackend.Current.Shutdown();
            IsValid = false;
        }

        public static void RunCallbacks() { }

        public static bool RestartAppIfNecessary(uint appid) => false;
    }

    public static class SteamUser
    {
        public static AuthTicket GetAuthSessionTicket(NetIdentity identity)
        {
            var data = SteamBackend.Current.GetAuthSessionTicket(out var handle);
            return new AuthTicket { Data = data, Handle = handle };
        }
    }

    /// <summary>
    /// As in Facepunch.Steamworks, a game handler that throws must not break whoever raised the event: there callbacks
    /// come from the dispatcher, which catches and reports. Here they are raised inline (OnLobbyEntered from inside
    /// JoinLobbyAsync, for one), so an escaping exception would fault the game's await and silently skip what follows
    /// it. Every handler runs even if an earlier one throws.
    /// </summary>
    public static class Dispatch
    {
        public static Action<Exception> OnException;

        internal static void Raise<T>(Action<T> handlers, T arg)
        {
            if (handlers == null)
                return;
            foreach (Action<T> handler in handlers.GetInvocationList())
            {
                try { handler(arg); }
                catch (Exception e) { Report(e); }
            }
        }

        internal static void Raise<T1, T2>(Action<T1, T2> handlers, T1 arg1, T2 arg2)
        {
            if (handlers == null)
                return;
            foreach (Action<T1, T2> handler in handlers.GetInvocationList())
            {
                try { handler(arg1, arg2); }
                catch (Exception e) { Report(e); }
            }
        }

        private static void Report(Exception e)
        {
            if (OnException != null)
                OnException(e);
            else
                Console.Error.WriteLine(e);
        }
    }

    public static class SteamUtils
    {
        public static bool IsOverlayEnabled => false;
        public static DateTime SteamServerTime => DateTime.UtcNow;
    }

    public static class SteamFriends
    {
        public static event Action<Lobby, SteamId> OnGameLobbyJoinRequested;
        public static event Action<bool> OnGameOverlayActivated;

        /// <summary>
        /// There is no Steam overlay on Quest: the game's overlay requests ("friends" from the main menu's Join friend
        /// button, invites) are forwarded here so the host can show its own UI instead
        /// </summary>
        public static event Action<string> OnOverlayRequested;
        public static event Action<SteamId> OnInviteOverlayRequested;

        public static void OpenOverlay(string type) => Dispatch.Raise(OnOverlayRequested, type);
        public static void OpenUserOverlay(SteamId id, string type) { }
        public static void OpenGameInviteOverlay(SteamId lobby) => Dispatch.Raise(OnInviteOverlayRequested, lobby);
        public static void SetPlayedWith(SteamId steamid) { }

        public static bool SetRichPresence(string key, string value) => SteamBackend.Current.SetRichPresence(key, value);
        public static void ClearRichPresence() => SteamBackend.Current.SetRichPresence(null, null);

        /// <summary>For backends: a friend invited us / we chose "Join game"</summary>
        public static void RaiseGameLobbyJoinRequested(ulong lobbyId, ulong friendId) =>
            Dispatch.Raise(OnGameLobbyJoinRequested, new Lobby(lobbyId), friendId);

        internal static void RaiseOverlayActivated(bool active) => Dispatch.Raise(OnGameOverlayActivated, active);
    }

    public static class SteamMatchmaking
    {
        public static event Action<Lobby> OnLobbyEntered;
        public static event Action<Result, Lobby> OnLobbyCreated;
        public static event Action<Lobby> OnLobbyDataChanged;
        public static event Action<Lobby, Friend> OnLobbyMemberDataChanged;
        public static event Action<Lobby, Friend> OnLobbyMemberJoined;
        public static event Action<Lobby, Friend> OnLobbyMemberLeave;

        public static async Task<Lobby?> CreateLobbyAsync(int maxMembers = 100)
        {
            var id = await SteamBackend.Current.CreateLobby(maxMembers);
            if (id is null)
            {
                Dispatch.Raise(OnLobbyCreated, Result.Fail, default);
                return null;
            }

            var lobby = new Lobby(id.Value);
            Dispatch.Raise(OnLobbyCreated, Result.OK, lobby);
            Dispatch.Raise(OnLobbyEntered, lobby);
            return lobby;
        }

        public static async Task<Lobby?> JoinLobbyAsync(SteamId lobbyId)
        {
            var id = await SteamBackend.Current.JoinLobby(lobbyId);
            if (id is null)
                return null;

            var lobby = new Lobby(id.Value);
            Dispatch.Raise(OnLobbyEntered, lobby);
            return lobby;
        }

        /// <summary>For backends: forward lobby membership and data changes to the game</summary>
        public static void RaiseLobbyDataChanged(ulong lobbyId) => Dispatch.Raise(OnLobbyDataChanged, new Lobby(lobbyId));
        public static void RaiseLobbyMemberJoined(ulong lobbyId, ulong member) => Dispatch.Raise(OnLobbyMemberJoined, new Lobby(lobbyId), new Friend(member));
        public static void RaiseLobbyMemberLeave(ulong lobbyId, ulong member) => Dispatch.Raise(OnLobbyMemberLeave, new Lobby(lobbyId), new Friend(member));
        public static void RaiseLobbyMemberDataChanged(ulong lobbyId, ulong member) => Dispatch.Raise(OnLobbyMemberDataChanged, new Lobby(lobbyId), new Friend(member));
    }
}

namespace Steamworks.Data
{
    public struct NetIdentity
    {
        private SteamId steamId;

        public bool IsSteamId => steamId.IsValid;
        public SteamId SteamId => steamId;

        public static implicit operator NetIdentity(SteamId value) => new NetIdentity { steamId = value };
        public static implicit operator SteamId(NetIdentity value) => value.steamId;

        public override string ToString() => steamId.ToString();
    }

    public struct Lobby
    {
        public SteamId Id { get; internal set; }

        public Lobby(SteamId id)
        {
            Id = id;
        }

        public int MemberCount => SteamBackend.Current.GetLobbyMembers(Id).Count;
        public IEnumerable<Friend> Members => SteamBackend.Current.GetLobbyMembers(Id).Select(m => new Friend(m)).ToList();

        public int MaxMembers
        {
            get => SteamBackend.Current.GetLobbyMemberLimit(Id);
            set => SteamBackend.Current.SetLobbyMemberLimit(Id, value);
        }

        public Friend Owner => new Friend(SteamBackend.Current.GetLobbyOwner(Id));

        public bool IsOwnedBy(SteamId k) => Owner.Id.Value == k.Value;

        public void Leave() => SteamBackend.Current.LeaveLobby(Id);
        public string GetData(string key) => SteamBackend.Current.GetLobbyData(Id, key) ?? "";
        public bool SetData(string key, string value) => SteamBackend.Current.SetLobbyData(Id, key, value);

        public bool SetPublic() => SteamBackend.Current.SetLobbyType(Id, LobbyVisibility.Public);
        public bool SetPrivate() => SteamBackend.Current.SetLobbyType(Id, LobbyVisibility.Private);
        public bool SetFriendsOnly() => SteamBackend.Current.SetLobbyType(Id, LobbyVisibility.FriendsOnly);
        public bool SetJoinable(bool b) => SteamBackend.Current.SetLobbyJoinable(Id, b);
    }
}
