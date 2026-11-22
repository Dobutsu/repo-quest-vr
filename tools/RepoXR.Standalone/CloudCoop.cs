using System;
using System.IO;
using RepoQuest.Cloud;
using Steamworks;
using Steamworks.Data;
using UnityEngine;

namespace RepoXR.Standalone
{
    /// <summary>Cloud is the default transport. Mode changes take effect before Steam login on the next launch.</summary>
    public static class CloudCoop
    {
        public static CloudSettings Settings { get; private set; }
        public static CloudSettings ActiveSettings { get; private set; }
        public static bool Enabled { get; private set; }
        public static CloudProfile Profile { get; private set; }
        public static CloudSteamBackend Backend { get; private set; }
        public static CloudMenu Menu { get; private set; }
        public static CloudJoinCode Invitation { get; private set; }
        public static string StartupError { get; private set; }
        public static bool RestartRequired { get; private set; }
        public static bool MigrationNotice { get; set; }
        public static bool IsGuest { get; private set; }
        public static bool IsHost => Enabled && Backend != null && Backend.IsHosting && !IsGuest;
        public static string SettingsPath => Path.Combine(Application.persistentDataPath, "cloud-coop.json");

        public static void Initialize()
        {
            try
            {
                var existing = File.Exists(SettingsPath);
                Settings = existing ? JsonUtility.FromJson<CloudSettings>(File.ReadAllText(SettingsPath)) : new CloudSettings();
                if (Settings == null) throw new FormatException("Empty cloud-coop.json");
                var previousSteam = !Settings.enabled;
                Settings.websiteUrl = CloudWebsite.Load(Settings.websiteUrl);
                if (Settings.Migrate())
                {
                    WriteJson(SettingsPath, Settings);
                    MigrationNotice = existing && previousSteam && Settings.enabled;
                }
                Enabled = Settings.enabled;
                if (Enabled)
                {
                    ValidateSettings(Settings);
                    var path = Path.Combine(Application.persistentDataPath, "cloud-profile.json");
                    if (!string.IsNullOrEmpty(Settings.cloudPlayerId))
                    {
                        Profile = CloudProfile.Resolve(null, Settings.cloudPlayerId);
                        // Settings are the committed selection; retry profile application after an interrupted launch.
                        var json = JsonUtility.ToJson(Profile, true);
                        if (!File.Exists(path) || File.ReadAllText(path) != json) WriteJson(path, Profile);
                    }
                    else if (File.Exists(path))
                        Profile = JsonUtility.FromJson<CloudProfile>(File.ReadAllText(path));
                    else
                    {
                        var identity = CloudProfile.GenerateId();
                        Profile = new CloudProfile { userId = identity, gameId = identity };
                        WriteJson(path, Profile);
                    }
                    Profile = CloudProfile.Resolve(Profile, null);
                    Backend = new CloudSteamBackend(ulong.Parse(Profile.gameId), Settings.playerName);
                }
            }
            catch (Exception error)
            {
                // Fail closed: a damaged cloud configuration must not start a saved Steam login.
                Enabled = true;
                Settings = Settings ?? new CloudSettings { enabled = true };
                StartupError = error.Message;
                Debug.LogError("[CloudCoop] " + error);
                Backend = new CloudSteamBackend(0x0240000100000001UL, "Cloud profile error");
            }
            var host = new GameObject("RepoQuest Cloud co-op");
            ActiveSettings = Settings.Copy();
            UnityEngine.Object.DontDestroyOnLoad(host);
            Menu = host.AddComponent<CloudMenu>();
        }

        public static void SaveSettings(CloudSettings settings)
        {
            settings = settings.PrepareSave();
            WriteJson(SettingsPath, settings);
            Settings = settings;
            RestartRequired = StartupError != null || !settings.MatchesRuntime(ActiveSettings);
        }

        public static void ValidateSettings(CloudSettings settings)
        {
            settings.PrepareSave();
        }

        private static void WriteJson(string path, object value) =>
            CloudConfigFile.Write(path, JsonUtility.ToJson(value, true));

        public static void ApplySettings()
        {
            if (!Enabled || StartupError != null) return;
            ValidateSettings(ActiveSettings);
            PhotonAccess.Configure(ActiveSettings.realtimeAppId.Trim(), ActiveSettings.voiceAppId.Trim());
            if (Invitation != null) PhotonAccess.Set(PhotonAccess.Settings, "FixedRegion", Invitation.Region);
        }

        public static void Authenticate()
        {
            if (!Enabled || StartupError != null) return;
            ApplySettings();
            Backend.SetName(ActiveSettings.playerName);
            // Keep Photon UserId equal to the numeric platform identity, as in the game's Steam join flow.
            PhotonAccess.Authenticate(Profile.userId, ActiveSettings.playerName);
        }

        public static void Join(string text)
        {
            if (!Enabled) throw new InvalidOperationException("Select Cloud in Network play > Change, then restart the game.");
            if (RestartRequired || Settings.enabled != Enabled) throw new InvalidOperationException("Settings changed. Restart the game first.");
            if (StartupError != null) throw new InvalidOperationException(StartupError);
            if (PhotonAccess.Connected) throw new InvalidOperationException("Leave the current session before joining another room.");
            ValidateSettings(Settings);
            var invitation = CloudJoinCode.Decode(text);
            invitation.CheckApps(Settings.realtimeAppId, Settings.voiceAppId);
            if (!SteamLobbyManager.instance) throw new InvalidOperationException("The game's lobby manager is not ready.");
            Invitation = invitation;
            IsGuest = true;
            Backend.Prepare(invitation);
            Authenticate();
            Debug.Log("[CloudCoop] joining room in " + invitation.Region);
            // Reuse the original game's scene handoff and OnLobbyEntered path. The backend supplies metadata locally.
            SteamLobbyManager.instance.OnGameLobbyJoinRequested(new Lobby(invitation.LobbyId), invitation.OwnerId);
        }

        public static bool ValidateRoomJoin(object client, object roomParameters)
        {
            if (!Enabled || !ReferenceEquals(client, PhotonAccess.Client)) return true;
            if (IsGuest && Invitation == null) return false; // Cancelled join; do not let a pending coroutine create a room.
            if (Invitation == null) return true;
            try
            {
                // Validate the effective version after the game sets it, on the main client only (not Voice).
                // Never copy the host's version over an incompatible build.
                CloudRoomRouting.ValidateGuestJoin(Invitation, (string)PhotonAccess.Get(client, "AppVersion"),
                    (string)PhotonAccess.Get(roomParameters, "RoomName"));
                return true;
            }
            catch (Exception error)
            {
                Menu.ConnectionFailed(error.GetBaseException().Message);
                return false;
            }
        }

        public static void ClearInvitation()
        {
            Invitation = null;
            Backend?.Clear();
        }

        public static void GuestReturnedToMenu() => IsGuest = false;
    }
}
