using System;
using System.Collections;
using System.IO;
using System.Linq;
using System.Text;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

namespace RepoQuest
{
    /// <summary>
    /// On-device snapshot for debugging rendering on Quest, where adb screencap sees nothing. Idle until asked:
    ///   adb shell touch /sdcard/Android/data/io.repoxr.quest/files/diag-request
    ///   adb pull /sdcard/Android/data/io.repoxr.quest/files/diag
    /// Writes a screen capture, every camera's render texture as PNG and a text report of cameras, canvases and the
    /// visible UI graphics to files/diag/&lt;time&gt;/ (scripts/quest-diag.sh does all of it).
    /// More requests, answered in the log: files/pp-toggle switches post-processing off or on, files/stereo-info
    /// reports the stereo rendering mode and the cameras, files/particles-toggle and files/particles-instancing hide
    /// particle systems or switch their GPU instancing, files/players-info lists every player's cosmetics with the
    /// colours their materials actually got, files/shader-gallery (a list of Resources paths, one per line) renders each
    /// of those prefabs into files/gallery/ for comparison with the PC game (ShaderGallery, scripts/shader_gallery.py).
    /// </summary>
    public class QuestDiagnostics : MonoBehaviour
    {
        private const float PollSeconds = 1f;

        private string requestPath;
        private string outputRoot;
        private string ppTogglePath;
        private string stereoInfoPath;
        private string particlesTogglePath;
        private string particlesInstancingPath;
        private string playersInfoPath;
        private string galleryRequestPath;
        private bool galleryRunning;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        private static void Install()
        {
            var go = new GameObject("RepoQuest Diagnostics");
            DontDestroyOnLoad(go);
            go.hideFlags = HideFlags.HideAndDontSave;
            go.AddComponent<QuestDiagnostics>();
        }

        private void Awake()
        {
            requestPath = Path.Combine(Application.persistentDataPath, "diag-request");
            outputRoot = Path.Combine(Application.persistentDataPath, "diag");
            ppTogglePath = Path.Combine(Application.persistentDataPath, "pp-toggle");
            stereoInfoPath = Path.Combine(Application.persistentDataPath, "stereo-info");
            particlesTogglePath = Path.Combine(Application.persistentDataPath, "particles-toggle");
            particlesInstancingPath = Path.Combine(Application.persistentDataPath, "particles-instancing");
            playersInfoPath = Path.Combine(Application.persistentDataPath, "players-info");
            galleryRequestPath = Path.Combine(Application.persistentDataPath, "shader-gallery");
        }

        private static bool Consume(string path)
        {
            if (!File.Exists(path))
                return false;
            File.Delete(path);
            return true;
        }

        /// <summary>
        /// files/pp-toggle: switches Post Processing (PostProcessLayer) on every camera off or back on, to see what it
        /// costs and whether a stereo problem comes from it
        /// </summary>
        private static void TogglePostProcessing()
        {
            foreach (var camera in Camera.allCameras)
            {
                if (camera.GetComponent("PostProcessLayer") is Behaviour layer)
                {
                    layer.enabled = !layer.enabled;
                    Debug.Log($"[QuestDiagnostics] {camera.name}: post-processing {(layer.enabled ? "on" : "off")}");
                }
            }
        }

        /// <summary>
        /// files/particles-toggle hides or shows every particle system; files/particles-instancing switches their mesh
        /// GPU instancing off or on (instanced mesh particles are a known source of one-eye-only rendering in
        /// single-pass instanced stereo)
        /// </summary>
        private static void ToggleParticles(bool instancing)
        {
            var renderers = FindObjectsOfType<ParticleSystemRenderer>(true);
            if (renderers.Length == 0)
                return;
            var value = instancing ? !renderers[0].enableGPUInstancing : !renderers[0].enabled;
            var instanced = 0;
            foreach (var renderer in renderers)
            {
                if (renderer.enableGPUInstancing)
                    instanced++;
                if (instancing)
                    renderer.enableGPUInstancing = value;
                else
                    renderer.enabled = value;
            }
            Debug.Log($"[QuestDiagnostics] {renderers.Length} particle renderers ({instanced} were instanced): " +
                      (instancing ? $"GPU instancing {(value ? "on" : "off")}" : value ? "shown" : "hidden"));
        }

        private static readonly string[] ColorProperties = { "_AlbedoColor", "_EmissionColor", "_FresnelColor", "_Color" };
        private static readonly string[] FloatProperties = { "_Metallic", "_Smoothness", "_FresnelAmount" };

        /// <summary>
        /// files/players-info: per player (the game's PlayerAvatarVisuals, found by name), each visible renderer with
        /// the cosmetic it belongs to, its material, shader and the colours the game tinted it with (PlayerMaterial)
        /// </summary>
        private static void LogPlayersInfo()
        {
            var report = new StringBuilder();
            foreach (var behaviour in FindObjectsByType<MonoBehaviour>(FindObjectsSortMode.None))
            {
                if (behaviour.GetType().Name != "PlayerAvatarVisuals")
                    continue;

                var owner = behaviour.transform.root.name;
                foreach (var component in behaviour.GetComponentsInParent<MonoBehaviour>(true))
                {
                    if (component.GetType().Name != "PlayerAvatar")
                        continue;
                    var field = component.GetType().GetField("playerName",
                        System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.NonPublic);
                    if (field?.GetValue(component) is string name)
                        owner = name;
                    break;
                }

                report.Append($"== {owner} ({behaviour.name})\n");
                foreach (var renderer in behaviour.GetComponentsInChildren<Renderer>())
                {
                    if (!renderer.enabled)
                        continue;
                    var cosmetic = renderer.transform;
                    while (cosmetic && !cosmetic.name.StartsWith("Cosmetic"))
                        cosmetic = cosmetic.parent;
                    report.Append($"  {(cosmetic ? cosmetic.name.Replace("(Clone)", "") : "-")} / {renderer.name}:");
                    foreach (var material in renderer.sharedMaterials)
                    {
                        if (!material)
                            continue;
                        report.Append($" [{material.name.Replace(" (Instance)", "*")} | {material.shader.name}");
                        foreach (var property in ColorProperties)
                            if (material.HasProperty(property))
                            {
                                var c = material.GetColor(property);
                                report.Append($" {property}=({c.r:0.##},{c.g:0.##},{c.b:0.##},{c.a:0.##})");
                            }
                        foreach (var property in FloatProperties)
                            if (material.HasProperty(property))
                                report.Append($" {property}={material.GetFloat(property):0.##}");
                        report.Append(']');
                    }
                    report.Append('\n');
                }
            }
            // One log call per renderer: logcat cuts long messages
            foreach (var line in report.ToString().Split('\n'))
                if (line.Length > 0)
                    Debug.Log("[players-info] " + line);
        }

        /// <summary>files/stereo-info: how the XR eyes are rendered right now, and by which cameras</summary>
        private static void LogStereoInfo()
        {
            var desc = UnityEngine.XR.XRSettings.eyeTextureDesc;
            Debug.Log($"[QuestDiagnostics] stereo: {UnityEngine.XR.XRSettings.stereoRenderingMode}, eye texture " +
                      $"{desc.width}x{desc.height} {desc.dimension} x{desc.volumeDepth} {desc.graphicsFormat} " +
                      $"msaa {desc.msaaSamples}, scale {UnityEngine.XR.XRSettings.eyeTextureResolutionScale}");
            foreach (var camera in Camera.allCameras)
            {
                var layer = camera.GetComponent("PostProcessLayer") as Behaviour;
                Debug.Log($"[QuestDiagnostics]   camera {camera.name}: depth {camera.depth}, eye {camera.stereoTargetEye}, " +
                          $"stereo {camera.stereoEnabled}, target {(camera.targetTexture ? camera.targetTexture.name : "-")}, " +
                          $"hdr {camera.allowHDR}, msaa {camera.allowMSAA}, post-processing " +
                          $"{(layer == null ? "none" : layer.enabled ? "on" : "off")}");
            }
        }

        private IEnumerator Start()
        {
            var wait = new WaitForSecondsRealtime(PollSeconds);
            while (true)
            {
                yield return wait;
                if (Consume(ppTogglePath))
                    TogglePostProcessing();
                if (Consume(stereoInfoPath))
                    LogStereoInfo();
                if (Consume(particlesTogglePath))
                    ToggleParticles(instancing: false);
                if (Consume(particlesInstancingPath))
                    ToggleParticles(instancing: true);
                if (Consume(playersInfoPath))
                    LogPlayersInfo();
                if (!galleryRunning && File.Exists(galleryRequestPath))
                    StartCoroutine(RunGallery());
                if (!File.Exists(requestPath))
                    continue;

                File.Delete(requestPath);
                yield return new WaitForEndOfFrame();
                try
                {
                    Snapshot();
                }
                catch (Exception e)
                {
                    Debug.LogError($"[QuestDiagnostics] snapshot failed: {e}");
                }
            }
        }

        /// <summary>files/shader-gallery: the listed prefabs rendered by ShaderGallery into files/gallery/</summary>
        private IEnumerator RunGallery()
        {
            var paths = File.ReadAllLines(galleryRequestPath).Select(l => l.Trim())
                .Where(l => l.Length > 0 && !l.StartsWith("#")).ToList();
            File.Delete(galleryRequestPath);
            var dir = Path.Combine(Application.persistentDataPath, "gallery");
            if (Directory.Exists(dir))
                Directory.Delete(dir, true);
            galleryRunning = true;
            Debug.Log($"[ShaderGallery] rendering {paths.Count} prefabs");
            try
            {
                yield return ShaderGallery.Run(paths, dir, message => Debug.Log(message));
            }
            finally
            {
                galleryRunning = false;
            }
        }

        private void Snapshot()
        {
            var dir = Path.Combine(outputRoot, DateTime.Now.ToString("HHmmss"));
            Directory.CreateDirectory(dir);
            var report = new StringBuilder();

            report.AppendLine($"scenes: {string.Join(", ", Enumerable.Range(0, SceneManager.sceneCount).Select(i => SceneManager.GetSceneAt(i).name))}");
            report.AppendLine($"screen: {Screen.width}x{Screen.height}, timeScale {Time.timeScale}, frame {Time.frameCount}");

            var screen = ScreenCapture.CaptureScreenshotAsTexture();
            Save(screen, Path.Combine(dir, "screen.png"));
            Destroy(screen);

            report.AppendLine().AppendLine("== cameras");
            foreach (var cam in Resources.FindObjectsOfTypeAll<Camera>().Where(c => c.gameObject.scene.IsValid()))
            {
                var t = cam.transform;
                var rt = cam.targetTexture;
                report.AppendLine($"{PathOf(t)} enabled={cam.enabled && cam.gameObject.activeInHierarchy} depth={cam.depth} " +
                                  $"mask=0x{cam.cullingMask:X} clear={cam.clearFlags} bg={cam.backgroundColor} ortho={cam.orthographic} " +
                                  $"size={cam.orthographicSize} near={cam.nearClipPlane} far={cam.farClipPlane} pos={t.position} rot={t.eulerAngles} " +
                                  $"target={(rt ? $"{rt.name} {rt.width}x{rt.height} {rt.graphicsFormat} depth{rt.depth} aa{rt.antiAliasing}" : "screen")}");
                if (rt && cam.enabled && cam.gameObject.activeInHierarchy)
                    SaveRenderTexture(rt, Path.Combine(dir, $"rt-{Sanitize(rt.name)}.png"));
            }

            report.AppendLine().AppendLine("== canvases");
            foreach (var canvas in Resources.FindObjectsOfTypeAll<Canvas>().Where(c => c.gameObject.scene.IsValid() && c.isRootCanvas))
            {
                var t = canvas.transform;
                var graphics = canvas.GetComponentsInChildren<Graphic>(false);
                report.AppendLine($"{PathOf(t)} active={canvas.gameObject.activeInHierarchy} enabled={canvas.enabled} mode={canvas.renderMode} " +
                                  $"cam={(canvas.worldCamera ? canvas.worldCamera.name : "none")} layer={canvas.gameObject.layer} sort={canvas.sortingOrder} " +
                                  $"pos={t.position} scale={t.lossyScale} activeGraphics={graphics.Length}");
                foreach (var g in graphics.Where(g => g.enabled).Take(60))
                {
                    var cr = g.canvasRenderer;
                    var rect = g.rectTransform;
                    var text = g.GetType().GetProperty("text")?.GetValue(g) as string;
                    report.AppendLine($"   {PathOf(g.transform)} [{g.GetType().Name}] color={g.color} crAlpha={cr.GetAlpha()} culled={cr.cull} " +
                                      $"shader={(g.materialForRendering ? g.materialForRendering.shader.name : "none")} " +
                                      $"world={rect.position} size={rect.rect.size} layer={g.gameObject.layer}" +
                                      (text != null ? $" text=\"{text.Substring(0, Math.Min(40, text.Length)).Replace("\n", " ")}\"" : ""));
                }
            }

            File.WriteAllText(Path.Combine(dir, "report.txt"), report.ToString());
            Debug.Log($"[QuestDiagnostics] snapshot written to {dir}");
        }

        private static void SaveRenderTexture(RenderTexture rt, string path)
        {
            // Via an 8-bit copy: ReadPixels does not convert from every float format
            var copy = RenderTexture.GetTemporary(rt.width, rt.height, 0, RenderTextureFormat.ARGB32);
            Graphics.Blit(rt, copy);
            var previous = RenderTexture.active;
            RenderTexture.active = copy;
            var tex = new Texture2D(rt.width, rt.height, TextureFormat.RGBA32, false);
            tex.ReadPixels(new Rect(0, 0, rt.width, rt.height), 0, 0);
            tex.Apply();
            RenderTexture.active = previous;
            RenderTexture.ReleaseTemporary(copy);
            Save(tex, path);
            Destroy(tex);
        }

        private static void Save(Texture2D tex, string path) => File.WriteAllBytes(path, tex.EncodeToPNG());

        private static string Sanitize(string name) => string.Concat(name.Select(c => char.IsLetterOrDigit(c) ? c : '_'));

        private static string PathOf(Transform t)
        {
            var path = t.name;
            for (var p = t.parent; p != null; p = p.parent)
                path = p.name + "/" + path;
            return path;
        }
    }
}
