using System;
using System.Collections;
using System.Collections.Generic;
using System.Reflection;
using System.Threading.Tasks;
using HarmonyLib;
using RepoQuest.Cloud;
using Steamworks;
using Steamworks.Data;
using UnityEngine;

namespace RepoQuest.CloudHost
{
    /// <summary>Scoped adapter for a code-based guest. Native Steam hosting remains available between sessions.</summary>
    internal static class GuestConnection
    {
        private static CloudJoinCode invitation;
        private static bool joined;
        private static bool ending;
        private static int hostActor;
        private static float deadline;
        internal static string Status { get; private set; }
        internal static bool Active => invitation != null;
        private static bool Matches(Lobby lobby) => Active && (ulong)lobby.Id == invitation.LobbyId;
        private static object Manager(string type) => PhotonAccess.Get(PhotonAccess.Find(type), "instance");
        private static bool InMainMenu => (bool)AccessTools.Method(PhotonAccess.Find("SemiFunc"), "IsMainMenu").Invoke(null, null);

        internal static void Install(Harmony harmony)
        {
            foreach (var type in typeof(GuestConnection).GetNestedTypes(BindingFlags.NonPublic))
                if (type.IsDefined(typeof(HarmonyPatch), false)) harmony.PatchAll(type);
        }

        internal static void Join(string code, string realtime, string voice)
        {
            if (Active || PhotonAccess.Connected || !InMainMenu)
                throw new InvalidOperationException("Return to the main menu and leave the current session first.");
            var next = CloudJoinCode.Decode(code);
            next.CheckApps(realtime, voice);
            var manager = Manager("SteamLobbyManager") ?? throw new InvalidOperationException("Lobby manager is not ready.");
            invitation = next;
            joined = ending = false;
            deadline = Time.unscaledTime + 45f;
            Status = "Connecting to room...";
            try
            {
                PhotonAccess.Configure(realtime, voice);
                PhotonAccess.Set(PhotonAccess.Settings, "FixedRegion", next.Region);
                AccessTools.Method(manager.GetType(), "OnGameLobbyJoinRequested").Invoke(manager,
                    new object[] { new Lobby(next.LobbyId), (SteamId)next.OwnerId });
            }
            catch { Fail("Could not start the room connection."); throw; }
        }

        internal static void Poll()
        {
            if (!Active) return;
            if (ending)
            {
                if (!PhotonAccess.Connected && InMainMenu && !UnityEngine.Object.FindFirstObjectByType(PhotonAccess.Find("NetworkConnect")))
                { invitation = null; joined = false; }
                return;
            }
            if (PhotonAccess.InRoom)
            {
                var master = PhotonAccess.Get(PhotonAccess.Network, "MasterClient");
                try { CloudRoomRouting.ValidateHostIdentity(invitation, (string)PhotonAccess.Get(master, "UserId")); }
                catch (InvalidOperationException error) { Fail(error.Message); return; }
                var actor = Convert.ToInt32(PhotonAccess.Get(master, "ActorNumber"));
                if ((bool)PhotonAccess.Get(PhotonAccess.Network, "IsMasterClient") || (joined && actor != hostActor))
                { Fail("The host left. The session has ended."); return; }
                if (!joined) { joined = true; hostActor = actor; Status = "Connected as guest."; }
            }
            else if (joined) Fail("Disconnected from the room.");
            else if (Time.unscaledTime >= deadline) Fail("Connection timed out. Check the code, room capacity and internet connection.");
        }

        internal static void Fail(string message)
        {
            if (!Active || ending) return;
            ending = true; // Keep guest save protection until teardown has returned to the main menu.
            Status = message;
            Debug.LogWarning("[CloudCoop] " + message);
            PhotonAccess.Disconnect();
            var network = Manager("NetworkManager") as MonoBehaviour;
            if (network)
            {
                AccessTools.Method(network.GetType(), "TriggerLeavePhotonRoomForced").Invoke(network, null);
                return;
            }
            var manager = Manager("SteamLobbyManager");
            AccessTools.Method(manager.GetType(), "LeaveLobby").Invoke(manager, null);
            var game = Manager("GameManager");
            AccessTools.Method(game.GetType(), "SetGameMode").Invoke(game, new object[] { 0 });
            var run = (MonoBehaviour)Manager("RunManager");
            run.StartCoroutine((IEnumerator)AccessTools.Method(run.GetType(), "LeaveToMainMenu").Invoke(run, null));
        }

        [HarmonyPatch(typeof(SteamMatchmaking), nameof(SteamMatchmaking.JoinLobbyAsync))]
        private static class JoinPatch
        {
            private static bool Prefix(SteamId __0, ref Task<Lobby?> __result)
            {
                if (!Active || (ulong)__0 != invitation.LobbyId) return true;
                var lobby = new Lobby(__0);
                var manager = Manager("SteamLobbyManager");
                AccessTools.Method(manager.GetType(), "OnLobbyEntered").Invoke(manager, new object[] { lobby });
                __result = Task.FromResult<Lobby?>(lobby);
                return false;
            }
        }

        [HarmonyPatch(typeof(Lobby), nameof(Lobby.GetData))]
        private static class DataPatch
        {
            private static bool Prefix(Lobby __instance, string __0, ref string __result)
            {
                if (!Matches(__instance)) return true;
                switch (__0)
                {
                    case "RoomName": __result = invitation.Room; break;
                    case "Region": __result = invitation.Region; break;
                    case "BuildName": __result = invitation.Build; break;
                    case "HasPassword": __result = "0"; break;
                    default: __result = ""; break;
                }
                return false;
            }
        }

        [HarmonyPatch(typeof(Lobby), "get_Owner")]
        private static class OwnerPatch
        {
            private static bool Prefix(Lobby __instance, ref Friend __result)
            {
                if (!Matches(__instance)) return true;
                __result = new Friend(invitation.OwnerId);
                return false;
            }
        }

        [HarmonyPatch(typeof(Lobby), "get_MaxMembers")]
        private static class CapacityPatch
        {
            private static bool Prefix(Lobby __instance, ref int __result)
            {
                if (!Matches(__instance)) return true;
                __result = invitation.Capacity;
                return false;
            }
        }

        [HarmonyPatch(typeof(Lobby), "get_MemberCount")]
        private static class CountPatch
        {
            private static bool Prefix(Lobby __instance, ref int __result)
            {
                if (!Matches(__instance)) return true;
                __result = PhotonAccess.InRoom ? Convert.ToInt32(PhotonAccess.Get(PhotonAccess.Get(PhotonAccess.Network, "CurrentRoom"), "PlayerCount")) : 2;
                return false;
            }
        }

        [HarmonyPatch(typeof(Lobby), "get_Members")]
        private static class MembersPatch
        {
            private static bool Prefix(Lobby __instance, ref IEnumerable<Friend> __result)
            {
                if (!Matches(__instance)) return true;
                __result = new[] { new Friend(invitation.OwnerId), new Friend(SteamClient.SteamId) };
                return false;
            }
        }

        [HarmonyPatch(typeof(Lobby), nameof(Lobby.Leave))]
        private static class LeavePatch
        {
            private static bool Prefix(Lobby __instance) => !Matches(__instance);
        }

        [HarmonyPatch]
        private static class AuthenticationPatch
        {
            private static MethodBase TargetMethod() => AccessTools.Method(PhotonAccess.Find("Platforms.PlatformManager"), "SetPhotonAuthTicket");
            [HarmonyPriority(Priority.Last)]
            [HarmonyAfter("com.github.zehsteam.LocalMultiplayer")]
            private static void Postfix()
            {
                if (Active) PhotonAccess.Authenticate(SteamClient.SteamId.ToString(), SteamClient.Name);
            }
        }

        [HarmonyPatch]
        private static class SavePatch
        {
            private static MethodBase TargetMethod() => AccessTools.Method(PhotonAccess.Find("MetaManager"), "Save");
            private static bool Prefix() => !Active;
        }

        [HarmonyPatch]
        private static class RoomPatch
        {
            private static IEnumerable<MethodBase> TargetMethods()
            {
                var client = PhotonAccess.Find("Photon.Realtime.LoadBalancingClient");
                var args = new[] { PhotonAccess.Find("Photon.Realtime.EnterRoomParams") };
                foreach (var name in new[] { "OpJoinRoom", "OpJoinOrCreateRoom" })
                    yield return AccessTools.DeclaredMethod(client, name, args) ?? throw new MissingMethodException(client.FullName, name);
            }
            private static bool Prefix(object __instance, object __0, MethodBase __originalMethod, ref bool __result)
            {
                if (!ReferenceEquals(__instance, PhotonAccess.Client)) return true;
                if (!Active)
                {
                    if (__originalMethod.Name == "OpJoinOrCreateRoom") CloudRoomRouting.PublishHostIdentity(__0);
                    return true;
                }
                if (ending) return false;
                try
                {
                    CloudRoomRouting.ValidateGuestJoin(invitation, (string)PhotonAccess.Get(__instance, "AppVersion"),
                        (string)PhotonAccess.Get(__0, "RoomName"));
                    if (__originalMethod.Name == "OpJoinRoom") return true;
                    __result = CloudRoomRouting.JoinExisting(__instance, __0);
                }
                catch (Exception error) { Fail(error.GetBaseException().Message); }
                return false;
            }
        }

        [HarmonyPatch]
        private static class DisconnectPatch
        {
            private static IEnumerable<MethodBase> TargetMethods()
            {
                foreach (var name in new[] { "NetworkManager", "NetworkConnect" })
                    yield return AccessTools.DeclaredMethod(PhotonAccess.Find(name), "OnDisconnected");
            }
            private static bool Prefix(object __0)
            {
                var reason = __0.ToString();
                if (!Active || ending || reason == "ApplicationQuit" || (!joined && reason == "DisconnectByClientLogic")) return true;
                Fail("Photon disconnected: " + reason);
                return false;
            }
        }

        [HarmonyPatch]
        private static class MasterPatch
        {
            private static MethodBase TargetMethod() => AccessTools.Method(PhotonAccess.Find("NetworkManager"), "OnMasterClientSwitched");
            private static bool Prefix()
            {
                if (!Active) return true;
                Fail("The host left. The session has ended.");
                return false;
            }
        }
    }
}
