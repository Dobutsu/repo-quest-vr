using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using UnityEngine;
using UnityEngine.Rendering;

namespace RepoQuest
{
    /// <summary>
    /// The shader oracle: renders prefabs from Resources one by one, far below the scene, under fixed lighting, so the
    /// same list rendered by the PC game (the original shaders) and by the port (the generated ones) can be compared
    /// picture by picture (scripts/shader_gallery.py).
    /// Nothing of a prefab runs: it is instantiated under an inactive parent (no Awake), and only copies of its visible
    /// renderers are drawn: meshes, skinned meshes in their rest pose (bound to the inactive clone's bones) and sprites.
    /// Lighting is one directional light, flat ambient light and a grey reflection probe (the PC game ignores a custom
    /// reflection set through RenderSettings at runtime); no shadows, no post-processing, forced anisotropic filtering
    /// (the PC's Ultra forces it, the Quest's Medium leaves it to the textures) and no point lights (on the Quest, lamps are evaluated by our own shader code, not by Unity's forward passes).
    /// The same file is compiled into the APK (QuestDiagnostics), the PC plugin (tools/ShaderGallery.Pc) and the
    /// editor (Editor/ShaderGalleryEditor.cs).
    /// Level modules (Resources/level/) are seen from high above, framed by their floor area, at a higher resolution.
    /// Output per prefab: &lt;key&gt;.png and &lt;key&gt;.txt (its renderers, materials and shaders); index.tsv; done.
    /// </summary>
    public static class ShaderGallery
    {
        public const int Size = 512;
        public const int LevelSize = 1024;  // a whole room in one picture
        private const int Layer = 31;
        private const float FieldOfView = 30f;
        private static readonly Vector3 Origin = new Vector3(0, -2000, 0);
        private static readonly Color Background = new Color(0.25f, 0.3f, 0.35f, 1);
        private static readonly Color Ambient = new Color(0.35f, 0.35f, 0.35f, 1);
        private static readonly Color ReflectionGrey = new Color(0.5f, 0.5f, 0.5f, 1);
        // Front-right-above for objects; level modules from high above, with the near plane cutting off the ceiling
        private static readonly Vector3 ObjectView = new Vector3(0.6f, 0.45f, 1f).normalized;
        private static readonly Vector3 LevelView = new Vector3(0.25f, 1.6f, 0.6f).normalized;
        private const float CeilingCut = 1.2f;

        public static string KeyOf(string path)
        {
            var key = new StringBuilder();
            foreach (var c in path)
                key.Append(c == '/' ? "__" : char.IsLetterOrDigit(c) || c == '-' || c == '.' ? c.ToString() : "_");
            return key.ToString();
        }

        /// <summary>One prefab per step (yield between them); unloads unused assets every 25 prefabs</summary>
        public static IEnumerator Run(IList<string> paths, string outDir, Action<string> log)
        {
            Directory.CreateDirectory(outDir);
            var done = Path.Combine(outDir, "done");
            if (File.Exists(done))  // Mono under Wine throws on deleting a missing file
                File.Delete(done);
            var stage = new Stage();
            var index = new StringBuilder("key\tpath\tstatus\tms\trenderers\n");
            var started = Time.realtimeSinceStartup;
            try
            {
                for (var i = 0; i < paths.Count; i++)
                {
                    var path = paths[i];
                    var key = KeyOf(path);
                    var t0 = Time.realtimeSinceStartup;
                    string status;
                    var count = 0;
                    try
                    {
                        status = stage.Render(path, Path.Combine(outDir, key), out count);
                    }
                    catch (Exception e)
                    {
                        status = "error: " + e.GetType().Name + ": " + e.Message.Replace('\t', ' ').Replace('\n', ' ');
                        stage.Clear();
                    }
                    index.Append($"{key}\t{path}\t{status}\t{(Time.realtimeSinceStartup - t0) * 1000:0}\t{count}\n");
                    if (i % 100 == 99)
                        log($"[ShaderGallery] {i + 1}/{paths.Count}");
                    if (i % 25 == 24)
                        yield return Resources.UnloadUnusedAssets();
                    else
                        yield return null;
                }
            }
            finally
            {
                stage.Dispose();
                File.WriteAllText(Path.Combine(outDir, "index.tsv"), index.ToString());
            }
            File.WriteAllText(done,
                              $"{paths.Count} prefabs in {Time.realtimeSinceStartup - started:0} s, Unity {Application.unityVersion}, " +
                              $"{SystemInfo.graphicsDeviceType}, {SystemInfo.graphicsDeviceName}, {Graphics.activeTier} " +
                              $"(Standard's BRDF depends on it), quality {QualitySettings.names[QualitySettings.GetQualityLevel()]}\n");
            log($"[ShaderGallery] done: {paths.Count} prefabs to {outDir}");
        }

        private sealed class Stage : IDisposable
        {
            private readonly GameObject root;
            private readonly Transform holder;   // inactive: instantiated prefabs never wake up
            private readonly Transform copies;
            private readonly Camera camera;
            private readonly Light light;
            private readonly RenderTexture target, levelTarget;
            private readonly Texture2D readback, levelReadback;
            private readonly Cubemap reflection;
            private readonly ReflectionProbe probe;
            private GameObject clone;

            public Stage()
            {
                root = new GameObject("RepoQuest Shader Gallery");
                root.transform.position = Origin;
                if (Application.isPlaying)
                    UnityEngine.Object.DontDestroyOnLoad(root);
                holder = new GameObject("prefab").transform;
                holder.gameObject.SetActive(false);
                holder.SetParent(root.transform, false);
                copies = new GameObject("copies").transform;
                copies.SetParent(root.transform, false);

                target = new RenderTexture(Size, Size, 24, RenderTextureFormat.ARGB32) { antiAliasing = 1 };
                readback = new Texture2D(Size, Size, TextureFormat.RGBA32, false);
                levelTarget = new RenderTexture(LevelSize, LevelSize, 24, RenderTextureFormat.ARGB32) { antiAliasing = 1 };
                levelReadback = new Texture2D(LevelSize, LevelSize, TextureFormat.RGBA32, false);
                reflection = new Cubemap(8, TextureFormat.RGBA32, false);
                var grey = Enumerable.Repeat(ReflectionGrey, 64).ToArray();
                foreach (CubemapFace face in Enum.GetValues(typeof(CubemapFace)))
                    if (face != CubemapFace.Unknown)
                        reflection.SetPixels(grey, face);
                reflection.Apply(false);

                probe = new GameObject("reflection").AddComponent<ReflectionProbe>();
                probe.transform.SetParent(root.transform, false);
                probe.mode = ReflectionProbeMode.Custom;
                probe.customBakedTexture = reflection;
                probe.size = Vector3.one * 1000;
                probe.importance = 1000;
                probe.blendDistance = 0;
                probe.boxProjection = false;
                probe.intensity = 1;

                camera = new GameObject("camera").AddComponent<Camera>();
                camera.transform.SetParent(root.transform, false);
                camera.enabled = false;
                camera.cullingMask = 1 << Layer;
                camera.clearFlags = CameraClearFlags.SolidColor;
                camera.backgroundColor = Background;
                camera.allowHDR = false;
                camera.allowMSAA = false;
                camera.renderingPath = RenderingPath.Forward;
                camera.useOcclusionCulling = false;
                camera.fieldOfView = FieldOfView;
                camera.stereoTargetEye = StereoTargetEyeMask.None;

                light = new GameObject("light").AddComponent<Light>();
                light.transform.SetParent(root.transform, false);
                light.transform.rotation = Quaternion.Euler(40, 220, 0);
                light.type = LightType.Directional;
                light.color = Color.white;
                light.intensity = 1;
                light.shadows = LightShadows.None;
                light.renderMode = LightRenderMode.ForcePixel;
                light.cullingMask = 1 << Layer;
            }

            /// <summary>Draws one prefab into &lt;file&gt;.png, describes it in &lt;file&gt;.txt; returns the status</summary>
            public string Render(string path, string file, out int count)
            {
                count = 0;
                var prefab = Resources.Load<GameObject>(path);
                if (!prefab)
                    return "missing";
                clone = UnityEngine.Object.Instantiate(prefab, holder, false);
                var drawn = CopyRenderers(clone.transform);
                count = drawn.Count;
                if (drawn.Count == 0)
                {
                    Clear();
                    return "empty";
                }

                var bounds = drawn[0].Bounds;
                foreach (var d in drawn.Skip(1))
                    bounds.Encapsulate(d.Bounds);
                var level = path.StartsWith("level/", StringComparison.OrdinalIgnoreCase);
                Frame(bounds, level);
                var image = level ? levelReadback : readback;
                camera.targetTexture = level ? levelTarget : target;
                Draw(image);
                camera.targetTexture = null;
                File.WriteAllBytes(file + ".png", image.EncodeToPNG());
                File.WriteAllText(file + ".txt", string.Concat(drawn.Select(d => d.Description)));
                Clear();
                return "ok";
            }

            private struct Drawn
            {
                public Bounds Bounds;
                public string Description;
            }

            private List<Drawn> CopyRenderers(Transform prefab)
            {
                // LOD groups: only LOD 0 is drawn (the group itself stays inactive with the clone)
                var otherLods = new HashSet<Renderer>();
                foreach (var group in prefab.GetComponentsInChildren<LODGroup>(true))
                {
                    var lods = group.GetLODs();
                    var first = lods.Length > 0 ? new HashSet<Renderer>(lods[0].renderers.Where(r => r)) : new HashSet<Renderer>();
                    foreach (var lod in lods.Skip(1))
                        otherLods.UnionWith(lod.renderers.Where(r => r && !first.Contains(r)));
                }

                var drawn = new List<Drawn>();
                foreach (var original in prefab.GetComponentsInChildren<Renderer>(true))
                {
                    if (!original.enabled || otherLods.Contains(original) || !ActiveUnder(original.transform, prefab))
                        continue;
                    var go = new GameObject(original.name) { layer = Layer };
                    go.transform.SetParent(copies, false);
                    go.transform.SetPositionAndRotation(original.transform.position, original.transform.rotation);
                    go.transform.localScale = original.transform.lossyScale;
                    Renderer copy;
                    Mesh mesh = null;
                    switch (original)
                    {
                        case SkinnedMeshRenderer skinned when skinned.sharedMesh:
                            var smr = go.AddComponent<SkinnedMeshRenderer>();
                            smr.sharedMesh = mesh = skinned.sharedMesh;
                            smr.bones = skinned.bones;
                            smr.rootBone = skinned.rootBone;
                            smr.updateWhenOffscreen = true;
                            for (var i = 0; i < mesh.blendShapeCount; i++)
                                smr.SetBlendShapeWeight(i, skinned.GetBlendShapeWeight(i));
                            copy = smr;
                            break;
                        case MeshRenderer _ when original.TryGetComponent<MeshFilter>(out var filter) && filter.sharedMesh:
                            go.AddComponent<MeshFilter>().sharedMesh = mesh = filter.sharedMesh;
                            copy = go.AddComponent<MeshRenderer>();
                            break;
                        case SpriteRenderer sprite when sprite.sprite:
                            var sr = go.AddComponent<SpriteRenderer>();
                            sr.sprite = sprite.sprite;
                            sr.color = sprite.color;
                            sr.flipX = sprite.flipX;
                            sr.flipY = sprite.flipY;
                            sr.drawMode = sprite.drawMode;
                            sr.size = sprite.size;
                            copy = sr;
                            break;
                        default:  // particles, lines, trails, billboards: they need the game running
                            UnityEngine.Object.DestroyImmediate(go);
                            continue;
                    }
                    copy.sharedMaterials = original.sharedMaterials;
                    copy.shadowCastingMode = ShadowCastingMode.Off;
                    copy.receiveShadows = false;
                    copy.lightProbeUsage = LightProbeUsage.Off;
                    copy.reflectionProbeUsage = ReflectionProbeUsage.Simple;  // our probe, the only one down here

                    var description = new StringBuilder(PathUnder(original.transform, prefab)).Append('\t').Append(original.GetType().Name);
                    foreach (var material in original.sharedMaterials)
                        description.Append('\t').Append(material ? $"{material.name} | {(material.shader ? material.shader.name : "-")}" : "-");
                    drawn.Add(new Drawn
                    {
                        // A new skinned renderer has no bounds before its first skinning: the mesh's, placed like the original
                        Bounds = mesh && copy is SkinnedMeshRenderer ? Transform(mesh.bounds, go.transform.localToWorldMatrix) : copy.bounds,
                        Description = description.Append('\n').ToString(),
                    });
                }
                return drawn;
            }

            private void Frame(Bounds bounds, bool level)
            {
                // Rooms: the floor area decides (tall props and shafts would shrink the room to a corner of the picture)
                var extents = bounds.extents;
                var radius = Mathf.Max(level ? new Vector2(extents.x, extents.z).magnitude : extents.magnitude, 0.01f);
                var distance = radius / Mathf.Sin(FieldOfView * 0.5f * Mathf.Deg2Rad) * 1.05f;
                var view = level ? LevelView : ObjectView;
                camera.transform.position = bounds.center + view * distance;
                camera.transform.LookAt(bounds.center);
                camera.nearClipPlane = level
                    ? Mathf.Max(0.01f, distance - extents.y * view.y + CeilingCut)
                    : Mathf.Max(0.01f, distance - radius * 1.1f);
                camera.farClipPlane = distance + extents.magnitude * 1.1f;
            }

            /// <summary>Renders with the gallery's lighting only; everything changed is put back afterwards</summary>
            private void Draw(Texture2D image)
            {
                var otherLights = UnityEngine.Object.FindObjectsOfType<Light>().Where(l => l != light && l.enabled).ToList();
                var fog = RenderSettings.fog;
                var ambientMode = RenderSettings.ambientMode;
                var ambientLight = RenderSettings.ambientLight;
                var ambientProbe = RenderSettings.ambientProbe;
                var ambientIntensity = RenderSettings.ambientIntensity;
                var reflectionMode = RenderSettings.defaultReflectionMode;
                var customReflection = RenderSettings.customReflectionTexture;
                var reflectionIntensity = RenderSettings.reflectionIntensity;
                var pixelLights = QualitySettings.pixelLightCount;
                var anisotropic = QualitySettings.anisotropicFiltering;
                var lampCount = Shader.GetGlobalInt("_RQLampCount");
                var previous = RenderTexture.active;
                try
                {
                    foreach (var l in otherLights)
                        l.enabled = false;
                    RenderSettings.fog = false;
                    RenderSettings.ambientMode = AmbientMode.Flat;
                    RenderSettings.ambientLight = Ambient;
                    var probe = new SphericalHarmonicsL2();
                    probe.AddAmbientLight(Ambient);
                    RenderSettings.ambientProbe = probe;
                    RenderSettings.ambientIntensity = 1;
                    RenderSettings.defaultReflectionMode = DefaultReflectionMode.Custom;
                    RenderSettings.customReflectionTexture = reflection;
                    RenderSettings.reflectionIntensity = 1;
                    QualitySettings.pixelLightCount = Mathf.Max(pixelLights, 2);
                    QualitySettings.anisotropicFiltering = AnisotropicFiltering.ForceEnable;
                    Shader.SetGlobalInt("_RQLampCount", 0);  // the port's lamps (RepoQuestLamps.cginc)

                    camera.Render();
                    RenderTexture.active = camera.targetTexture;
                    image.ReadPixels(new Rect(0, 0, image.width, image.height), 0, 0);
                    image.Apply(false);
                }
                finally
                {
                    RenderTexture.active = previous;
                    foreach (var l in otherLights)
                        if (l)
                            l.enabled = true;
                    RenderSettings.fog = fog;
                    RenderSettings.ambientMode = ambientMode;
                    RenderSettings.ambientLight = ambientLight;
                    RenderSettings.ambientProbe = ambientProbe;
                    RenderSettings.ambientIntensity = ambientIntensity;
                    RenderSettings.defaultReflectionMode = reflectionMode;
                    RenderSettings.customReflectionTexture = customReflection;
                    RenderSettings.reflectionIntensity = reflectionIntensity;
                    QualitySettings.pixelLightCount = pixelLights;
                    QualitySettings.anisotropicFiltering = anisotropic;
                    Shader.SetGlobalInt("_RQLampCount", lampCount);
                }
            }

            public void Clear()
            {
                for (var i = copies.childCount - 1; i >= 0; i--)
                    UnityEngine.Object.DestroyImmediate(copies.GetChild(i).gameObject);
                if (clone)
                    UnityEngine.Object.DestroyImmediate(clone);
                clone = null;
            }

            public void Dispose()
            {
                Clear();
                UnityEngine.Object.DestroyImmediate(root);
                UnityEngine.Object.DestroyImmediate(target);
                UnityEngine.Object.DestroyImmediate(readback);
                UnityEngine.Object.DestroyImmediate(levelTarget);
                UnityEngine.Object.DestroyImmediate(levelReadback);
                UnityEngine.Object.DestroyImmediate(reflection);
            }

            private static bool ActiveUnder(Transform t, Transform root)
            {
                for (; t && t != root; t = t.parent)
                    if (!t.gameObject.activeSelf)
                        return false;
                return true;
            }

            private static string PathUnder(Transform t, Transform root)
            {
                var path = t == root ? "." : t.name;
                for (var p = t.parent; p && p != root; p = p.parent)
                    path = p.name + "/" + path;
                return path;
            }

            private static Bounds Transform(Bounds local, Matrix4x4 matrix)
            {
                var result = new Bounds(matrix.MultiplyPoint3x4(local.center), Vector3.zero);
                for (var i = 0; i < 8; i++)
                {
                    var corner = local.center + Vector3.Scale(local.extents,
                        new Vector3((i & 1) == 0 ? -1 : 1, (i & 2) == 0 ? -1 : 1, (i & 4) == 0 ? -1 : 1));
                    result.Encapsulate(matrix.MultiplyPoint3x4(corner));
                }
                return result;
            }
        }
    }
}
