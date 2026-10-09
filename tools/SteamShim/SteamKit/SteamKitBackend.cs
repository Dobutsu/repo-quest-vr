using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using SteamKit2;
using SteamKit2.Internal;
using Shim = Steamworks;

namespace RepoQuest.Steam
{
    /// <summary>
    /// SteamKit asks the OS for machine identifiers at logon (IOKit on macOS, machine-id files on Linux), which is
    /// fragile off the desktop; a random id saved with the session serves the same purpose and stays stable
    /// </summary>
    internal sealed class SessionMachineInfo : IMachineInfoProvider
    {
        private readonly byte[] id;

        public SessionMachineInfo(string machineId)
        {
            id = Guid.ParseExact(machineId, "N").ToByteArray();
        }

        public byte[] GetMachineGuid() => id;
        public byte[] GetMacAddress() => id.Take(6).ToArray();
        public byte[] GetDiskId() => Enumerable.Reverse(id).ToArray();
    }

    /// <summary>
    /// Real Steam for the Quest, without the Steam client: logs into the player's account with SteamKit2 using the
    /// refresh token of an earlier login (<see cref="SessionStore"/>, <see cref="PasswordLogin"/>), shows the player as
    /// playing R.E.P.O., makes auth session tickets for Photon and joins friends' lobbies. Hosting (creating lobbies) is
    /// not supported yet.
    ///
    /// SteamKit callbacks run on a pump thread; everything the game observes (lobby events, join requests) is posted
    /// to the SynchronizationContext of the thread that called <see cref="Initialize"/> (Unity's main thread).
    /// </summary>
    public sealed class SteamKitBackend : Shim.ISteamBackend
    {
        /// <summary>Diagnostics sink; the host sets it (Debug.Log in the game, Console in the probe)</summary>
        public static Action<string> Log = _ => { };

        private static readonly TimeSpan LogOnWait = TimeSpan.FromSeconds(8);
        private static readonly TimeSpan RequestTimeout = TimeSpan.FromSeconds(15);
        private const string MachineName = "RepoXR Quest";

        private readonly SessionStore session;
        private readonly SteamClient client;
        private readonly CallbackManager callbacks;
        private readonly SteamUser user;
        private readonly SteamFriends friends;
        private readonly SteamMatchmaking matchmaking;
        private readonly SteamApps apps;
        private readonly AuthTicketHandler tickets = new AuthTicketHandler();
        private readonly FriendPresence presence = new FriendPresence();
        private readonly InviteWatcher invites = new InviteWatcher();
        private readonly ManualResetEventSlim loggedOn = new ManualResetEventSlim(false);
        private readonly ManualResetEventSlim personaKnown = new ManualResetEventSlim(false);
        private readonly HashSet<ulong> joinedLobbies = new HashSet<ulong>();

        private SynchronizationContext mainThread;
        private Thread pump;
        private volatile bool running;
        private volatile bool loginRejected;
        private volatile bool signingOut;
        private uint appId;
        private byte[] ownershipTicket;
        private int reconnectDelaySeconds = 1;

        public SteamKitBackend(string sessionPath)
        {
            // IL2CPP cannot emit code: make protobuf-net (SteamKit's message serializer) use reflection only. Set here
            // for every platform, so the Mac probe runs the same way the Quest does
            ProtoBuf.Meta.RuntimeTypeModel.Default.AutoCompile = false;

            session = SessionStore.Load(sessionPath);

            // "<session>.debug" next to the session file: SteamKit's own log too (bring-up on new platforms)
            if (System.IO.File.Exists(sessionPath + ".debug"))
            {
                DebugLog.Enabled = true;
                DebugLog.AddListener((category, message) => Log($"[SteamKit] {category}: {message}"));
                invites.Trace = true;
            }

            session.EnsureDeviceIds();
            client = new SteamClient(CreateConfiguration(session.MachineId));
            client.AddHandler(tickets);
            client.AddHandler(presence);
            client.AddHandler(invites);
            invites.Invited += (friend, lobby) => PostToGame(() => Invited?.Invoke(friend, lobby));
            user = client.GetHandler<SteamUser>();
            friends = client.GetHandler<SteamFriends>();
            matchmaking = client.GetHandler<SteamMatchmaking>();
            apps = client.GetHandler<SteamApps>();
            Cloud = new CloudFiles(client.GetHandler<SteamUnifiedMessages>(), WaitLoggedOnAsync, RequestTimeout);

            callbacks = new CallbackManager(client);
            callbacks.Subscribe<SteamClient.ConnectedCallback>(_ => OnConnected());
            callbacks.Subscribe<SteamClient.DisconnectedCallback>(OnDisconnected);
            callbacks.Subscribe<SteamUser.LoggedOnCallback>(OnLoggedOn);
            callbacks.Subscribe<SteamUser.LoggedOffCallback>(cb => Log($"[Steam] logged off: {cb.Result}"));
            callbacks.Subscribe<SteamUser.AccountInfoCallback>(OnAccountInfo);
            callbacks.Subscribe<SteamMatchmaking.LobbyDataCallback>(cb =>
                PostToGame(() => Shim.SteamMatchmaking.RaiseLobbyDataChanged(cb.Lobby.SteamID)));
            callbacks.Subscribe<SteamMatchmaking.UserJoinedLobbyCallback>(cb =>
                PostToGame(() => Shim.SteamMatchmaking.RaiseLobbyMemberJoined(cb.LobbySteamID, cb.User.SteamID)));
            callbacks.Subscribe<SteamMatchmaking.UserLeftLobbyCallback>(cb =>
                PostToGame(() => Shim.SteamMatchmaking.RaiseLobbyMemberLeave(cb.LobbySteamID, cb.User.SteamID)));
        }

        /// <summary>
        /// How this device talks to Steam: TCP, and the session's machine id instead of the OS's (see
        /// <see cref="SessionMachineInfo"/>)
        /// </summary>
        public static SteamConfiguration CreateConfiguration(string machineId) =>
            SteamConfiguration.Create(b => b
                .WithProtocolTypes(ProtocolTypes.Tcp)
                .WithMachineInfoProvider(new SessionMachineInfo(machineId)));

        /// <summary>A friend invited us to their game: (friend or 0, lobby or 0), on the game's thread</summary>
        public event Action<ulong, ulong> Invited;

        /// <summary>The account's Steam Cloud files (calls wait for the logon, like the lobby calls)</summary>
        public CloudFiles Cloud { get; }

        /// <summary>Extra message handler (diagnostics); add before <see cref="Initialize"/></summary>
        public void AddHandler(ClientMsgHandler handler) => client.AddHandler(handler);

        /// <summary>A login was saved earlier; without one the game should use the offline backend</summary>
        public bool HasLogin => session.HasLogin;

        public bool IsLoggedOn => loggedOn.IsSet;

        /// <summary>The saved login was refused by Steam (revoked or expired): a new login is needed</summary>
        public bool LoginRejected => loginRejected;

        /// <summary>The account's persona name has arrived (right after the first logon on this device)</summary>
        public bool PersonaKnown => personaKnown.IsSet || session.PersonaName.Length > 0;

        public bool Initialize(uint appId)
        {
            if (!Start(appId))
                return false;

            // The game reads the SteamID and name right away; both are known from the session, but lobbies and
            // tickets need the connection, so give it a moment (the splash screen is on anyway)
            if (!loggedOn.Wait(LogOnWait))
                Log("[Steam] still logging on; continuing with the saved identity");
            else if (session.PersonaName.Length == 0)
                personaKnown.Wait(TimeSpan.FromSeconds(3)); // first logon on this device: the name follows the logon
            return true;
        }

        /// <summary>
        /// Connects and logs on without waiting for it: for a login made while the game runs (the game already called
        /// SteamClient.Init). Call on the game's main thread, whose events this backend raises there.
        /// </summary>
        public bool Start(uint appId)
        {
            if (!session.HasLogin)
            {
                Log("[Steam] no saved Steam login");
                return false;
            }

            this.appId = appId;
            invites.AppId = appId;
            session.Save();
            mainThread = SynchronizationContext.Current;

            running = true;
            pump = new Thread(Pump) { IsBackground = true, Name = "SteamKit callbacks" };
            pump.Start();
            // Not from this thread: SteamKit's async code does not use ConfigureAwait(false), so starting it on Unity's
            // main thread ties its continuations to that thread, which Initialize blocks while it waits
            Task.Run(() => client.Connect());
            return true;
        }

        /// <summary>
        /// Signs this device out, as the Steam client's Sign out does: Steam revokes the refresh token, then the
        /// connection closes. From here on the session file is left alone; clearing it is the caller's job.
        /// </summary>
        public Task LogOutAsync()
        {
            // Before returning: the caller clears the session file next, and a late persona update must not write the
            // token back
            signingOut = true;
            return Task.Run(async () =>
            {
                if (loggedOn.IsSet)
                {
                    try
                    {
                        var job = client.GetHandler<SteamUnifiedMessages>().SendMessage("Authentication.RevokeToken#1",
                            new CAuthentication_Token_Revoke_Request
                            {
                                token = session.RefreshToken,
                                revoke_action = EAuthTokenRevokeAction.k_EAuthTokenRevokePermanent,
                            });
                        var response = await WithTimeout(job.ToTask()).ConfigureAwait(false);
                        Log($"[Steam] login token revoked: {response.Result}");
                    }
                    catch (Exception e)
                    {
                        Log($"[Steam] revoking the login token failed: {e.GetBaseException().Message}");
                    }
                }
                else
                    Log("[Steam] not connected: the login token is forgotten here, but stays valid until it expires");

                Shutdown();
            });
        }

        public void Shutdown()
        {
            running = false;
            if (client.IsConnected)
            {
                user.LogOff();
                client.Disconnect();
            }

            pump?.Join(TimeSpan.FromSeconds(2));
        }

        public ulong SteamId => client.SteamID?.ConvertToUInt64() ?? session.SteamId;
        public string PersonaName => session.PersonaName;

        public string GetPersonaName(ulong steamId)
        {
            if (steamId == SteamId)
                return PersonaName;

            var name = presence.NameOf(steamId);
            if (name.Length > 0)
                return name;

            foreach (var lobbyId in JoinedLobbies())
            {
                var member = Lobby(lobbyId)?.Members.FirstOrDefault(m => m.SteamID.ConvertToUInt64() == steamId);
                if (member != null)
                    return member.PersonaName ?? "";
            }

            return "";
        }

        // --- Auth tickets (Photon custom authentication) --------------------------------------------------------------

        public byte[] GetAuthSessionTicket(out uint handle)
        {
            handle = 0;
            try
            {
                // ISteamUser::GetAuthSessionTicket is synchronous; all awaits below avoid the calling context
                var task = CreateTicketAsync();
                if (!task.Wait(RequestTimeout))
                {
                    Log("[Steam] auth ticket timed out");
                    return Array.Empty<byte>();
                }

                handle = task.Result.Crc;
                return task.Result.Ticket;
            }
            catch (Exception e)
            {
                Log($"[Steam] auth ticket failed: {e.GetBaseException().Message}");
                return Array.Empty<byte>();
            }
        }

        public void CancelAuthTicket(uint handle)
        {
            if (loggedOn.IsSet)
                tickets.Cancel(appId, handle);
        }

        private async Task<(byte[] Ticket, uint Crc)> CreateTicketAsync()
        {
            if (!await WaitLoggedOnAsync().ConfigureAwait(false))
                throw new InvalidOperationException("not logged on to Steam");

            if (ownershipTicket == null)
            {
                var owned = await apps.GetAppOwnershipTicket(appId).ToTask().ConfigureAwait(false);
                if (owned.Result != EResult.OK)
                    throw new InvalidOperationException($"no ownership ticket for app {appId}: {owned.Result}");
                ownershipTicket = owned.Ticket;
            }

            return await tickets.CreateAsync(appId, ownershipTicket, RequestTimeout).ConfigureAwait(false);
        }

        // --- Lobbies ----------------------------------------------------------------------------------------------------

        public Task<ulong?> CreateLobby(int maxMembers)
        {
            Log("[Steam] hosting from the Quest is not supported yet");
            return Task.FromResult<ulong?>(null);
        }

        public async Task<ulong?> JoinLobby(ulong lobbyId)
        {
            if (!await WaitLoggedOnAsync().ConfigureAwait(false))
                return null;

            try
            {
                var joined = await WithTimeout(matchmaking.JoinLobby(appId, lobbyId).ToTask()).ConfigureAwait(false);
                if (joined.ChatRoomEnterResponse != EChatRoomEnterResponse.Success)
                {
                    Log($"[Steam] joining lobby {lobbyId} failed: {joined.ChatRoomEnterResponse}");
                    return null;
                }

                lock (joinedLobbies)
                    joinedLobbies.Add(lobbyId);
                Log($"[Steam] joined lobby {lobbyId}: {Describe(joined.Lobby)}");
                return lobbyId;
            }
            catch (Exception e)
            {
                Log($"[Steam] joining lobby {lobbyId} failed: {e.GetBaseException().Message}");
                return null;
            }
        }

        public void LeaveLobby(ulong lobbyId)
        {
            bool wasJoined;
            lock (joinedLobbies)
                wasJoined = joinedLobbies.Remove(lobbyId);
            if (wasJoined && loggedOn.IsSet)
                matchmaking.LeaveLobby(appId, lobbyId);
        }

        public string GetLobbyData(ulong lobbyId, string key)
        {
            var lobby = Lobby(lobbyId);
            return lobby != null && lobby.Metadata.TryGetValue(key, out var value) ? value : "";
        }

        public ulong GetLobbyOwner(ulong lobbyId) => Lobby(lobbyId)?.OwnerSteamID?.ConvertToUInt64() ?? 0;

        public IReadOnlyList<ulong> GetLobbyMembers(ulong lobbyId) =>
            Lobby(lobbyId)?.Members.Select(m => m.SteamID.ConvertToUInt64()).ToList() ?? (IReadOnlyList<ulong>)Array.Empty<ulong>();

        public int GetLobbyMemberLimit(ulong lobbyId) => Lobby(lobbyId)?.MaxMembers ?? 0;

        // Only the lobby owner (the host) changes lobby settings; the Quest joins, it does not host
        public bool SetLobbyData(ulong lobbyId, string key, string value) => false;
        public bool SetLobbyType(ulong lobbyId, Shim.LobbyVisibility visibility) => false;
        public bool SetLobbyJoinable(ulong lobbyId, bool joinable) => false;
        public void SetLobbyMemberLimit(ulong lobbyId, int limit) { }

        public bool SetRichPresence(string key, string value) => true;

        // --- Friends ----------------------------------------------------------------------------------------------------

        /// <summary>Asks Steam for friends' presence including the lobby they are in (arrives within seconds)</summary>
        public void RefreshFriends()
        {
            if (!loggedOn.IsSet)
                return;

            var ids = Enumerable.Range(0, friends.GetFriendCount()).Select(friends.GetFriendByIndex)
                .Where(id => id.IsIndividualAccount).ToList();
            friends.RequestFriendInfo(ids,
                EClientPersonaStateFlag.PlayerName | EClientPersonaStateFlag.Presence |
                EClientPersonaStateFlag.GameExtraInfo | EClientPersonaStateFlag.GameDataBlob);
        }

        /// <summary>Friends playing this game, with the lobby they are in (0 if none)</summary>
        public IReadOnlyList<FriendInfo> FriendsInGame() =>
            presence.Snapshot().Where(f => f.AppId == appId && f.SteamId != SteamId).OrderBy(f => f.Name).ToList();

        /// <summary>
        /// What "Join game" in the Steam friends list does: the game's SteamManager then joins the lobby and the
        /// Photon room behind it. Returns false if the friend is not in a lobby.
        /// </summary>
        public bool JoinFriend(ulong friendSteamId)
        {
            var friend = presence.Snapshot().FirstOrDefault(f => f.SteamId == friendSteamId);
            if (friend == null || friend.LobbyId == 0)
                return false;

            PostToGame(() => Shim.SteamFriends.RaiseGameLobbyJoinRequested(friend.LobbyId, friend.SteamId));
            return true;
        }

        /// <summary>
        /// Accepting an invite: the lobby it named, else the inviting friend's current lobby. False if neither is known.
        /// </summary>
        public bool JoinInvite(ulong friendSteamId, ulong lobbyId)
        {
            if (lobbyId == 0)
                return JoinFriend(friendSteamId);

            PostToGame(() => Shim.SteamFriends.RaiseGameLobbyJoinRequested(lobbyId, friendSteamId));
            return true;
        }

        // --- Connection -------------------------------------------------------------------------------------------------

        private void Pump()
        {
            while (running)
            {
                try
                {
                    callbacks.RunWaitCallbacks(TimeSpan.FromMilliseconds(100));
                }
                catch (Exception e)
                {
                    Log($"[Steam] callback failed: {e}");
                }
            }
        }

        private void OnConnected()
        {
            Log("[Steam] connected, logging on");
            user.LogOn(new SteamUser.LogOnDetails
            {
                Username = session.AccountName,
                AccessToken = session.RefreshToken,
                ShouldRememberPassword = true,
                LoginID = session.LoginId,
                MachineName = MachineName,
            });
        }

        private void OnLoggedOn(SteamUser.LoggedOnCallback cb)
        {
            if (cb.Result != EResult.OK)
            {
                Log($"[Steam] logon failed: {cb.Result} / {cb.ExtendedResult}");
                if (cb.Result == EResult.InvalidPassword || cb.Result == EResult.AccessDenied || cb.Result == EResult.Expired ||
                    cb.Result == EResult.Revoked || cb.Result == EResult.InvalidSignature)
                    loginRejected = true;
                return;
            }

            reconnectDelaySeconds = 1;
            friends.SetPersonaState(EPersonaState.Online);
            SendGamesPlayed();
            loggedOn.Set();
            Log($"[Steam] logged on as {client.SteamID?.ConvertToUInt64()}");
            RefreshFriends();
        }

        private void OnAccountInfo(SteamUser.AccountInfoCallback cb)
        {
            if (!string.IsNullOrEmpty(cb.PersonaName) && cb.PersonaName != session.PersonaName)
            {
                session.PersonaName = cb.PersonaName;
                if (!signingOut)
                    session.Save();
            }

            personaKnown.Set();
        }

        private void OnDisconnected(SteamClient.DisconnectedCallback cb)
        {
            loggedOn.Reset();
            tickets.Reset();
            ownershipTicket = null;
            lock (joinedLobbies)
                joinedLobbies.Clear();

            if (!running || cb.UserInitiated || loginRejected)
                return;

            var delay = reconnectDelaySeconds;
            reconnectDelaySeconds = Math.Min(reconnectDelaySeconds * 2, 60);
            Log($"[Steam] disconnected, reconnecting in {delay} s");
            Task.Delay(TimeSpan.FromSeconds(delay)).ContinueWith(_ =>
            {
                if (running)
                    client.Connect();
            });
        }

        /// <summary>Shows the player as playing the game, which is what lets friends see them in it</summary>
        private void SendGamesPlayed()
        {
            var msg = new ClientMsgProtobuf<CMsgClientGamesPlayed>(EMsg.ClientGamesPlayed);
            msg.Body.games_played.Add(new CMsgClientGamesPlayed.GamePlayed { game_id = new GameID(appId) });
            client.Send(msg);
        }

        // --- Helpers ----------------------------------------------------------------------------------------------------

        private SteamMatchmaking.Lobby Lobby(ulong lobbyId) => matchmaking.GetLobby(appId, lobbyId);

        private IReadOnlyList<ulong> JoinedLobbies()
        {
            lock (joinedLobbies)
                return joinedLobbies.ToList();
        }

        private Task<bool> WaitLoggedOnAsync() => Task.Run(() => loggedOn.Wait(RequestTimeout));

        private static async Task<T> WithTimeout<T>(Task<T> task)
        {
            if (await Task.WhenAny(task, Task.Delay(RequestTimeout)).ConfigureAwait(false) != task)
                throw new TimeoutException("Steam did not answer");
            return await task.ConfigureAwait(false);
        }

        private void PostToGame(Action action)
        {
            void Run()
            {
                try
                {
                    action();
                }
                catch (Exception e)
                {
                    Log($"[Steam] event handler failed: {e}");
                }
            }

            if (mainThread != null)
                mainThread.Post(_ => Run(), null);
            else
                Run();
        }

        private static string Describe(SteamMatchmaking.Lobby lobby) =>
            $"owner {lobby.OwnerSteamID?.ConvertToUInt64()}, {lobby.NumMembers}/{lobby.MaxMembers} members, " +
            string.Join(", ", lobby.Metadata.Select(kv => $"{kv.Key}={kv.Value}"));
    }
}
