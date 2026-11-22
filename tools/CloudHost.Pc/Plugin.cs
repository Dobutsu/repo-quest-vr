using System;
using System.IO;
using System.Reflection;
using BepInEx;
using BepInEx.Configuration;
using HarmonyLib;
using RepoQuest.Cloud;
using UnityEngine;

namespace RepoQuest.CloudHost
{
    // The panel reports the package version from the assembly.
    [BepInPlugin("io.repoquest.cloudhost", "Quest Cloud Co-op", "1.2.0")]
    [BepInDependency("com.github.zehsteam.LocalMultiplayer")]
    public sealed class Plugin : BaseUnityPlugin
    {
        public static readonly string ReleaseVersion = typeof(Plugin).Assembly
            .GetCustomAttribute<AssemblyInformationalVersionAttribute>().InformationalVersion.Split('+')[0];
        private static Plugin instance;
        private Harmony harmony;
        private ConfigEntry<string> realtimeAppId;
        private ConfigEntry<string> voiceAppId;
        private object lobby;
        private string code = "";
        private string status = "Create a game with LocalMultiplayer. Password-protected lobbies are not supported.";
        private string lastError;
        private float nextPoll;
        private bool visible = true;
        private string joinCode = "";
        private string CodePath => Path.Combine(Paths.ConfigPath, "quest-room.txt");

        private void Awake()
        {
            instance = this;
            realtimeAppId = Config.Bind("Photon", "AppIdRealtime", "",
                "Photon Realtime App ID. Must match the Quest settings. Restart the game after editing.");
            voiceAppId = Config.Bind("Photon", "AppIdVoice", "",
                "Photon Voice App ID. Must match the Quest settings. Restart the game after editing.");
            ClearCode();
            if (!CloudAppIds.IsValid(realtimeAppId.Value) || !CloudAppIds.IsValid(voiceAppId.Value))
            {
                Logger.LogWarning("Set the Photon Realtime and Voice App IDs in the plugin config and restart the game.");
                enabled = false;
                return;
            }
            harmony = new Harmony("io.repoquest.cloudhost");
            harmony.PatchAll(typeof(CreatedPatch));
            harmony.PatchAll(typeof(AppIdsPatch));
            GuestConnection.Install(harmony);
            Logger.LogInfo("Quest Cloud Co-op " + ReleaseVersion);
            Logger.LogInfo("Quest room panel enabled (F8). Routing code is also written to " + CodePath);
        }

        private void Update()
        {
            if (Input.GetKeyDown(KeyCode.F8)) visible = !visible;
            if (Time.unscaledTime < nextPoll) return;
            nextPoll = Time.unscaledTime + 1f;
            try
            {
                if (GuestConnection.Active)
                {
                    ClearCode();
                    GuestConnection.Poll();
                    status = GuestConnection.Status;
                    return;
                }
                if (!PhotonAccess.InRoom || !(bool)PhotonAccess.Get(PhotonAccess.Network, "IsMasterClient"))
                {
                    ClearCode();
                    status = "Waiting for a hosted Photon room. Create a new game to publish its code.";
                    return;
                }
                if (lobby == null) throw new InvalidOperationException("No Steam lobby captured. Create a new game after enabling Quest Cloud Host.");
                string Data(string key) => (string)lobby.GetType().GetMethod("GetData").Invoke(lobby, new object[] { key });
                var invitation = CloudRoomRouting.HostedInvitation(SteamId(PhotonAccess.Get(lobby, "Id")),
                    SteamId(PhotonAccess.Get(PhotonAccess.Find("Steamworks.SteamClient"), "SteamId")), Data);
                var next = invitation.Encode();
                if (next != code) { File.WriteAllText(CodePath, next); code = next; }
                status = "Room ready: " + invitation.Region + ". Copy the full code to the Quest. F8 hides this panel.";
                lastError = null;
            }
            catch (Exception error)
            {
                ClearCode();
                status = error.GetBaseException().Message;
                if (status != lastError) { Logger.LogWarning(status); lastError = status; }
            }
        }

        private static ulong SteamId(object value) => Convert.ToUInt64(PhotonAccess.Get(value, "Value"));

        private void ClearCode()
        {
            code = "";
            // Never leave a stale invitation on disk after the host leaves.
            if (File.Exists(CodePath)) File.Delete(CodePath);
        }

        private void OnGUI()
        {
            if (!visible) return;
            GUILayout.BeginArea(new Rect(20, 20, Math.Min(Screen.width - 40, 740), 350), GUI.skin.box);
            GUILayout.Label("QUEST CLOUD CO-OP " + ReleaseVersion);
            GUILayout.Label(status);
            if (code.Length > 0)
            {
                GUILayout.TextArea(code, GUILayout.Height(100));
                if (GUILayout.Button("Copy room code")) GUIUtility.systemCopyBuffer = code;
            }
            if (GuestConnection.Active)
            {
                if (GUILayout.Button("Leave cloud session")) GuestConnection.Fail("Session left.");
            }
            else
            {
                GUILayout.Label("Join a headset or PC host (RQ1 code):");
                joinCode = GUILayout.TextField(joinCode, 1024);
                if (GUILayout.Button("Paste code")) joinCode = GUIUtility.systemCopyBuffer.Trim();
                if (GUILayout.Button("Join room"))
                {
                    try { GuestConnection.Join(joinCode, realtimeAppId.Value.Trim(), voiceAppId.Value.Trim()); status = GuestConnection.Status; }
                    catch (Exception error) { status = error.GetBaseException().Message; Logger.LogWarning(status); }
                }
            }
            GUILayout.EndArea();
        }

        private void OnDestroy()
        {
            harmony?.UnpatchSelf();
            ClearCode();
            if (instance == this) instance = null;
        }

        [HarmonyPatch]
        private static class AppIdsPatch
        {
            private static MethodBase TargetMethod() => AccessTools.DeclaredMethod(
                PhotonAccess.Find("DataDirector"), "PhotonSetAppId") ?? throw new MissingMethodException("DataDirector.PhotonSetAppId");

            [HarmonyPriority(Priority.Last)]
            [HarmonyAfter("com.github.zehsteam.LocalMultiplayer")]
            private static void Postfix()
            {
                if (!CloudAppIds.IsValid(instance.realtimeAppId.Value) || !CloudAppIds.IsValid(instance.voiceAppId.Value))
                    throw new InvalidOperationException("Enter valid Photon App IDs in io.repoquest.cloudhost.cfg, then restart.");
                PhotonAccess.Configure(instance.realtimeAppId.Value.Trim(), instance.voiceAppId.Value.Trim());
            }
        }

        [HarmonyPatch]
        private static class CreatedPatch
        {
            private static MethodBase TargetMethod() => AccessTools.DeclaredMethod(
                PhotonAccess.Find("SteamLobbyManager"), "OnLobbyCreated") ?? throw new MissingMethodException("SteamLobbyManager.OnLobbyCreated");

            private static void Postfix(object[] __args)
            {
                if (instance == null) return;
                foreach (var arg in __args)
                    if (arg?.GetType().FullName == "Steamworks.Data.Lobby") instance.lobby = arg;
            }
        }
    }
}
