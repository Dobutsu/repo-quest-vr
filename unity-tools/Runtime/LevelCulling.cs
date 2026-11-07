using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Reflection;
using UnityEngine;

namespace RepoQuest
{
    /// <summary>
    /// Draws less of the level on the Quest. The game is CPU-bound there, and in the built-in pipeline
    /// every drawn renderer costs the render thread; R.E.P.O. builds its levels from modules at runtime, so nothing is
    /// culled beyond the camera frustum (no baked occlusion, no cull distances).
    ///
    /// Only rendering changes, through Renderer.forceRenderingOff: objects stay active, so their scripts, physics,
    /// Photon views and everything other players see go on as before (multiplayer is unaffected). The game itself sets
    /// forceRenderingOff only on enemy parts (SemiTentacle fog culling), which are not under the level; a renderer
    /// that is already off when first seen is left alone, and only renderers this script turned off are turned on.
    ///
    ///   small objects (bounds diagonal under `size` m: props, valuables, items) beyond `small` m from the camera
    ///   anything beyond `far` m; 0 = automatic, 5 m past where the fog covers everything and at least 20 m (exactly at
    ///   the fog's end things were seen popping in). Each renderer is judged by its own current bounds, so a valuable carried out of a far room stays
    ///
    /// Settings in the game: Settings > VR > Rendering (RepoXR's config, patch 0009): Level Culling on/off, Small Object
    /// Distance (`small`), Room Distance (`far`); read again every second, so changes apply at once. An optional
    /// /sdcard/Android/data/io.repoxr.quest/files/culling.txt (lines "small=20", "size=1.5", "far=0", "off") overrides
    /// them, for tests.
    ///
    /// From crimecock's 0.5.0 fork (github.com/crimecock/repo-quest-vr, GPL-3.0); patch 0009 already adds its settings.
    /// </summary>
    public sealed class LevelCulling : MonoBehaviour
    {
        private const float DefaultSmallDistance = 20f;
        private const float DefaultSmallSize = 1.5f;
        private const float DefaultFarDistance = 0f; // automatic: AutoFarDistance
        private const float AutoFogMargin = 5f;
        private const float AutoMinFarDistance = 20f;
        private const float Hysteresis = 1.5f;          // shown again this much closer than hidden
        private const int RenderersPerFrame = 400;

        private float smallDistance = DefaultSmallDistance;
        private float smallSize = DefaultSmallSize;
        private float farDistance = DefaultFarDistance;
        private bool active = true;
        private bool fileOverride;
        private float settingsAt;
        private object repoXRConfig;
        private PropertyInfo cullingProperty, smallProperty, farProperty;

        private Type generatorType;
        private Type moduleType;
        private FieldInfo instanceField, generatedField, levelParentField, itemParentField;

        private sealed class Entry
        {
            public Renderer Renderer;
            public bool Small;
            public bool HiddenByUs;
        }

        private sealed class Root
        {
            public Transform Transform;
            public bool IsModule;
            public List<Entry> Entries = new List<Entry>();
        }

        private readonly List<Root> roots = new List<Root>();
        private readonly Dictionary<int, Entry> byId = new Dictionary<int, Entry>();
        private readonly List<Renderer> scratch = new List<Renderer>();
        private readonly HashSet<int> seen = new HashSet<int>();
        private List<Entry> fresh = new List<Entry>();
        private int levelId;
        private float scanAt = -1f;
        private int rootCursor, rootIndex, entryIndex;
        private int hiddenSmall, hiddenFar, frame;
        private float statsAt;

        /// <summary>PerfLog's A/B phase "level culling off": everything shown again until it is cleared</summary>
        public static bool Suspended { get; set; }

        /// <summary>The last full pass: renderers, hidden small and far, the far limit (QuestDiagnostics "culling")</summary>
        public static string LastPass { get; private set; } = "no pass yet";

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        private static void Install()
        {
            if (Application.platform != RuntimePlatform.Android)
                return;
            var go = new GameObject("RepoQuest Level Culling");
            DontDestroyOnLoad(go);
            go.hideFlags = HideFlags.HideAndDontSave;
            go.AddComponent<LevelCulling>();
        }

        private void Awake()
        {
            if (!ReadSettings())
            {
                Debug.Log("[RepoQuest] LevelCulling: off (culling.txt)");
                enabled = false;
                return;
            }
            foreach (var assembly in AppDomain.CurrentDomain.GetAssemblies())
            {
                var plugin = assembly.GetType("RepoXR.Plugin");
                if (plugin == null)
                    continue;
                var configProperty = plugin.GetProperty("Config", BindingFlags.Public | BindingFlags.Static | BindingFlags.DeclaredOnly);
                var configType = configProperty?.PropertyType;
                cullingProperty = configType?.GetProperty("LevelCulling");
                smallProperty = configType?.GetProperty("CullSmallDistance");
                farProperty = configType?.GetProperty("CullFarDistance");
                repoXRConfig = configProperty;
                break;
            }

            foreach (var assembly in AppDomain.CurrentDomain.GetAssemblies())
            {
                generatorType ??= assembly.GetType("LevelGenerator");
                moduleType ??= assembly.GetType("Module");
            }
            const BindingFlags all = BindingFlags.Public | BindingFlags.NonPublic;
            instanceField = generatorType?.GetField("Instance", all | BindingFlags.Static);
            generatedField = generatorType?.GetField("Generated", all | BindingFlags.Instance);
            levelParentField = generatorType?.GetField("LevelParent", all | BindingFlags.Instance);
            itemParentField = generatorType?.GetField("ItemParent", all | BindingFlags.Instance);
            if (instanceField == null || generatedField == null || levelParentField == null || moduleType == null)
            {
                Debug.LogWarning("[RepoQuest] LevelCulling: LevelGenerator/Module not found, off");
                enabled = false;
                return;
            }
            Debug.Log($"[RepoQuest] LevelCulling: small objects (< {smallSize} m) beyond {smallDistance} m, everything beyond " +
                      (farDistance > 0 ? $"{farDistance} m" : $"the fog + {AutoFogMargin} m, at least {AutoMinFarDistance} m"));
        }

        private bool ReadSettings()
        {
            try
            {
                var path = Path.Combine(Application.persistentDataPath, "culling.txt");
                if (!File.Exists(path))
                    return true;
                fileOverride = true;
                foreach (var raw in File.ReadAllLines(path))
                {
                    var line = raw.Trim().ToLowerInvariant();
                    if (line == "off")
                        return false;
                    var parts = line.Split('=');
                    if (parts.Length != 2 || !float.TryParse(parts[1].Trim(), NumberStyles.Float, CultureInfo.InvariantCulture, out var value))
                        continue;
                    switch (parts[0].Trim())
                    {
                        case "small": smallDistance = value; break;
                        case "size": smallSize = value; break;
                        case "far": farDistance = value; break;
                    }
                }
            }
            catch (Exception e)
            {
                Debug.LogWarning($"[RepoQuest] LevelCulling: culling.txt: {e.Message}");
            }
            return true;
        }

        private void Update()
        {
            try
            {
                Tick();
            }
            catch (Exception e)
            {
                // Never take the game down: show everything again and stop
                Debug.LogError($"[RepoQuest] LevelCulling stopped: {e}");
                RestoreAll();
                enabled = false;
            }
        }

        /// <summary>RepoXR's settings (Level Culling, Small Object Distance, Room Distance), once a second</summary>
        private void ReadGameSettings()
        {
            if (fileOverride || Time.realtimeSinceStartup < settingsAt)
                return;
            settingsAt = Time.realtimeSinceStartup + 1f;
            if (!(repoXRConfig is PropertyInfo configProperty) || cullingProperty == null)
                return;
            var config = configProperty.GetValue(null);
            if (config == null)
                return; // RepoXR not started yet
            var wasActive = active;
            active = EntryValue(cullingProperty, config) is bool on ? on : active;
            if (EntryValue(smallProperty, config) is int small)
                smallDistance = small;
            if (EntryValue(farProperty, config) is int far)
                farDistance = far;
            if (wasActive && !active)
                RestoreAll();
        }

        private static object EntryValue(PropertyInfo property, object config)
        {
            var entry = property?.GetValue(config);
            return entry?.GetType().GetProperty("Value")?.GetValue(entry);
        }

        private void Tick()
        {
            ReadGameSettings();
            if (!active)
                return;
            if (Suspended)
            {
                RestoreAll();
                return;
            }

            var generator = instanceField.GetValue(null) as UnityEngine.Object;
            var levelParent = generator ? levelParentField.GetValue(generator) as GameObject : null;
            var generated = generator && (bool)generatedField.GetValue(generator);
            if (!generated || !levelParent)
            {
                if (roots.Count > 0)
                    Clear();
                return;
            }

            if (levelParent.GetInstanceID() != levelId)
            {
                // A new level: let props, valuables and items spawn first
                Clear();
                levelId = levelParent.GetInstanceID();
                scanAt = Time.realtimeSinceStartup + 2f;
                return;
            }
            if (scanAt > 0f)
            {
                if (Time.realtimeSinceStartup < scanAt)
                    return;
                scanAt = -1f;
                Scan(levelParent, generator);
            }

            var camera = Camera.main;
            if (!camera || roots.Count == 0)
                return;
            var eye = camera.transform.position;
            var farLimit = farDistance > 0f ? farDistance : AutoFarDistance();

            // Every fourth frame one root's renderer list is refreshed (things spawn, break, get destroyed)
            if (++frame % 4 == 0)
            {
                Refresh(roots[rootCursor]);
                rootCursor = (rootCursor + 1) % roots.Count;
            }

            var budget = RenderersPerFrame;
            while (budget > 0)
            {
                if (rootIndex >= roots.Count)
                {
                    rootIndex = 0;
                    LastPass = $"{byId.Count} renderers, hidden {hiddenSmall} small, {hiddenFar} far (limit {farLimit:0} m)";
                    if (Time.realtimeSinceStartup >= statsAt)
                    {
                        statsAt = Time.realtimeSinceStartup + 30f;
                        Debug.Log($"[RepoQuest] LevelCulling: {byId.Count} renderers, hidden {hiddenSmall} small, " +
                                  $"{hiddenFar} far (limit {farLimit:0} m)");
                    }
                    hiddenSmall = hiddenFar = 0;
                    break;
                }
                var root = roots[rootIndex];
                for (; entryIndex < root.Entries.Count && budget > 0; entryIndex++, budget--)
                    Evaluate(root.Entries[entryIndex], farLimit, eye);
                if (entryIndex >= root.Entries.Count)
                {
                    entryIndex = 0;
                    rootIndex++;
                }
            }
        }

        private void Evaluate(Entry entry, float farLimit, Vector3 eye)
        {
            var renderer = entry.Renderer;
            if (!renderer)
                return;

            var sqr = renderer.bounds.SqrDistance(eye);
            var back = entry.HiddenByUs ? Hysteresis : 0f;
            var hide = false;
            if (farLimit > 0f && sqr > (farLimit - back) * (farLimit - back))
            {
                hide = true;
                hiddenFar++;
            }
            else if (entry.Small && sqr > (smallDistance - back) * (smallDistance - back))
            {
                hide = true;
                hiddenSmall++;
            }

            if (hide && !entry.HiddenByUs)
            {
                if (renderer.forceRenderingOff)
                    return; // the game's own
                renderer.forceRenderingOff = true;
                entry.HiddenByUs = true;
            }
            else if (!hide && entry.HiddenByUs)
            {
                renderer.forceRenderingOff = false;
                entry.HiddenByUs = false;
            }
        }

        private void Scan(GameObject levelParent, UnityEngine.Object generator)
        {
            var modules = 0;
            foreach (var module in levelParent.GetComponentsInChildren(moduleType, true))
            {
                roots.Add(new Root { Transform = ((Component)module).transform, IsModule = true });
                modules++;
            }
            // What lies under the level outside of the modules: each such child of the level is a root of its own
            foreach (Transform child in levelParent.transform)
                if (!child.GetComponentInChildren(moduleType, true))
                    roots.Add(new Root { Transform = child, IsModule = false });
            if (itemParentField?.GetValue(generator) is GameObject items && items)
                roots.Add(new Root { Transform = items.transform, IsModule = false });

            foreach (var root in roots)
                Refresh(root);
            Debug.Log($"[RepoQuest] LevelCulling: level with {modules} modules, {roots.Count} roots, {byId.Count} renderers");
        }

        /// <summary>The renderers under a root now</summary>
        private void Refresh(Root root)
        {
            if (!root.Transform)
            {
                foreach (var entry in root.Entries)
                    Release(entry);
                root.Entries.Clear();
                return;
            }

            scratch.Clear();
            seen.Clear();
            fresh.Clear();
            root.Transform.GetComponentsInChildren(true, scratch);
            foreach (var renderer in scratch)
            {
                if (!(renderer is MeshRenderer) && !(renderer is SkinnedMeshRenderer))
                    continue;
                var id = renderer.GetInstanceID();
                if (!seen.Add(id))
                    continue;
                if (!byId.TryGetValue(id, out var entry))
                {
                    // Sized while visible only: a switched-off object has empty bounds and would pass for small
                    if (!renderer.enabled || !renderer.gameObject.activeInHierarchy)
                        continue;
                    var size = renderer.bounds.size.magnitude;
                    entry = new Entry { Renderer = renderer, Small = size < smallSize };
                    byId[id] = entry;
                }
                fresh.Add(entry);
            }

            // Gone from this root (destroyed, or carried elsewhere): never leave one hidden
            foreach (var old in root.Entries)
                if (!old.Renderer || !seen.Contains(old.Renderer.GetInstanceID()))
                    Release(old);
            // Swap the lists instead of copying
            var previous = root.Entries;
            root.Entries = fresh;
            fresh = previous;
        }

        private void Release(Entry entry)
        {
            if (entry.HiddenByUs && entry.Renderer)
                entry.Renderer.forceRenderingOff = false;
            entry.HiddenByUs = false;
            if (entry.Renderer)
                byId.Remove(entry.Renderer.GetInstanceID());
        }

        /// <summary>
        /// Room Distance 0: a little past the fog, and never closer than 20 m. Exactly at the fog's end doors and walls were
        /// seen appearing out of it on Sunstone Valley Resort (fog 10-15 m); 20 m there looked seamless (b28). 0 without fog
        /// </summary>
        private static float AutoFarDistance()
        {
            var fog = FogDistance();
            return fog > 0f ? Mathf.Max(fog + AutoFogMargin, AutoMinFarDistance) : 0f;
        }

        /// <summary>
        /// How far anything can be seen through the fog: at that distance the fog covers more than 254/255 of the colour.
        /// 0 when there is no fog (modules are then never hidden by distance)
        /// </summary>
        private static float FogDistance()
        {
            if (!RenderSettings.fog)
                return 0f;
            switch (RenderSettings.fogMode)
            {
                case FogMode.Linear:
                    return RenderSettings.fogEndDistance;
                case FogMode.Exponential:
                    return RenderSettings.fogDensity > 0f ? Mathf.Log(255f) / RenderSettings.fogDensity : 0f;
                default:
                    return RenderSettings.fogDensity > 0f ? Mathf.Sqrt(Mathf.Log(255f)) / RenderSettings.fogDensity : 0f;
            }
        }

        private void RestoreAll()
        {
            foreach (var entry in byId.Values)
            {
                if (entry.HiddenByUs && entry.Renderer)
                    entry.Renderer.forceRenderingOff = false;
                entry.HiddenByUs = false;
            }
        }

        private void Clear()
        {
            RestoreAll();
            roots.Clear();
            byId.Clear();
            rootCursor = rootIndex = entryIndex = 0;
            levelId = 0;
        }

        private void OnDestroy() => RestoreAll();
    }
}
