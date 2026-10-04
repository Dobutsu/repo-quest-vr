using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.XR.Management;
using UnityEditor.XR.Management.Metadata;
using UnityEditor.XR.OpenXR.Features;
using UnityEngine;
using UnityEngine.XR.Management;
using UnityEngine.XR.OpenXR;
using UnityEngine.XR.OpenXR.Features.Interactions;
using UnityEngine.XR.OpenXR.Features.MetaQuestSupport;

namespace RepoQuest.Editor
{
    /// <summary>
    /// XR setup for the VR build (only copied into the project together with the XR packages):
    /// OpenXR as the Android loader, not started automatically (RepoXR starts XR itself once its assets are
    /// loaded), with Meta Quest support and the Quest controller profiles enabled.
    /// </summary>
    public static class QuestXRSetup
    {
        private const string SettingsFolder = "Assets/XR";

        public static void Apply()
        {
            var perTarget = GetOrCreatePerTargetSettings();
            if (!perTarget.HasSettingsForBuildTarget(BuildTargetGroup.Android))
                perTarget.CreateDefaultSettingsForBuildTarget(BuildTargetGroup.Android);
            if (!perTarget.HasManagerSettingsForBuildTarget(BuildTargetGroup.Android))
                perTarget.CreateDefaultManagerSettingsForBuildTarget(BuildTargetGroup.Android);

            var general = perTarget.SettingsForBuildTarget(BuildTargetGroup.Android);
            general.InitManagerOnStart = false;

            if (!XRPackageMetadataStore.AssignLoader(general.AssignedSettings, typeof(OpenXRLoader).FullName, BuildTargetGroup.Android))
                Debug.LogWarning("[QuestXRSetup] OpenXR loader was already assigned or could not be assigned");

            FeatureHelpers.RefreshFeatures(BuildTargetGroup.Android);
            var openXr = OpenXRSettings.GetSettingsForBuildTargetGroup(BuildTargetGroup.Android);
            Enable<MetaQuestFeature>(openXr);
            Enable<OculusTouchControllerProfile>(openXr);
            Enable<MetaQuestTouchPlusControllerProfile>(openXr);

            // Multiview is RepoXR's default on the Quest (patches/repoxr/0003): with it, a symmetric projection lets both
            // eyes share one frustum shape (Meta: 5-15% less GPU time). The editor keeps the flag in two places
            openXr.symmetricProjection = true;
            if (openXr.GetFeature<MetaQuestFeature>() is { } metaQuest)
            {
                var serialized = new SerializedObject(metaQuest);
                serialized.FindProperty("symmetricProjection").boolValue = true;
                // OpenXR's validation refuses symmetric projection while the original Quest is a target device (it
                // cannot do it); this build is for the Quest 3 anyway
                var devices = serialized.FindProperty("targetDevices");
                for (var i = 0; i < devices.arraySize; i++)
                {
                    var device = devices.GetArrayElementAtIndex(i);
                    if (device.FindPropertyRelative("manifestName").stringValue == "quest")
                        device.FindPropertyRelative("enabled").boolValue = false;
                }
                serialized.ApplyModifiedPropertiesWithoutUndo();
            }

            // OpenXR 1.14.3's controller profiles fetch their thumbsticks as StickControl in FinishSetup, but declare them
            // as Vector2Control unless this is defined: without it no Quest controller device can be created. RepoXR's
            // bindings also use stick sub-controls (primary2DAxis/up)
            const string stickDefine = "USE_STICK_CONTROL_THUMBSTICKS";
            var defines = PlayerSettings.GetScriptingDefineSymbolsForGroup(BuildTargetGroup.Android);
            if (!defines.Split(';').Contains(stickDefine))
                PlayerSettings.SetScriptingDefineSymbolsForGroup(BuildTargetGroup.Android,
                    string.IsNullOrEmpty(defines) ? stickDefine : defines + ";" + stickDefine);

            EditorUtility.SetDirty(perTarget);
            EditorUtility.SetDirty(general);
            EditorUtility.SetDirty(openXr);
            AssetDatabase.SaveAssets();
            Debug.Log("[QuestXRSetup] OpenXR configured for Android (Meta Quest, Touch, Touch Plus)");
        }

        private static void Enable<T>(OpenXRSettings settings) where T : UnityEngine.XR.OpenXR.Features.OpenXRFeature
        {
            var feature = settings.GetFeature<T>();
            if (feature == null)
            {
                Debug.LogError($"[QuestXRSetup] feature {typeof(T).Name} not found");
                return;
            }

            feature.enabled = true;
            EditorUtility.SetDirty(feature);
        }

        private static XRGeneralSettingsPerBuildTarget GetOrCreatePerTargetSettings()
        {
            if (EditorBuildSettings.TryGetConfigObject(XRGeneralSettings.k_SettingsKey, out XRGeneralSettingsPerBuildTarget existing) && existing != null)
                return existing;

            // Left by an earlier run that did not get to register it
            var path = $"{SettingsFolder}/XRGeneralSettingsPerBuildTarget.asset";
            var created = AssetDatabase.LoadAssetAtPath<XRGeneralSettingsPerBuildTarget>(path);
            if (created == null)
            {
                Directory.CreateDirectory(SettingsFolder);
                created = ScriptableObject.CreateInstance<XRGeneralSettingsPerBuildTarget>();
                AssetDatabase.CreateAsset(created, path);
            }

            EditorBuildSettings.AddConfigObject(XRGeneralSettings.k_SettingsKey, created, true);
            return created;
        }
    }
}
