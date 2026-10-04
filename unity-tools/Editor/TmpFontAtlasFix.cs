using System.Linq;
using TMPro;
using UnityEditor;
using UnityEngine;

namespace RepoQuest.Editor
{
    /// <summary>
    /// Dynamic TMP font assets that were never populated in the original build (Noto Sans JP/KR/TC/Thai/Arabic,
    /// LiberationSans Fallback) had 0x0 atlas textures, which AssetRipper does not export. Without an atlas TMP cannot
    /// add glyphs at runtime (and its pre-build step throws). Each gets a fresh empty atlas, like a newly created font
    /// asset has; TMP resizes it on first use.
    /// </summary>
    public static class TmpFontAtlasFix
    {
        public static void Apply()
        {
            var fixedFonts = 0;
            foreach (var guid in AssetDatabase.FindAssets("t:TMP_FontAsset"))
            {
                var path = AssetDatabase.GUIDToAssetPath(guid);
                var font = AssetDatabase.LoadAssetAtPath<TMP_FontAsset>(path);
                if (font == null || font.atlasPopulationMode != AtlasPopulationMode.Dynamic)
                    continue;
                if (font.atlasTextures != null && font.atlasTextures.Length > 0 && font.atlasTextures.All(t => t != null))
                    continue;

                var atlas = new Texture2D(0, 0, TextureFormat.Alpha8, false) { name = $"{font.name} Atlas" };
                AssetDatabase.AddObjectToAsset(atlas, font);
                font.atlasTextures = new[] { atlas };
                if (font.material != null)
                    font.material.SetTexture(ShaderUtilities.ID_MainTex, atlas);
                font.ClearFontAssetData(true);

                EditorUtility.SetDirty(font);
                fixedFonts++;
            }

            AssetDatabase.SaveAssets();
            Debug.Log($"[TmpFontAtlasFix] empty atlases created for {fixedFonts} dynamic font assets");
        }
    }
}
