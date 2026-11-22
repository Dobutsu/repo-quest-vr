using HarmonyLib;
using Platforms;
using RepoQuest.Cloud;
using System;
using System.Reflection;
using System.Collections.Generic;

namespace RepoXR.Standalone
{
    [HarmonyPatch]
    public static class CloudRoomJoinPatch
    {
        public static IEnumerable<MethodBase> TargetMethods()
        {
            var type = PhotonAccess.Find("Photon.Realtime.LoadBalancingClient");
            var parameters = new[] { PhotonAccess.Find("Photon.Realtime.EnterRoomParams") };
            foreach (var name in new[] { "OpJoinRoom", "OpJoinOrCreateRoom" })
                yield return AccessTools.DeclaredMethod(type, name, parameters) ?? throw new MissingMethodException(type.FullName, name);
        }

        public static bool Prefix(object __instance, object __0, MethodBase __originalMethod, ref bool __result)
        {
            if (CloudCoop.IsHost && ReferenceEquals(__instance, PhotonAccess.Client) && __originalMethod.Name == "OpJoinOrCreateRoom")
                CloudRoomRouting.PublishHostIdentity(__0);
            if (!CloudCoop.ValidateRoomJoin(__instance, __0)) return false;
            if (CloudCoop.Enabled && CloudCoop.Invitation != null && ReferenceEquals(__instance, PhotonAccess.Client) &&
                __originalMethod.Name == "OpJoinOrCreateRoom")
            {
                __result = CloudRoomRouting.JoinExisting(__instance, __0);
                return false;
            }
            return true;
        }
    }

    // Public methods are called by baked game IL, not Harmony-generated DynamicMethods on the headset.
    [HarmonyPatch(typeof(DataDirector), nameof(DataDirector.PhotonSetAppId))]
    public static class CloudAppSettingsPatch
    {
        public static void Postfix() => CloudCoop.ApplySettings();
    }

    [HarmonyPatch(typeof(PlatformManager), nameof(PlatformManager.SetPhotonAuthTicket))]
    public static class CloudAuthenticationPatch
    {
        public static bool Prefix()
        {
            if (!CloudCoop.Enabled) return true;
            CloudCoop.Authenticate();
            return false;
        }
    }

    [HarmonyPatch(typeof(PlayerAvatar), nameof(PlayerAvatar.AddToStatsManager))]
    public static class CloudNicknamePatch
    {
        public static void Prefix()
        {
            if (CloudCoop.Enabled && CloudCoop.StartupError == null)
                PhotonAccess.Set(PhotonAccess.Network, "NickName", CloudCoop.ActiveSettings.playerName);
        }
    }

    [HarmonyPatch(typeof(MenuPageMain), nameof(MenuPageMain.ButtonEventJoinGame))]
    public static class CloudJoinButtonPatch
    {
        public static bool Prefix()
        {
            CloudCoop.Menu.Open();
            return false;
        }
    }

    [HarmonyPatch]
    public static class CloudDisconnectPatch
    {
        public static IEnumerable<MethodBase> TargetMethods()
        {
            yield return AccessTools.DeclaredMethod(typeof(NetworkManager), nameof(NetworkManager.OnDisconnected));
            yield return AccessTools.DeclaredMethod(typeof(NetworkConnect), nameof(NetworkConnect.OnDisconnected));
        }
        public static void Postfix(object __0)
        {
            if (CloudCoop.Enabled) CloudCoop.Menu.Disconnected(__0.ToString());
        }
    }

    [HarmonyPatch(typeof(NetworkManager), nameof(NetworkManager.OnMasterClientSwitched))]
    public static class CloudMasterChangedPatch
    {
        public static bool Prefix()
        {
            if (!CloudCoop.Enabled || !CloudCoop.IsGuest) return true;
            CloudCoop.Menu.ConnectionFailed("The host has left. The session has ended.");
            return false;
        }
    }

    [HarmonyPatch(typeof(MetaManager), nameof(MetaManager.Save))]
    public static class CloudGuestSavePatch
    {
        public static bool Prefix() => !CloudCoop.Enabled || !CloudCoop.IsGuest;
    }

    [HarmonyPatch(typeof(SteamLobbyManager), nameof(SteamLobbyManager.OpenSteamOverlayToInvite))]
    public static class CloudInviteButtonPatch
    {
        public static bool Prefix()
        {
            if (!CloudCoop.Enabled) return true;
            CloudCoop.Menu.ShowInvitation();
            return false;
        }
    }

    [HarmonyPatch(typeof(MenuPageMain), nameof(MenuPageMain.ButtonEventHostGame))]
    public static class CloudHostButtonPatch
    {
        public static bool Prefix()
        {
            if (!CloudCoop.Menu.AllowNativeHost) { CloudCoop.Menu.OpenHost(); return false; }
            if (CloudCoop.StartupError != null || CloudCoop.RestartRequired)
            {
                CloudCoop.Menu.ConnectionFailed(CloudCoop.StartupError ?? "Settings changed. Restart the game first.");
                return false;
            }
            if (CloudCoop.Enabled)
            {
                CloudCoop.ClearInvitation();
                CloudCoop.GuestReturnedToMenu();
            }
            return true;
        }
    }

    [HarmonyPatch(typeof(SteamLobbyManager), nameof(SteamLobbyManager.HostLobby))]
    public static class CloudHostingProgressPatch
    {
        public static void Prefix()
        {
            if (CloudCoop.Enabled) CloudCoop.Menu.HostingStarted();
        }
    }

    [HarmonyPatch(typeof(MenuPagePassword), "Start")]
    public static class CloudNoPasswordPatch
    {
        public static void Postfix(MenuPagePassword __instance)
        {
            if (!CloudCoop.Enabled || CloudCoop.IsGuest) return;
            __instance.passwordInput.textCurrent = "";
            __instance.ConfirmButton();
        }
    }

    [HarmonyPatch(typeof(NetworkConnect), nameof(NetworkConnect.OnJoinRoomFailed))]
    public static class CloudRoomFailurePatch
    {
        public static bool Prefix(short __0)
        {
            if (!CloudCoop.Enabled) return true;
            CloudCoop.Menu.RoomFailed(__0);
            return false;
        }
    }
}
