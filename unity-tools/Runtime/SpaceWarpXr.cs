using System;
using System.Linq;
using System.Reflection;
using System.Runtime.InteropServices;
using UnityEngine;
using UnityEngine.XR.OpenXR;
using UnityEngine.XR.OpenXR.Features;

namespace RepoQuest
{
    /// <summary>
    /// Application SpaceWarp's OpenXR side: a feature added to the settings at startup (as PerfXr is; RepoXR starts XR
    /// later and keeps the features it finds) that asks the runtime for XR_FB_space_warp. OpenXR 1.14.3 has no
    /// SpaceWarpFeature class (1.16 has, PEAK uses it), but its native plugin (libUnityOpenXR.so) already implements the
    /// extension: it allocates the motion vector and depth swapchains, hands them out as
    /// XRDisplaySubsystem.XRRenderPass.motionVectorRenderTarget and submits them with every frame; MetaSetSpaceWarp,
    /// MetaSetAppSpacePosition and MetaSetAppSpaceRotation are the exports 1.16's SpaceWarpFeature calls. The motion
    /// vectors themselves are drawn by QuestSpaceWarp (URP draws them in its XRDepthMotionPass; R.E.P.O. renders with the
    /// built-in pipeline, which does not).
    /// /sdcard/Android/data/io.repoxr.quest/files/spacewarp-off (an empty file) leaves the extension out altogether.
    /// </summary>
    public class SpaceWarpXr : OpenXRFeature
    {
        public const string Extension = "XR_FB_space_warp";

        public static SpaceWarpXr Instance { get; private set; }
        public static string Status { get; private set; } = "not added";
        public static bool SessionRunning { get; private set; }

        public static bool ExtensionEnabled
        {
            get
            {
                try
                {
                    return Instance != null && OpenXRRuntime.IsExtensionEnabled(Extension);
                }
                catch (Exception)
                {
                    return false;
                }
            }
        }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
        private static void Install()
        {
            if (Application.platform != RuntimePlatform.Android)
                return;
            if (QuestSpaceWarp.ExtensionOff())
            {
                Status = "not added (files/spacewarp-off)";
                Debug.Log($"[QuestSpaceWarp] {Status}");
                return;
            }
            try
            {
                var settings = OpenXRSettings.Instance;
                var features = typeof(OpenXRSettings).GetField("features", BindingFlags.Instance | BindingFlags.NonPublic);
                if (settings == null || features == null)
                {
                    Status = "OpenXR settings not found";
                    return;
                }
                var feature = CreateInstance<SpaceWarpXr>();
                feature.name = "RepoQuest SpaceWarp";
                feature.hideFlags = HideFlags.DontUnloadUnusedAsset;
                Set(feature, "nameUi", "RepoQuest Application SpaceWarp");
                Set(feature, "version", "1");
                Set(feature, "company", "RepoQuest");
                Set(feature, "featureIdInternal", "io.repoxr.quest.spacewarp");
                Set(feature, "openxrExtensionStrings", Extension);
                feature.enabled = true;
                var current = (OpenXRFeature[])features.GetValue(settings) ?? Array.Empty<OpenXRFeature>();
                features.SetValue(settings, current.Append(feature).ToArray());
                Instance = feature;
                Status = "added, no OpenXR instance yet";
            }
            catch (Exception e)
            {
                Status = $"not added: {e.GetType().Name}: {e.Message}";
            }
            Debug.Log($"[QuestSpaceWarp] feature {Status}");
        }

        private static void Set(OpenXRFeature feature, string field, string value) =>
            typeof(OpenXRFeature).GetField(field, BindingFlags.Instance | BindingFlags.NonPublic)?.SetValue(feature, value);

        protected override bool OnInstanceCreate(ulong xrInstance)
        {
            Status = OpenXRRuntime.IsExtensionEnabled(Extension) ? $"{Extension} on" : $"{Extension} missing (runtime refused it)";
            Debug.Log($"[QuestSpaceWarp] instance: {Status}");
            // Never fail the instance over it: without the extension the game runs as before
            return true;
        }

        protected override void OnSessionBegin(ulong xrSession) => SessionRunning = true;

        protected override void OnSessionEnd(ulong xrSession) => SessionRunning = false;

        protected override void OnSessionDestroy(ulong xrSession) => SessionRunning = false;

        private const string Library = "UnityOpenXR";

        [DllImport(Library, EntryPoint = "MetaSetSpaceWarp")]
        private static extern int MetaSetSpaceWarp([MarshalAs(UnmanagedType.I1)] bool enabled);

        [DllImport(Library, EntryPoint = "MetaSetAppSpacePosition")]
        private static extern int MetaSetAppSpacePosition(float x, float y, float z);

        [DllImport(Library, EntryPoint = "MetaSetAppSpaceRotation")]
        private static extern int MetaSetAppSpaceRotation(float x, float y, float z, float w);

        /// <summary>XrResult of the native call (0 = success)</summary>
        public static int SetSpaceWarp(bool on) => MetaSetSpaceWarp(on);

        public static int SetAppSpacePosition(Vector3 p) => MetaSetAppSpacePosition(p.x, p.y, p.z);

        public static int SetAppSpaceRotation(Quaternion q) => MetaSetAppSpaceRotation(q.x, q.y, q.z, q.w);
    }
}
