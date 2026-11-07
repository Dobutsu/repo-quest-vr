using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEngine;

namespace RepoQuest
{
    /// <summary>
    /// The local player's VR arms, and the arm cosmetics RepoXR puts on them, are drawn right in front of the eyes and
    /// can fill the view. With the game's Hurtable shaders a cosmetic sleeve held up took 18-24 ms of GPU time per frame
    /// instead of ~9.5 on a Quest 3 (b28, 40-50 FPS): an alpha-clipped surface shader with a forward add pass for the
    /// flashlight next to it (and its shadows) and all 16 lamps per pixel. Whatever is on the rig gets the lighter copy
    /// of its shader that scripts/generate_shaders.py makes (RepoQuest/Arms/..., in Resources): no forward add pass, the
    /// 4 nearest lamps. Other players' arms are far away and keep the game's shaders.
    ///
    /// A material the game made for this renderer ("(Instance)": the player's colour, the hurt flash) gets the shader in
    /// place, so the game's later changes to it still show; a shared asset material is copied first, so the same
    /// cosmetic on other players and in the menus is untouched. Checked once a second: cosmetics change, and RepoXR
    /// builds the rig again for each level.
    /// files/arm-shaders-off puts everything back (an A/B test without a build); log lines start with [ArmShaders].
    /// </summary>
    public sealed class ArmShaders : MonoBehaviour
    {
        private const string Prefix = "RepoQuest/Arms/";
        private const float ScanSeconds = 1f;

        private readonly Dictionary<Shader, Shader> variants = new(); // game shader -> arms copy (null: none)
        private readonly Dictionary<Material, Material> copies = new(); // shared material -> its arms copy
        private readonly Dictionary<Material, Shader> inPlace = new(); // instance material -> its own shader
        private readonly Dictionary<Renderer, Material[]> originals = new(); // renderer -> its materials before
        private Type rigType;
        private string offPath;
        private float nextScan;
        private bool off;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        private static void Install()
        {
            if (Application.platform != RuntimePlatform.Android)
                return;
            var go = new GameObject("RepoQuest Arm Shaders");
            DontDestroyOnLoad(go);
            go.hideFlags = HideFlags.HideAndDontSave;
            go.AddComponent<ArmShaders>();
        }

        private void Awake()
        {
            offPath = Path.Combine(Application.persistentDataPath, "arm-shaders-off");
            rigType = AppDomain.CurrentDomain.GetAssemblies()
                .Select(a => a.GetType("RepoXR.Player.VRRig")).FirstOrDefault(t => t != null);
            if (rigType == null)
            {
                Debug.Log("[ArmShaders] off: no RepoXR.Player.VRRig");
                enabled = false;
            }
        }

        private void Update()
        {
            if (Time.unscaledTime < nextScan)
                return;
            nextScan = Time.unscaledTime + ScanSeconds;
            try
            {
                var wantOff = File.Exists(offPath);
                if (wantOff != off)
                {
                    off = wantOff;
                    if (off)
                        RestoreAll();
                    Debug.Log($"[ArmShaders] {(off ? "off (files/arm-shaders-off)" : "on")}");
                }
                if (!off)
                    Apply();
            }
            catch (Exception e)
            {
                Debug.LogError($"[ArmShaders] off: {e}");
                RestoreAll();
                enabled = false;
            }
        }

        private void Apply()
        {
            // The rig of an earlier level is gone with its renderers and their material instances
            foreach (var renderer in originals.Keys.Where(r => !r).ToList())
                originals.Remove(renderer);
            foreach (var material in inPlace.Keys.Where(m => !m).ToList())
                inPlace.Remove(material);

            var changed = new List<string>();
            foreach (var rig in FindObjectsByType(rigType, FindObjectsInactive.Include, FindObjectsSortMode.None))
            {
                if (!(rig is Component component))
                    continue;
                foreach (var renderer in component.GetComponentsInChildren<Renderer>(true))
                {
                    var materials = renderer.sharedMaterials;
                    Material[] before = null;
                    for (var i = 0; i < materials.Length; i++)
                    {
                        var material = materials[i];
                        if (!material || material.shader.name.StartsWith(Prefix, StringComparison.Ordinal))
                            continue;
                        var variant = Variant(material.shader);
                        if (!variant)
                            continue;
                        if (material.name.EndsWith("(Instance)", StringComparison.Ordinal))
                        {
                            inPlace[material] = material.shader;
                            var queue = material.renderQueue;
                            material.shader = variant;
                            material.renderQueue = queue;
                        }
                        else
                        {
                            before ??= (Material[])materials.Clone();
                            if (!copies.TryGetValue(material, out var copy) || !copy)
                            {
                                copy = new Material(material) { name = material.name + " (Arms)", shader = variant };
                                copy.renderQueue = material.renderQueue;
                                copies[material] = copy;
                            }
                            materials[i] = copy;
                        }
                        changed.Add($"{renderer.name}: {material.name} ({variant.name.Substring(Prefix.Length)})");
                    }
                    if (before != null)
                    {
                        if (!originals.ContainsKey(renderer))
                            originals[renderer] = before;
                        renderer.sharedMaterials = materials;
                    }
                }
            }
            if (changed.Count > 0)
                Debug.Log($"[ArmShaders] {changed.Count} materials on the VR arms: {string.Join(", ", changed)}");
        }

        private Shader Variant(Shader shader)
        {
            if (!variants.TryGetValue(shader, out var variant))
                variants[shader] = variant = Shader.Find(Prefix + shader.name);
            return variant;
        }

        private void RestoreAll()
        {
            foreach (var (renderer, materials) in originals)
                if (renderer)
                    renderer.sharedMaterials = materials;
            originals.Clear();
            foreach (var (material, shader) in inPlace)
            {
                if (!material)
                    continue;
                var queue = material.renderQueue;
                material.shader = shader;
                material.renderQueue = queue;
            }
            inPlace.Clear();
        }
    }
}
