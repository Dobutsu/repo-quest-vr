using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using HarmonyLib;
using RepoXR;
using RepoXR.Assets;
using UnityEngine;
using UnityEngine.XR.Management;
using UnityEngine.XR.OpenXR;
using UnityEngine.XR.OpenXR.Features;

namespace RepoXR.Standalone
{
    /// <summary>
    /// Marks a method whose body is copied over a RepoXR (or game) method when the patches are baked
    /// (HarmonyBaker --overrides). Same parameters, plus a leading instance parameter for instance targets.
    /// </summary>
    [AttributeUsage(AttributeTargets.Method)]
    public sealed class ReplacesAttribute : Attribute
    {
        public ReplacesAttribute(string type, string method, string platform = "any")
        {
            Type = type;
            Method = method;
            Platform = platform;
        }

        public string Type { get; }
        public string Method { get; }

        /// <summary>Assembly holding the target (default: RepoXR). The game is "Assembly-CSharp" at bake time.</summary>
        public string Assembly { get; set; } = "RepoXR";

        /// <summary>"any", or the bake --platform this applies to ("android")</summary>
        public string Platform { get; }
    }

    /// <summary>What RepoXR does differently once it runs without BepInEx, or without Windows</summary>
    public static class PlatformOverrides
    {
        /// <summary>
        /// FixPluginTypesSerialization registers plugin assemblies with the native serializer under BepInEx.
        /// Standalone builds register RepoXR's assemblies with the player directly, and baked patches are always
        /// active, so RepoXR must never disable itself over this check.
        /// </summary>
        [Replaces("RepoXR.Assets.AssetCollection", "ValidateFPTSVersion")]
        public static bool ValidateFPTSVersion() => true;

        // Win32 calls (user32/advapi32) that do not exist on Android

        [Replaces("RepoXR.Native", "BringGameWindowToFront", "android")]
        public static void BringGameWindowToFront()
        {
        }

        [Replaces("RepoXR.Native", "IsHighIntegrityLevel", "android")]
        public static bool IsHighIntegrityLevel() => false;

        // Discord's native SDK does not exist on Android: DiscordManager would throw every frame from its callback pump
        // and abort level generation from UpdateDiscordRichPresence. Without a client nothing is pumped.

        /// <summary>
        /// HarmonyX compiles a fast invoker with DynamicMethodDefinition (Reflection.Emit), which IL2CPP cannot do.
        /// AccessTools' static constructor builds one, so without this every AccessTools call at runtime (RepoXR's
        /// settings page, its RPC registry) fails with a TypeInitializationException. Reflection invoke instead.
        /// </summary>
        [Replaces("HarmonyLib.MethodInvoker", "GetHandler", "android", Assembly = "0Harmony")]
        public static FastInvokeHandler GetHandler(MethodInfo methodInfo, bool directBoxValueAccess)
        {
            // A direct method-group delegate (ldftn MethodBase.Invoke), so IL2CPP's linker keeps what it calls
            return methodInfo.Invoke;
        }

        /// <summary>
        /// On Quest, Map.AddValuable throws a NullReferenceException whenever a valuable is discovered, although every
        /// reference it uses is set in the exported scene and prefab. Same logic, but each null is reported instead, so
        /// the log says which one it is. No lambdas or helpers: the body is transplanted into the game.
        /// </summary>
        [Replaces("Map", "AddValuable", "android", Assembly = "Assembly-CSharp")]
        public static void MapAddValuable(Map map, ValuableObject valuable)
        {
            if (SemiFunc.RunIsShop() || SemiFunc.RunIsArena() || SemiFunc.RunIsLobby())
                return;

            if (!map.ValuableObject || !map.OverLayerParent || !valuable)
            {
                Debug.LogWarning("[RepoXR.Standalone] Map.AddValuable: prefab " + (bool)map.ValuableObject +
                                 ", OverLayerParent " + (bool)map.OverLayerParent + ", valuable " + (bool)valuable);
                return;
            }

            var marker = UnityEngine.Object.Instantiate(map.ValuableObject, map.OverLayerParent.transform);
            marker.name = valuable.gameObject.name;
            marker.transform.position = valuable.transform.position * map.Scale + map.OverLayerParent.position;
            marker.transform.localPosition = new Vector3(marker.transform.localPosition.x, 0f, marker.transform.localPosition.z);

            var component = marker.GetComponent<MapValuable>();
            if (!component || !component.spriteRenderer)
            {
                Debug.LogWarning("[RepoXR.Standalone] Map.AddValuable: MapValuable " + (bool)component +
                                 ", spriteRenderer " + (component && component.spriteRenderer) + " on " + marker.name);
                return;
            }

            component.target = valuable;
            component.spriteRenderer.sprite = valuable.volumeType <= ValuableVolume.Type.Medium
                ? component.spriteSmall
                : component.spriteBig;
        }

        /// <summary>
        /// Photon's WebRTC audio processing (echo cancellation, noise suppression) needs its native webrtc-audio
        /// library, which only exists for Windows here; PhotonVoice.dll is the Windows build, so it claims support
        /// everywhere. Unsupported makes the component step aside; voice itself works (libopus_egpv.so is built for
        /// Android, scripts/build-opus-android.sh)
        /// </summary>
        [Replaces("Photon.Voice.Unity.WebRtcAudioDsp", "get_IsSupported", "android", Assembly = "PhotonVoice")]
        public static bool WebRtcAudioDspIsSupported(Photon.Voice.Unity.WebRtcAudioDsp dsp) => false;

        /// <summary>
        /// DiscordManager.instance stays null, which every use in the game checks: joining a lobby would otherwise
        /// build a Discord ActivityParty (SteamManager.OnLobbyEntered), whose native calls throw and keep throwing
        /// every frame from Discord's callback pump
        /// </summary>
        [Replaces("DiscordManager", "Awake", "android", Assembly = "Assembly-CSharp")]
        public static void DiscordAwake(object self)
        {
        }

        [Replaces("DiscordManager", "Start", "android", Assembly = "Assembly-CSharp")]
        public static void DiscordStart(object self)
        {
        }

        [Replaces("DiscordManager", "UpdateDiscordRichPresence", "android", Assembly = "Assembly-CSharp")]
        public static void DiscordUpdateRichPresence(object self, string state, string details)
        {
        }

        [Replaces("DiscordManager", "RefreshDiscordRichPresence", "android", Assembly = "Assembly-CSharp")]
        public static void DiscordRefreshRichPresence(object self)
        {
        }

        /// <summary>
        /// The cosmetics come from Steam Cloud first, as on PC, where the Steam client syncs them before the game starts
        /// (<see cref="CosmeticsCloud"/>); then the game's own Start runs. Everything that shows cosmetics waits for
        /// MetaManager.saveReady meanwhile.
        /// </summary>
        [Replaces("MetaManager", "Start", "android", Assembly = "Assembly-CSharp")]
        public static void MetaManagerStart(MetaManager self) => self.StartCoroutine(CosmeticsCloud.StartAfterSync(self));

        /// <summary>Renames the desktop window through user32; there is no such window (or DLL) on Android</summary>
        [Replaces("WindowManager", "Awake", "android", Assembly = "Assembly-CSharp")]
        public static void WindowManagerAwake(object self)
        {
        }

        /// <summary>
        /// Runtime discovery reads the Windows registry. On Quest there is exactly one runtime (Meta's), found by
        /// the Android OpenXR loader; with no runtimes listed RepoXR initializes the system default.
        /// </summary>
        [Replaces("RepoXR.OpenXR", "GetRuntimes", "android")]
        public static OpenXR.Runtimes GetRuntimes() => new OpenXR.Runtimes(Array.Empty<OpenXR.Runtime>());

        /// <summary>
        /// Same as RepoXR's OpenXR.Loader.InitializeScripts, except for the feature list: RepoXR replaces it with its
        /// pack of PC controller profiles. On Quest the APK already enables Meta Quest support and the Touch/Touch Plus
        /// profiles, so the pack is added to those instead.
        /// </summary>
        [Replaces("RepoXR.OpenXR+Loader", "InitializeScripts", "android")]
        public static void InitializeScripts()
        {
            OpenXR.Loader.xrGeneralSettings ??= ScriptableObject.CreateInstance<XRGeneralSettings>();
            OpenXR.Loader.xrManagerSettings ??= ScriptableObject.CreateInstance<XRManagerSettings>();
            OpenXR.Loader.xrLoader ??= ScriptableObject.CreateInstance<OpenXRLoader>();

            OpenXR.Loader.xrGeneralSettings.Manager = OpenXR.Loader.xrManagerSettings;

            var loaders = (List<XRLoader>)OpenXR.Loader.xrManagerSettings.activeLoaders;
            loaders.Clear();
            loaders.Add(OpenXR.Loader.xrLoader);

            var settings = OpenXRSettings.Instance;
            settings.renderMode = Plugin.Config.SinglePassRendering.Value
                ? OpenXRSettings.RenderMode.SinglePassInstanced
                : OpenXRSettings.RenderMode.MultiPass;
            settings.depthSubmissionMode = OpenXRSettings.DepthSubmissionMode.None;

            // No lambdas here: this body is transplanted into RepoXR, and closures would stay behind in this assembly
            var features = new List<OpenXRFeature>(settings.features ?? Array.Empty<OpenXRFeature>());
            foreach (var feature in AssetCollection.OpenXRFeatures.Features)
            {
                var present = false;
                foreach (var existing in features)
                    present |= existing.GetType() == feature.GetType();
                if (!present)
                    features.Add(feature);
            }

            settings.features = features.ToArray();
        }
    }
}
