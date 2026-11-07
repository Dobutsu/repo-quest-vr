using System;
using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEditor.Build;
using UnityEditor.Build.Reporting;
using UnityEditor.Rendering;
using UnityEngine;
using UnityEngine.Rendering;

namespace RepoQuest.Editor
{
    /// <summary>
    /// Drops shader variants the Quest can never select, so the first build compiles fewer of them (a player's first build
    /// compiled ~30,500 variants, hours on a slow PC). Only keywords no code path on the Quest turns on:
    ///   - stereo modes other than multiview: UNITY_SINGLE_PASS_STEREO and STEREO_INSTANCING_ON are the double-wide and
    ///     instanced single-pass of desktop VR (Android Vulkan renders single-pass as multiview, STEREO_MULTIVIEW_ON);
    ///     STEREO_DOUBLEWIDE_TARGET is Post Processing's double-wide target, which our patched PostProcessRenderContext
    ///     never selects on Android (the eye texture array is "instanced", see patch_packages.py);
    ///   - SHADOWS_SOFT, when the Android quality level allows hard shadows only (Medium; nothing changes the level at
    ///     runtime): Unity then renders soft-shadowed lights with hard shadows and never sets the keyword.
    /// Deferred passes stay (the avatar preview camera in the menus is deferred), as do fog and lightmap modes (Unity
    /// strips those itself from the scenes). If a variant turned out to be needed after all, Unity draws the closest one
    /// (strict variant matching is off) rather than an error shader.
    /// -noShaderStripping on the command line turns it off. Unity caches the stripped set with the compiled shaders:
    /// a change here takes effect for shaders that get compiled again (a clean Library/ShaderCache).
    /// </summary>
    public sealed class ShaderVariantStripper : IPreprocessShaders, IPostprocessBuildWithReport
    {
        private static readonly string[] AlwaysStripped = { "UNITY_SINGLE_PASS_STEREO", "STEREO_INSTANCING_ON", "STEREO_DOUBLEWIDE_TARGET" };

        private static HashSet<string> stripped;
        private static readonly Dictionary<string, (int In, int Out)> Counts = new();

        public int callbackOrder => 0;

        public void OnProcessShader(Shader shader, ShaderSnippetData snippet, IList<ShaderCompilerData> data)
        {
            if (EditorUserBuildSettings.activeBuildTarget != BuildTarget.Android || Disabled || data.Count == 0)
                return;
            stripped ??= StrippedKeywords();

            var before = data.Count;
            for (var i = data.Count - 1; i >= 0; i--)
            {
                if (data[i].shaderKeywordSet.GetShaderKeywords().Any(k => stripped.Contains(k.name)))
                    data.RemoveAt(i);
            }

            var key = shader.name;
            Counts.TryGetValue(key, out var count);
            Counts[key] = (count.In + before, count.Out + data.Count);
        }

        public void OnPostprocessBuild(BuildReport report)
        {
            if (report.summary.platform != BuildTarget.Android)
                return;
            if (Disabled)
            {
                Debug.Log("[ShaderStrip] off (-noShaderStripping)");
                return;
            }

            var total = (In: Counts.Values.Sum(c => c.In), Out: Counts.Values.Sum(c => c.Out));
            Debug.Log($"[ShaderStrip] keywords {string.Join(", ", stripped ?? new HashSet<string>())}: " +
                      $"{total.In - total.Out} of {total.In} variants dropped in {Counts.Count} shaders compiled this build");
            foreach (var (name, count) in Counts.Where(c => c.Value.In != c.Value.Out).OrderByDescending(c => c.Value.In - c.Value.Out).Take(20))
                Debug.Log($"[ShaderStrip]   {name}: {count.In} -> {count.Out}");
            Counts.Clear();
        }

        private static bool Disabled => Environment.GetCommandLineArgs().Contains("-noShaderStripping");

        private static HashSet<string> StrippedKeywords()
        {
            var keywords = new HashSet<string>(AlwaysStripped);
            var shadows = AndroidShadowQuality();
            if (shadows is ShadowQuality.HardOnly or ShadowQuality.Disable)
                keywords.Add("SHADOWS_SOFT");
            Debug.Log($"[ShaderStrip] Android shadows {shadows?.ToString() ?? "unknown"}; dropping variants with {string.Join(", ", keywords)}");
            return keywords;
        }

        /// <summary>The shadows setting of Android's default quality level, read from ProjectSettings/QualitySettings.asset</summary>
        private static ShadowQuality? AndroidShadowQuality()
        {
            var asset = AssetDatabase.LoadAllAssetsAtPath("ProjectSettings/QualitySettings.asset").FirstOrDefault();
            if (asset == null)
                return null;
            var settings = new SerializedObject(asset);
            var defaults = settings.FindProperty("m_PerPlatformDefaultQuality");
            var levels = settings.FindProperty("m_QualitySettings");
            if (defaults == null || levels == null)
                return null;

            for (var i = 0; i < defaults.arraySize; i++)
            {
                var pair = defaults.GetArrayElementAtIndex(i);
                if (pair.FindPropertyRelative("first")?.stringValue != "Android")
                    continue;
                var level = pair.FindPropertyRelative("second").intValue;
                if (level < 0 || level >= levels.arraySize)
                    return null;
                return (ShadowQuality)levels.GetArrayElementAtIndex(level).FindPropertyRelative("shadows").intValue;
            }
            return null;
        }
    }
}
