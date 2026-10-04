using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.AddressableAssets;
using UnityEditor.AddressableAssets.Settings;
using UnityEditor.AddressableAssets.Settings.GroupSchemas;
using UnityEditor.Localization;
using UnityEngine;
using UnityEngine.Localization.Settings;

namespace RepoQuest.Editor
{
    /// <summary>
    /// Recreates the Addressables setup of the game from the entries decoded out of its player catalog
    /// (scripts/addressables_catalog.py): same groups (one per bundle), addresses and labels. The editor-side
    /// AddressableAssetSettings never ship with a game, so the export does not have them.
    /// </summary>
    public static class AddressablesRestore
    {
        [Serializable]
        private class Entry
        {
            public string path;
            public string bundle;
            public string address;
            public string[] labels;
        }

        [Serializable]
        private class EntryList
        {
            public Entry[] items;
        }

        private const string LocalizationSettingsPath = "Assets/Localization/Localization Settings.asset";

        public static void Apply(string entriesJson)
        {
            var entries = JsonUtility.FromJson<EntryList>("{\"items\":" + File.ReadAllText(entriesJson) + "}").items;
            var settings = AddressableAssetSettingsDefaultObject.GetSettings(true);

            var missing = 0;
            foreach (var bundleEntries in entries.GroupBy(e => e.bundle ?? "defaultlocalgroup"))
            {
                var group = GroupForBundle(settings, bundleEntries.Key);
                foreach (var item in bundleEntries)
                {
                    var guid = AssetDatabase.AssetPathToGUID(item.path);
                    if (string.IsNullOrEmpty(guid))
                    {
                        Debug.LogError($"[AddressablesRestore] asset not found: {item.path}");
                        missing++;
                        continue;
                    }

                    var entry = settings.CreateOrMoveEntry(guid, group, false, false);
                    entry.SetAddress(item.address, false);
                    foreach (var label in item.labels ?? Array.Empty<string>())
                    {
                        settings.AddLabel(label, false);
                        entry.SetLabel(label, true, true, false);
                    }
                }
            }

            // Localization finds its settings through this editor config object
            var localizationSettings = AssetDatabase.LoadAssetAtPath<LocalizationSettings>(LocalizationSettingsPath);
            if (localizationSettings != null)
                LocalizationEditorSettings.ActiveLocalizationSettings = localizationSettings;
            else
                Debug.LogError($"[AddressablesRestore] {LocalizationSettingsPath} not found");

            EditorUtility.SetDirty(settings);
            AssetDatabase.SaveAssets();
            Debug.Log($"[AddressablesRestore] {entries.Length - missing} entries in {settings.groups.Count} groups ({missing} missing)");
        }

        public static bool BuildContent()
        {
            AddressableAssetSettings.BuildPlayerContent(out var result);
            if (!string.IsNullOrEmpty(result.Error))
            {
                Debug.LogError($"[AddressablesRestore] content build failed: {result.Error}");
                return false;
            }

            Debug.Log($"[AddressablesRestore] content built in {result.Duration:F1}s");
            return true;
        }

        /// <summary>"localization-locales_assets_all.bundle" -> group "localization-locales"; the hashed default group
        /// bundle maps onto the settings' default group</summary>
        private static AddressableAssetGroup GroupForBundle(AddressableAssetSettings settings, string bundle)
        {
            if (bundle.StartsWith("defaultlocalgroup", StringComparison.Ordinal))
                return settings.DefaultGroup;

            var name = bundle.Replace(".bundle", "").Replace("_assets_all", "");
            var group = settings.FindGroup(name);
            if (group != null)
                return group;

            group = settings.CreateGroup(name, false, false, false, new List<AddressableAssetGroupSchema>(),
                typeof(BundledAssetGroupSchema), typeof(ContentUpdateGroupSchema));
            var schema = group.GetSchema<BundledAssetGroupSchema>();
            schema.BundleMode = BundledAssetGroupSchema.BundlePackingMode.PackTogether;
            schema.BuildPath.SetVariableByName(settings, AddressableAssetSettings.kLocalBuildPath);
            schema.LoadPath.SetVariableByName(settings, AddressableAssetSettings.kLocalLoadPath);
            return group;
        }
    }
}
