using System;
using System.Collections;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.XR;
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
    /// files/invoke ("Type.Method" per line) calls those parameterless methods on the scene's object of that type, to
    /// press menu buttons without controllers: MenuPageMain.ButtonEventSinglePlayer, then MenuPageSaves.OnNewGame.
    /// files/xr-info logs the XR devices, the head pose they report and every TrackedPoseDriver with its bound controls.
    /// files/renderers-info logs the active particle and mesh renderers' materials: shader, support, keywords, texture.
    /// files/ui-info logs the event systems with their input modules, the UI raycasters and what each ray interactor
    /// points at (the menus are clicked through XRI's UI raycast).
    /// files/inspect ("Type" or "Type@hierarchy/path" per line) dumps the fields and properties of those objects.
    /// files/command runs a test script without anyone in the headset, one line at a time: "wait 5" (seconds),
    /// "tp 3 90" (spawn the local player at the 4th level point, sorted by distance from the player, facing yaw 90),
    /// "at 1.5 0 22 90" (the same at a position), "yaw 180" (face yaw 180 where it stands), "turn 45" (turn by 45),
    /// "culling off|on" (LevelCulling's A/B; the reply has its last pass, the camera's far plane and the fog),
    /// anything else goes to the game's debug console
    /// (DebugCommandHandler: "level sunstone valley resort", "spawn enemy pony", "godmode", "noaggro", "level shop").
    /// The reports of these requests also go to files/reports/&lt;request&gt;-&lt;time&gt;.txt: logcat cuts a message at 4 KB.
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
        private string invokePath;
        private string commandPath;
        private bool commandRunning;
        private string xrInfoPath;
        private string renderersInfoPath;
        private string uiInfoPath;
        private string inspectPath;
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
            invokePath = Path.Combine(Application.persistentDataPath, "invoke");
            commandPath = Path.Combine(Application.persistentDataPath, "command");
            xrInfoPath = Path.Combine(Application.persistentDataPath, "xr-info");
            renderersInfoPath = Path.Combine(Application.persistentDataPath, "renderers-info");
            uiInfoPath = Path.Combine(Application.persistentDataPath, "ui-info");
            inspectPath = Path.Combine(Application.persistentDataPath, "inspect");
        }

        private static bool Consume(string path)
        {
            if (!File.Exists(path))
                return false;
            File.Delete(path);
            return true;
        }

        /// <summary>
        /// A request file's lines without comments, the file deleted. A file the game cannot read (adb pushes it owned
        /// by shell: chmod 666 it) is logged and dropped: the exception would end the poll loop, and every request with it
        /// </summary>
        private static System.Collections.Generic.List<string> ReadRequest(string path, string name)
        {
            try
            {
                var lines = File.ReadAllLines(path).Select(l => l.Trim()).Where(l => l.Length > 0 && !l.StartsWith("#")).ToList();
                File.Delete(path);
                return lines;
            }
            catch (Exception e)
            {
                Debug.LogError($"[QuestDiagnostics] {name} not read (pushed by adb? chmod 666 it): {e.GetType().Name}: {e.Message}");
                try { File.Delete(path); } catch (Exception) { /* left for the next poll to report again */ }
                return new System.Collections.Generic.List<string>();
            }
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
                if (Consume(xrInfoPath))
                    LogXRInfo();
                if (Consume(renderersInfoPath))
                    LogRenderersInfo();
                if (Consume(uiInfoPath))
                    LogUIInfo();
                if (File.Exists(inspectPath))
                    LogInspect(inspectPath);
                if (File.Exists(invokePath))
                    yield return InvokeRequested();
                if (!commandRunning && File.Exists(commandPath))
                    StartCoroutine(RunCommands());
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

        /// <summary>
        /// files/ui-info: the event systems and their input modules, the UI raycasters, and for each ray interactor
        /// (found by name, through reflection) its pose, whether UI interaction is on and what its UI raycast hits
        /// </summary>
        private static void LogUIInfo()
        {
            var log = new StringBuilder("[QuestDiagnostics] ui-info");
            var current = UnityEngine.EventSystems.EventSystem.current;
            log.Append($"\n  current {(current ? PathOf(current.transform) : "none")}");
            foreach (var system in Resources.FindObjectsOfTypeAll<UnityEngine.EventSystems.EventSystem>().Where(e => e.gameObject.scene.IsValid()))
                log.Append($"\n  event system {PathOf(system.transform)} scene {system.gameObject.scene.name} enabled {system.isActiveAndEnabled} " +
                           $"modules [{string.Join(", ", system.GetComponents<UnityEngine.EventSystems.BaseInputModule>().Select(m => $"{m.GetType().Name} {(m.isActiveAndEnabled ? "on" : "off")}"))}]");
            foreach (var raycaster in Resources.FindObjectsOfTypeAll<UnityEngine.EventSystems.BaseRaycaster>().Where(r => r.gameObject.scene.IsValid()))
                log.Append($"\n  raycaster {PathOf(raycaster.transform)} [{raycaster.GetType().Name}] enabled {raycaster.isActiveAndEnabled}");
            foreach (var interactor in Resources.FindObjectsOfTypeAll<MonoBehaviour>()
                         .Where(b => b.gameObject.scene.IsValid() && b.GetType().Name.EndsWith("RayInteractor")))
            {
                var type = interactor.GetType();
                var ui = type.GetProperty("enableUIInteraction")?.GetValue(interactor);
                var hit = "-";
                var tryGet = type.GetMethods().FirstOrDefault(m => m.Name == "TryGetCurrentUIRaycastResult" && m.GetParameters().Length == 1);
                if (tryGet != null)
                {
                    var args = new object[1];
                    hit = (bool)tryGet.Invoke(interactor, args) && args[0] is UnityEngine.EventSystems.RaycastResult result
                        ? $"{(result.gameObject ? PathOf(result.gameObject.transform) : "?")} at {result.worldPosition}"
                        : "nothing";
                }
                log.Append($"\n  interactor {PathOf(interactor.transform)} [{type.Name}] enabled {interactor.isActiveAndEnabled} ui {ui} " +
                           $"pos {interactor.transform.position} rot {interactor.transform.eulerAngles} hit {hit}");
            }
            Report(log.ToString());
        }

        /// <summary>files/renderers-info: each distinct material of the visible renderers, with what it renders with</summary>
        private static void LogRenderersInfo()
        {
            var log = new StringBuilder("[QuestDiagnostics] renderers-info");
            var seen = new System.Collections.Generic.HashSet<Material>();
            foreach (var renderer in FindObjectsByType<Renderer>(FindObjectsSortMode.None)
                         .Where(r => r.enabled && r.isVisible && (r is ParticleSystemRenderer || r is MeshRenderer)))
            foreach (var material in renderer.sharedMaterials.Where(m => m && seen.Add(m)))
            {
                var shader = material.shader;
                var texture = material.HasProperty("_MainTex") ? material.mainTexture : null;
                log.Append($"\n  {material.name} on {PathOf(renderer.transform)} [{renderer.GetType().Name}]: " +
                           $"shader {(shader ? $"{shader.name} supported {shader.isSupported} passes {shader.passCount}" : "none")} " +
                           $"queue {material.renderQueue} keywords [{string.Join(" ", material.shaderKeywords)}] " +
                           $"texture {(texture ? $"{texture.name} {texture.graphicsFormat} {texture.width}x{texture.height}" : "none")}");
            }
            Report(log.ToString());
        }

        /// <summary>files/xr-info: what head tracking gets from the XR plugin and what the pose drivers make of it</summary>
        private static void LogXRInfo()
        {
            var log = new StringBuilder("[QuestDiagnostics] xr-info");
            var head = UnityEngine.XR.InputDevices.GetDeviceAtXRNode(UnityEngine.XR.XRNode.Head);
            head.TryGetFeatureValue(UnityEngine.XR.CommonUsages.centerEyeRotation, out var xrRotation);
            head.TryGetFeatureValue(UnityEngine.XR.CommonUsages.trackingState, out var xrState);
            log.Append($"\n  XR: device {UnityEngine.XR.XRSettings.loadedDeviceName}, head {head.name} valid {head.isValid}, " +
                       $"rotation {xrRotation.eulerAngles}, state {xrState}");
            foreach (var device in InputSystem.devices)
            {
                log.Append($"\n  device {device.name} layout {device.layout} enabled {device.enabled} updated {device.lastUpdateTime:0.00}");
                if (device is XRHMD hmd)
                    log.Append($" centerEye {hmd.centerEyeRotation.ReadValue().eulerAngles} state {hmd.trackingState.ReadValue()}");
            }
            foreach (var driver in Resources.FindObjectsOfTypeAll<TrackedPoseDriver>().Where(d => d.gameObject.scene.IsValid()))
            {
                log.Append($"\n  driver {PathOf(driver.transform)} [{driver.GetType().Name}] enabled {driver.isActiveAndEnabled} " +
                           $"type {driver.trackingType} update {driver.updateType} ignoreState {driver.ignoreTrackingState} " +
                           $"local {driver.transform.localEulerAngles}");
                foreach (var (name, action) in new[] { ("position", driver.positionInput.action),
                             ("rotation", driver.rotationInput.action), ("state", driver.trackingStateInput.action) })
                    log.Append($"\n    {name}: " + (action == null ? "none"
                        : $"{action.name} enabled {action.enabled} bindings [{string.Join(", ", action.bindings.Select(b => b.effectivePath))}] " +
                          $"controls [{string.Join(", ", action.controls.Select(c => c.path))}] value {action.ReadValueAsObject()}"));
            }
            var main = Camera.main;
            if (main)
                log.Append($"\n  Camera.main {PathOf(main.transform)} local {main.transform.localEulerAngles} world {main.transform.eulerAngles}");
            Report(log.ToString());
        }

        /// <summary>
        /// files/invoke: one "Type.Method" per line, a frame apart, each logged with its outcome. "Type.Method@path"
        /// calls it on the component of the active object at that hierarchy path instead of on any object of the type
        /// </summary>
        private IEnumerator InvokeRequested()
        {
            var calls = ReadRequest(invokePath, "invoke");
            foreach (var line in calls)
            {
                var at = line.IndexOf('@');
                var call = at < 0 ? line : line.Substring(0, at);
                var dot = call.LastIndexOf('.');
                var type = dot > 0
                    ? AppDomain.CurrentDomain.GetAssemblies().Select(a => a.GetType(call.Substring(0, dot)))
                        .FirstOrDefault(t => t != null)
                    : null;
                var target = type == null ? null
                    : at < 0 ? FindAnyObjectByType(type)
                    : GameObject.Find(line.Substring(at + 1))?.GetComponent(type);
                var method = type?.GetMethod(call.Substring(dot + 1),
                    BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic, null, Type.EmptyTypes, null);
                if (target == null || method == null)
                {
                    Debug.LogWarning($"[QuestDiagnostics] invoke {call}: " +
                                     (type == null ? "no such type" : target == null ? "no object" : "no such method"));
                    continue;
                }

                try
                {
                    method.Invoke(target, null);
                    Debug.Log($"[QuestDiagnostics] invoked {call}");
                }
                catch (Exception e)
                {
                    Debug.LogError($"[QuestDiagnostics] invoke {call} failed: {e.InnerException ?? e}");
                }

                yield return null;
            }
        }

        /// <summary>files/shader-gallery: the listed prefabs rendered by ShaderGallery into files/gallery/</summary>
        private IEnumerator RunGallery()
        {
            var paths = ReadRequest(galleryRequestPath, "shader-gallery");
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
                    if (g is TMPro.TMP_Text tmp)
                        report.AppendLine($"      tmp {TextMeshInfo(tmp)}");
                }
            }

            File.WriteAllText(Path.Combine(dir, "report.txt"), report.ToString());
            Debug.Log($"[QuestDiagnostics] snapshot written to {dir}");
        }

        /// <summary>What decides how a TMP text's SDF is drawn: the scale packed into uv0.w and the material's weights</summary>
        private static string TextMeshInfo(TMPro.TMP_Text text)
        {
            var mesh = text.textInfo?.meshInfo;
            var uvs = mesh is { Length: > 0 } ? mesh[0].uvs0 : null;
            var material = text.fontSharedMaterial;
            string Float(string name) => material && material.HasProperty(name) ? material.GetFloat(name).ToString("0.###") : "-";
            return $"font={(text.font ? text.font.name : "none")} material={(material ? material.name : "none")} " +
                   $"atlas={(material && material.mainTexture ? $"{material.mainTexture.name} {material.mainTexture.graphicsFormat}" : "none")} " +
                   $"size={text.fontSize} lossyScale={text.rectTransform.lossyScale} " +
                   $"uv0.w={(uvs is { Length: > 0 } ? uvs[0].w.ToString("0.#####") : "-")} " +
                   $"gradient={Float("_GradientScale")} dilate={Float("_FaceDilate")} weight={Float("_WeightNormal")} ratioA={Float("_ScaleRatioA")}";
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

        /// <summary>
        /// files/inspect: for each line, "Type" (every active object of that type, at most 40) or "Type@path" (the
        /// component on the object at that hierarchy path), each field and property of the object, its own type's and
        /// its base types' up to MonoBehaviour: plain values as they are, Unity objects by name, collections by count
        /// </summary>
        /// <summary>files/command: see the class summary</summary>
        private IEnumerator RunCommands()
        {
            commandRunning = true;
            var lines = ReadRequest(commandPath, "command");
            foreach (var line in lines)
            {
                var words = line.Split(' ', StringSplitOptions.RemoveEmptyEntries);
                if (words[0] == "wait" && words.Length > 1 && float.TryParse(words[1], NumberStyles.Float, CultureInfo.InvariantCulture, out var seconds))
                {
                    yield return new WaitForSecondsRealtime(seconds);
                    continue;
                }

                try
                {
                    Debug.Log($"[QuestDiagnostics] command {line}: {RunCommand(words, line)}");
                }
                catch (Exception e)
                {
                    Debug.LogError($"[QuestDiagnostics] command {line} failed: {e.InnerException ?? e}");
                }
                yield return null;

                // RepoXR's VRPlayer moves the player's rigidbody after the head every physics step, from where the
                // rigidbody was: a spawn that only moved the transforms was undone now and then. Held for a few steps
                if (placeTarget is { } target)
                {
                    for (var i = 0; i < 10; i++)
                    {
                        Place(target);
                        yield return new WaitForFixedUpdate();
                    }
                    placeTarget = null;
                }
            }
            commandRunning = false;
        }

        private static Vector3? placeTarget;

        private static Type GameType(string name) =>
            AppDomain.CurrentDomain.GetAssemblies().Select(a => a.GetType(name)).FirstOrDefault(t => t != null);

        private static void Place(Vector3 position)
        {
            const BindingFlags all = BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.Static;
            var controllerType = GameType("PlayerController");
            var controller = (Component)controllerType?.GetField("instance", all)?.GetValue(null);
            if (!controller)
                return;
            controller.transform.position = position;
            if (controllerType.GetField("rb", all)?.GetValue(controller) is Rigidbody rb && rb)
            {
                rb.position = position;
                rb.linearVelocity = Vector3.zero;
            }
        }

        /// <summary>The VR view's yaw: RepoXR's VRCameraAim, which the player's body follows (the spawn rotation does not stick)</summary>
        private static string Aim(string method, float degrees)
        {
            const BindingFlags all = BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.Static;
            var aimType = GameType("RepoXR.Player.Camera.VRCameraAim");
            var aim = aimType?.GetField("instance", all)?.GetValue(null);
            if (aim == null)
                return "no VRCameraAim";
            aimType.GetMethod(method, all, null, new[] { typeof(float) }, null)!.Invoke(aim, new object[] { degrees });
            return $"{method} {degrees}";
        }

        private static string RunCommand(string[] words, string line)
        {
            const BindingFlags all = BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.Static;
            float Number(int index) => words.Length > index ? float.Parse(words[index], CultureInfo.InvariantCulture) : 0;

            if (words[0] == "culling")
            {
                // A/B of LevelCulling at the current spot: "culling off", "culling on", "culling" (status only)
                if (words.Length > 1)
                    LevelCulling.Suspended = words[1] == "off";
                var camera = Camera.main;
                return $"suspended {LevelCulling.Suspended}; last pass {LevelCulling.LastPass}; camera " +
                       (camera ? $"{camera.name} far {camera.farClipPlane:0} m at {camera.transform.position}" : "none") +
                       $"; fog {RenderSettings.fog} {RenderSettings.fogMode} end {RenderSettings.fogEndDistance:0} density {RenderSettings.fogDensity:0.###}";
            }
            if (words[0] == "yaw")
                return Aim("SetAimNow", Number(1));
            if (words[0] == "turn")
                return Aim("TurnAimNow", Number(1));

            if (words[0] == "tp" || words[0] == "at")
            {
                var avatarType = GameType("PlayerAvatar");
                var avatar = (Component)avatarType.GetField("instance", all)?.GetValue(null);
                if (!avatar)
                    return "no local player";
                var position = avatar.transform.position;
                float? yaw = null;
                if (words[0] == "at")
                {
                    position = new Vector3(Number(1), Number(2), Number(3));
                    if (words.Length > 4)
                        yaw = Number(4);
                }
                else
                {
                    var points = FindObjectsByType(GameType("LevelPoint"), FindObjectsInactive.Exclude, FindObjectsSortMode.None)
                        .Cast<Component>().OrderBy(p => (p.transform.position - position).sqrMagnitude).ToList();
                    if (points.Count == 0)
                        return "no level points";
                    var index = Mathf.Clamp((int)Number(1), 0, points.Count - 1);
                    position = points[index].transform.position + Vector3.up * 0.2f;
                    if (words.Length > 2)
                        yaw = Number(2);
                }
                avatarType.GetMethod("Spawn", all, null, new[] { typeof(Vector3), typeof(Quaternion), typeof(bool) }, null)!
                    .Invoke(avatar, new object[] { position, Quaternion.Euler(0, yaw ?? 0, 0), true });
                Place(position);
                placeTarget = position;
                return $"player at {position}" + (yaw is { } y ? $", {Aim("SetAimNow", y)}" : "");
            }

            var handlerType = GameType("DebugCommandHandler");
            var handler = handlerType?.GetField("instance", all)?.GetValue(null) ?? handlerType?.GetProperty("instance", all)?.GetValue(null);
            if (handler == null)
                return "no DebugCommandHandler";
            var execute = handlerType.GetMethod("Execute", all, null, new[] { typeof(string), typeof(bool) }, null);
            return (bool)execute!.Invoke(handler, new object[] { line, true }) ? "done" : "not run (unknown or not enabled here)";
        }

        private static void LogInspect(string path)
        {
            var lines = ReadRequest(path, "inspect");
            var log = new StringBuilder("[QuestDiagnostics] inspect");
            foreach (var line in lines)
            {
                var at = line.IndexOf('@');
                var typeName = at < 0 ? line : line.Substring(0, at);
                var type = AppDomain.CurrentDomain.GetAssemblies().Select(a => a.GetType(typeName)).FirstOrDefault(t => t != null);
                if (type == null)
                {
                    log.Append($"\n{line}: no such type");
                    continue;
                }
                var targets = at < 0
                    ? FindObjectsByType(type, FindObjectsInactive.Exclude, FindObjectsSortMode.None).Take(40).ToArray()
                    : new[] { GameObject.Find(line.Substring(at + 1)) is { } go ? go.GetComponent(type) : null }.Where(c => c != null).ToArray();
                log.Append($"\n{line}: {targets.Length} object(s)");
                foreach (var target in targets)
                {
                    log.Append($"\n  {(target is Component c ? PathOf(c.transform) : target.name)}");
                    if (target is Component component)
                        log.Append($" pos {component.transform.position} local {component.transform.localPosition} " +
                                   $"rot {component.transform.eulerAngles} lossyScale {component.transform.lossyScale}");
                    for (var t = type; t != null && t != typeof(MonoBehaviour) && t != typeof(Behaviour) && t != typeof(Component); t = t.BaseType)
                    {
                        const BindingFlags flags = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.DeclaredOnly;
                        foreach (var field in t.GetFields(flags).Where(f => !f.Name.Contains("<")))
                            log.Append($"\n    {field.Name} = {Describe(() => field.GetValue(target))}");
                        foreach (var property in t.GetProperties(flags).Where(p => p.CanRead && p.GetIndexParameters().Length == 0))
                            log.Append($"\n    {property.Name} = {Describe(() => property.GetValue(target))}");
                    }
                }
            }
            Report(log.ToString());
        }

        private static string Describe(Func<object> get)
        {
            object value;
            try
            {
                value = get();
            }
            catch (Exception e)
            {
                return $"<{(e.InnerException ?? e).GetType().Name}>";
            }
            return value switch
            {
                null => "null",
                UnityEngine.Object o => o ? $"{o.name} [{o.GetType().Name}]" : "null (destroyed)",
                string s => $"\"{s}\"",
                System.Collections.ICollection collection => $"{value.GetType().Name} count {collection.Count}",
                _ => value.ToString()
            };
        }

        /// <summary>Logs a request's report and writes it to files/reports/&lt;request&gt;-&lt;time&gt;.txt</summary>
        private static void Report(string text)
        {
            Debug.Log(text);
            try
            {
                var name = text.Split('\n')[0].Replace("[QuestDiagnostics] ", "").Trim();
                var dir = Path.Combine(Application.persistentDataPath, "reports");
                Directory.CreateDirectory(dir);
                File.WriteAllText(Path.Combine(dir, $"{Sanitize(name)}-{DateTime.Now:HHmmss}.txt"), text);
            }
            catch (Exception e)
            {
                Debug.LogError($"[QuestDiagnostics] report not written: {e}");
            }
        }

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
