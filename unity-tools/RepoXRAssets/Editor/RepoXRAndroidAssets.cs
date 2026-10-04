using System;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.AddressableAssets;
using UnityEditor.AddressableAssets.Settings;
using UnityEngine;

namespace RepoQuest.Editor
{
    /// <summary>
    /// Builds RepoXR's runtime assets (RepoXR-Unity project) for Android: the "repoxrassets" AssetBundle and the
    /// mod's Addressables localization, laid out like the plugin folder of the PC release.
    /// Unity -batchmode -buildTarget Android -projectPath &lt;RepoXR-Unity copy&gt;
    ///       -executeMethod RepoQuest.Editor.RepoXRAndroidAssets.Build -outDir &lt;dir&gt;
    /// </summary>
    public static class RepoXRAndroidAssets
    {
        private const string AddressablesProfile = "RepoXR";

        public static void Build()
        {
            try
            {
                var outDir = ArgumentValue("-outDir") ?? throw new ArgumentException("-outDir is required");
                Directory.CreateDirectory(outDir);

                var bundleDir = Path.Combine(Path.GetTempPath(), "repoxr-android-bundles");
                Directory.CreateDirectory(bundleDir);
                var manifest = BuildPipeline.BuildAssetBundles(bundleDir, BuildAssetBundleOptions.ChunkBasedCompression,
                    BuildTarget.Android);
                if (manifest == null || !manifest.GetAllAssetBundles().Contains("repoxrassets"))
                    throw new Exception("repoxrassets bundle was not built");
                File.Copy(Path.Combine(bundleDir, "repoxrassets"), Path.Combine(outDir, "repoxrassets"), true);

                // The mod loads its localization through Addressables from <plugin dir>/Addressables
                var settings = AddressableAssetSettingsDefaultObject.Settings;
                settings.activeProfileId = settings.profileSettings.GetProfileId(AddressablesProfile);
                // Bundles are named by content hash; old ones stay in the output folder and would be copied along
                var addressablesBuild = Path.Combine("Addressables", "Output", "Android");
                if (Directory.Exists(addressablesBuild))
                    Directory.Delete(addressablesBuild, true);
                AddressableAssetSettings.BuildPlayerContent(out var result);
                if (!string.IsNullOrEmpty(result.Error))
                    throw new Exception($"Addressables build failed: {result.Error}");

                var addressablesOut = Path.Combine(outDir, "Addressables");
                if (Directory.Exists(addressablesOut))
                    Directory.Delete(addressablesOut, true);
                CopyDirectory(Path.Combine("Addressables", "Output", "Android"), addressablesOut);

                Debug.Log($"[RepoXRAndroidAssets] built into {outDir}");
                EditorApplication.Exit(0);
            }
            catch (Exception e)
            {
                Debug.LogError($"[RepoXRAndroidAssets] {e}");
                EditorApplication.Exit(1);
            }
        }

        private static void CopyDirectory(string from, string to)
        {
            Directory.CreateDirectory(to);
            foreach (var file in Directory.GetFiles(from))
                File.Copy(file, Path.Combine(to, Path.GetFileName(file)), true);
        }

        private static string ArgumentValue(string name)
        {
            var args = Environment.GetCommandLineArgs();
            var index = Array.IndexOf(args, name);
            return index >= 0 && index + 1 < args.Length ? args[index + 1] : null;
        }
    }
}
