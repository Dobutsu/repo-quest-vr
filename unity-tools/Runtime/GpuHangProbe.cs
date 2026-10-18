using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text;
using UnityEngine;
using UnityEngine.UI;
using UnityEngine.XR;

namespace RepoQuest
{
    /// <summary>
    /// Finds what hangs the GPU in the main menu on headsets where it does (Pico 4, Adreno 650: the kernel logs
    /// "kgsl-3d0: GPU hang detected" on the menu's first frames, the OpenXR runtime then fails xrAcquireSwapchainImage
    /// and Unity shuts XR down). Idle unless asked:
    ///   adb shell touch /sdcard/Android/data/io.repoxr.quest/files/gpu-probe-request
    /// From the game's start it holds back everything suspect: every shader on a renderer is swapped for
    /// RepoQuest/ProbeSafe, shadows are off, cameras that would render deferred render forward, cameras other than the
    /// eyes' draw nothing, skinned meshes and particles are hidden.
    /// Phase A, in the main menu: the held-back things come back one per step (StepSeconds), features first, then shader
    /// by shader. A step that hangs the GPU costs the session ("Shutting down OpenXR."): it is written down as guilty and
    /// the game quits; the next launch goes on from the following step with the guilty still held back. If even the
    /// first step, with everything held back, hangs, the next launch switches RepoXR to multi-pass stereo and starts over.
    /// Phase B, one launch: only the guilty held back from the start; surviving SurviveSeconds in the menu confirms them.
    /// Phase C, one launch each: for a hang that only the menu's first frames cause (a camera that renders once), the
    /// guilty plus one more suspect held back from the start, until a launch survives.
    /// Progress shows in front of the eyes; files/gpu-probe/ keeps state.txt and report.txt; log lines start with
    /// [GpuProbe]. At the end the request is deleted and RepoXR's stereo setting put back.
    /// </summary>
    public class GpuHangProbe : MonoBehaviour
    {
        // The hang ends the session about 5 s after it starts; a culprit in view hangs within the first frames
        private const float StepSeconds = 7f;
        private const float SettleSeconds = 2f;
        private const float SurviveSeconds = 20f;

        private static readonly string[] Features = { "cameras", "deferred", "shadows", "skinned", "particles" };
        private static readonly string[] StartSuspects = { "cameras", "deferred", "shadows", "skinned", "particles", "shader:*" };

        private sealed class Original
        {
            public Shader Shader;
            public string[] Keywords;
            public int Queue;
        }

        private string requestPath;
        private string statePath;
        private string reportPath;
        private string configPath;
        private Shader safe;

        // Persistent state (state.txt)
        private string phase = "A";
        private readonly List<string> passed = new();
        private readonly Dictionary<string, string> guilty = new();
        private readonly List<string> history = new();
        private int launchTest;
        private bool multipass;
        private string originalSinglePass;
        private string running;
        private int launches;

        // This launch
        private List<string> plan;
        private string current;
        private float stepTime;
        private float menuTime;
        private bool lost;
        private float quitAt = -1;
        private bool xrRunning;
        private int scannedFrame = -1;
        private readonly Dictionary<Material, Original> originals = new();
        private readonly Dictionary<string, Original> byName = new();
        private readonly HashSet<Renderer> forcedOff = new();
        private readonly Dictionary<Camera, RenderingPath> paths = new();
        private readonly Dictionary<Camera, int> masks = new();
        private readonly Dictionary<string, int> counts = new();
        private readonly Dictionary<string, int> visibleCounts = new();
        private readonly List<Material> materials = new();
        private readonly List<XRDisplaySubsystem> displays = new();
        private ShadowQuality? gameShadows;
        private Text hud;
        private Canvas canvas;

        private static FieldInfo runInstance, levelCurrent, levelMainMenu;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        private static void Install()
        {
            if (!File.Exists(Path.Combine(Application.persistentDataPath, "gpu-probe-request")))
                return;
            var go = new GameObject("RepoQuest GPU Probe");
            DontDestroyOnLoad(go);
            go.hideFlags = HideFlags.HideAndDontSave;
            go.AddComponent<GpuHangProbe>();
        }

        private void Awake()
        {
            var data = Application.persistentDataPath;
            requestPath = Path.Combine(data, "gpu-probe-request");
            var dir = Path.Combine(data, "gpu-probe");
            Directory.CreateDirectory(dir);
            statePath = Path.Combine(dir, "state.txt");
            reportPath = Path.Combine(dir, "report.txt");
            configPath = Path.Combine(data, "RepoXR", "config", "io.daxcess.repoxr.cfg");

            safe = Shader.Find("RepoQuest/ProbeSafe");
            if (safe == null)
            {
                Debug.LogError("[GpuProbe] shader RepoQuest/ProbeSafe missing; probe off");
                enabled = false;
                return;
            }

            Load();
            if (phase == "done")
                Reset();
            launches++;
            Note($"launch {launches}, phase {phase}{(multipass ? ", multi-pass" : "")}; " +
                 $"{SystemInfo.deviceModel}, {SystemInfo.graphicsDeviceName}, {SystemInfo.graphicsDeviceVersion}");

            // The previous launch died during a step or test without the XR error reaching us
            if (running != null)
            {
                var key = running;
                running = null;
                if (key == "launch")
                    LaunchHung("the game died");
                else
                    StepHung(key, "the game died");
            }
            Save();

            var runManager = AppDomain.CurrentDomain.GetAssemblies()
                .Select(a => a.GetType("RunManager")).FirstOrDefault(t => t != null);
            if (runManager != null)
            {
                runInstance = runManager.GetField("instance", BindingFlags.Public | BindingFlags.Static);
                levelCurrent = runManager.GetField("levelCurrent");
                levelMainMenu = runManager.GetField("levelMainMenu");
            }
            else
                Note("RunManager not found: the main menu cannot be told apart");

            Camera.onPreCull += PreCull;
            Camera.onPostRender += PostRender;
            Application.logMessageReceived += OnLog;
            CreateHud();
        }

        private void OnDestroy()
        {
            Camera.onPreCull -= PreCull;
            Camera.onPostRender -= PostRender;
            Application.logMessageReceived -= OnLog;
        }

        // ---- What is held back

        private bool IsHeld(string key)
        {
            var shader = key.StartsWith("shader:", StringComparison.Ordinal);
            if (guilty.ContainsKey(key) || (shader && guilty.ContainsKey("shader:*")))
                return true;
            switch (phase)
            {
                case "A":
                    return key != current && !passed.Contains(key);
                case "C":
                    var test = StartSuspects[launchTest];
                    return key == test || (shader && test == "shader:*");
                default:
                    return false;
            }
        }

        private void PreCull(Camera camera)
        {
            if (Time.frameCount != scannedFrame)
            {
                scannedFrame = Time.frameCount;
                Scan();
            }

            if (IsHeld("deferred") && camera.actualRenderingPath == RenderingPath.DeferredShading)
            {
                paths[camera] = camera.renderingPath;
                camera.renderingPath = RenderingPath.Forward;
            }
            if (IsHeld("cameras") && IsSecondary(camera))
            {
                masks[camera] = camera.cullingMask;
                camera.cullingMask = 0;
            }
        }

        private void PostRender(Camera camera)
        {
            if (paths.TryGetValue(camera, out var path))
            {
                camera.renderingPath = path;
                paths.Remove(camera);
            }
            if (masks.TryGetValue(camera, out var mask))
            {
                camera.cullingMask = mask;
                masks.Remove(camera);
            }
        }

        private static bool IsSecondary(Camera camera) =>
            camera.targetTexture != null || camera.stereoTargetEye == StereoTargetEyeMask.None;

        /// <summary>Once per frame, before the first camera culls: shaders, skinned meshes, particles, shadows</summary>
        private void Scan()
        {
            var skinned = IsHeld("skinned");
            var particles = IsHeld("particles");
            counts.Clear();
            visibleCounts.Clear();
            foreach (var renderer in FindObjectsByType<Renderer>(FindObjectsInactive.Exclude, FindObjectsSortMode.None))
            {
                if (renderer is SkinnedMeshRenderer)
                    Hold(renderer, skinned);
                else if (renderer is ParticleSystemRenderer)
                    Hold(renderer, particles);

                renderer.GetSharedMaterials(materials);
                foreach (var material in materials)
                {
                    if (!material)
                        continue;
                    var original = OriginalOf(material);
                    if (original?.Shader == null)
                        continue;
                    var key = "shader:" + original.Shader.name;
                    counts[key] = (counts.TryGetValue(key, out var n) ? n : 0) + 1;
                    if (renderer.isVisible)
                        visibleCounts[key] = (visibleCounts.TryGetValue(key, out var v) ? v : 0) + 1;

                    var want = IsHeld(key) ? safe : original.Shader;
                    if (material.shader == want)
                        continue;
                    material.shader = want;
                    if (want != safe)
                    {
                        material.shaderKeywords = original.Keywords;
                        material.renderQueue = original.Queue;
                    }
                }
            }

            if (IsHeld("shadows"))
            {
                if (QualitySettings.shadows != ShadowQuality.Disable)
                {
                    gameShadows = QualitySettings.shadows;
                    QualitySettings.shadows = ShadowQuality.Disable;
                }
            }
            else if (gameShadows.HasValue)
            {
                QualitySettings.shadows = gameShadows.Value;
                gameShadows = null;
            }

            // Shaders that showed up after the plan was made are tested at its end
            if (plan != null)
                foreach (var key in counts.Keys)
                    if (!plan.Contains(key))
                        plan.Add(key);
        }

        private Original OriginalOf(Material material)
        {
            if (originals.TryGetValue(material, out var original))
                return original;
            if (material.shader == safe)
            {
                // A copy the game made of a material already swapped (renderer.material): its source's shader
                byName.TryGetValue(material.name.Replace(" (Instance)", ""), out original);
            }
            else
            {
                original = new Original { Shader = material.shader, Keywords = material.shaderKeywords, Queue = material.renderQueue };
                if (!byName.ContainsKey(material.name))
                    byName[material.name] = original;
            }
            originals[material] = original;
            return original;
        }

        private void Hold(Renderer renderer, bool held)
        {
            if (held)
            {
                if (!renderer.forceRenderingOff)
                {
                    renderer.forceRenderingOff = true;
                    forcedOff.Add(renderer);
                }
            }
            else if (forcedOff.Remove(renderer))
                renderer.forceRenderingOff = false;
        }

        // ---- Steps and launch tests

        private void Update()
        {
            if (quitAt >= 0 && Time.realtimeSinceStartup >= quitAt)
                Application.Quit();
            PollXr();
            if (!lost && quitAt < 0 && phase != "done")
            {
                var ticking = InMainMenu() && Application.isFocused && xrRunning;
                if (ticking)
                    menuTime += Time.unscaledDeltaTime;
                else if (running != null)
                    Interrupt(InMainMenu() ? "the game lost focus" : "the main menu was left");
                if (phase == "A")
                    StepPhaseA(ticking);
                else
                    LaunchTest(ticking);
            }
            UpdateHud();
        }

        private void StepPhaseA(bool ticking)
        {
            if (!ticking)
                return;
            if (current == null)
            {
                // Everything held back from the menu's first frame on; other steps let the menu settle first
                var baseline = !passed.Contains("baseline");
                if (!baseline && menuTime < SettleSeconds)
                    return;
                current = baseline ? "baseline" : NextStep();
                if (current == null)
                {
                    phase = "B";
                    Note($"phase A done; guilty: {GuiltyList()}. Next launch: only those held back from the start");
                    Save();
                    QuitSoon("Steps done. The game closes now: start it again.");
                    return;
                }
                stepTime = 0;
                running = current;
                Save();
                Debug.Log($"[GpuProbe] step {StepNumber()} start: {current}");
            }

            if (running == null)
            {
                running = current;
                Save();
                Debug.Log($"[GpuProbe] step {StepNumber()} start again: {current}");
            }
            stepTime += Time.unscaledDeltaTime;
            if (stepTime < StepSeconds)
                return;
            passed.Add(current);
            running = null;
            Note($"step {StepNumber()} passed: {current}");
            if (current == "baseline")
                Note(Inventory());
            current = null;
            Save();
        }

        private string NextStep()
        {
            if (plan == null)
            {
                plan = new List<string>(Features);
                plan.AddRange(counts.Keys.OrderBy(k => k.StartsWith("shader:RepoQuest/", StringComparison.Ordinal) ? 0 : 1)
                    .ThenByDescending(k => visibleCounts.TryGetValue(k, out var v) ? v : 0)
                    .ThenByDescending(k => counts[k]).ThenBy(k => k, StringComparer.Ordinal));
                Note($"plan: {plan.Count} steps: {string.Join(", ", plan)}");
            }
            return plan.FirstOrDefault(k => !passed.Contains(k) && !guilty.ContainsKey(k));
        }

        private string StepNumber() =>
            current == "baseline" || plan == null ? "0" : $"{plan.IndexOf(current) + 1}/{plan.Count}";

        private void LaunchTest(bool ticking)
        {
            if (!ticking)
                return;
            if (running == null)
            {
                running = "launch";
                Save();
                Debug.Log($"[GpuProbe] launch test start, held back from the start: {HeldDescription()}");
            }
            if (menuTime < SurviveSeconds)
                return;

            running = null;
            if (phase == "B")
                Finish(guilty.Count == 0
                    ? "no step hung and the menu survives with nothing held back: the hang did not come back"
                    : $"confirmed: holding back {GuiltyList()} keeps the menu alive");
            else
            {
                var suspect = StartSuspects[launchTest];
                guilty[suspect] = "launch test";
                Finish($"the menu's first frames hang unless {suspect} is held back (with {GuiltyList()})");
            }
        }

        /// <summary>
        /// Leaving the menu (a menu button) or the game (Home: Pico then kills it) is not a hang: the step or launch test
        /// starts over. A hang reaches XrLost first, while the game is still focused.
        /// </summary>
        private void Interrupt(string how)
        {
            if (lost || quitAt >= 0 || phase == "done")
                return;
            Note($"{(phase == "A" ? $"step {StepNumber()}: {current}" : "launch test")} interrupted ({how}); it starts over");
            running = null;
            stepTime = 0;
            if (phase != "A")
                menuTime = 0;
            Save();
        }

        private void OnApplicationPause(bool paused)
        {
            if (paused && running != null)
                Interrupt("the game was paused");
        }

        private void StepHung(string key, string how)
        {
            if (key == "baseline")
            {
                if (!multipass && SwitchStereo(toMultipass: true))
                {
                    multipass = true;
                    passed.Clear();
                    guilty.Clear();
                    plan = null;
                    Note($"hang with everything held back ({how}): next launch starts over in multi-pass stereo");
                }
                else
                    Finish($"hang with everything held back ({how}){(multipass ? ", in multi-pass stereo too" : "")}");
                return;
            }
            guilty[key] = how;
            Note($"GUILTY: {key} ({how})");
        }

        private void LaunchHung(string how)
        {
            if (phase == "B")
            {
                phase = "C";
                launchTest = 0;
                Note($"confirmation launch hung ({how}): the menu's first frames need more held back; launch tests next");
                return;
            }
            Note($"launch test hung with {StartSuspects[launchTest]} held back ({how})");
            if (++launchTest >= StartSuspects.Length)
                Finish("every launch test hung");
        }

        private void Finish(string summary)
        {
            phase = "done";
            Note("FINISHED: " + summary);
            if (multipass)
                SwitchStereo(toMultipass: false);
            File.Delete(requestPath);
            Save();
        }

        // ---- Losing XR

        private void OnLog(string message, string stackTrace, LogType type)
        {
            if (message.StartsWith("Shutting down OpenXR", StringComparison.Ordinal) || message.Contains("Unrecoverable runtime error"))
                XrLost(message.Trim());
        }

        private void PollXr()
        {
            SubsystemManager.GetSubsystems(displays);
            var now = displays.Any(d => d.running);
            if (xrRunning && !now)
                XrLost("the XR display stopped");
            xrRunning = now;
        }

        private void XrLost(string how)
        {
            if (lost || quitAt >= 0 || phase == "done")
                return;
            // Leaving the game (Home) stops XR too, but the game is no longer focused by then; a hang stops it in focus
            if (!Application.isFocused)
            {
                Note($"XR stopped while the game was not focused ({how}); nothing blamed");
                return;
            }
            var inMenu = InMainMenu();
            if (phase == "A" && current != null)
                StepHung(current, how);
            else if (phase == "A" && inMenu)
            {
                // Before any step of this launch: the menu's first frames, with the steps passed so far let back
                phase = "C";
                launchTest = 0;
                Note($"hang at the menu's start before a step ({how}): launch tests next");
            }
            else if (phase is "B" or "C" && running != null)
                LaunchHung(how);
            else
            {
                Note($"XR lost outside the main menu ({how}); nothing blamed");
                return;
            }
            lost = true;
            running = null;
            Save();
            QuitSoon("XR stopped. The game closes now: start it again.");
        }

        private void QuitSoon(string message)
        {
            if (hud)
                hud.text = message;
            quitAt = Time.realtimeSinceStartup + 6f;
        }

        // ---- RepoXR's stereo setting (read when XR starts, so it counts from the next launch)

        private bool SwitchStereo(bool toMultipass)
        {
            if (!File.Exists(configPath))
            {
                Note($"no {configPath}: cannot switch stereo rendering");
                return false;
            }
            var lines = File.ReadAllLines(configPath);
            for (var i = 0; i < lines.Length; i++)
            {
                if (!lines[i].StartsWith("SinglePassRendering = ", StringComparison.Ordinal))
                    continue;
                if (toMultipass)
                {
                    originalSinglePass = lines[i].Substring("SinglePassRendering = ".Length).Trim();
                    lines[i] = "SinglePassRendering = false";
                }
                else
                    lines[i] = "SinglePassRendering = " + (originalSinglePass ?? "true");
                File.WriteAllLines(configPath, lines);
                return true;
            }
            Note("SinglePassRendering not in RepoXR's config: cannot switch stereo rendering");
            return false;
        }

        // ---- Main menu

        private static bool InMainMenu()
        {
            if (runInstance == null)
                return false;
            var run = runInstance.GetValue(null) as UnityEngine.Object;
            if (!run)
                return false;
            var level = levelCurrent.GetValue(run) as UnityEngine.Object;
            return level && level == levelMainMenu.GetValue(run) as UnityEngine.Object;
        }

        // ---- State, report

        private void Reset()
        {
            phase = "A";
            passed.Clear();
            guilty.Clear();
            history.Clear();
            launchTest = 0;
            multipass = false;
            originalSinglePass = null;
            running = null;
            launches = 0;
        }

        private void Load()
        {
            if (!File.Exists(statePath))
                return;
            foreach (var line in File.ReadAllLines(statePath))
            {
                var parts = line.Split('\t');
                var value = parts.Length > 1 ? parts[1] : "";
                switch (parts[0])
                {
                    case "phase": phase = value; break;
                    case "passed": passed.Add(value); break;
                    case "guilty": guilty[value] = parts.Length > 2 ? parts[2] : ""; break;
                    case "history": history.Add(value); break;
                    case "launchTest": int.TryParse(value, out launchTest); break;
                    case "multipass": multipass = value == "1"; break;
                    case "singlePass": originalSinglePass = value; break;
                    case "running": running = value; break;
                    case "launches": int.TryParse(value, out launches); break;
                }
            }
        }

        private void Save()
        {
            var s = new StringBuilder();
            s.Append("phase\t").Append(phase).Append('\n');
            s.Append("launches\t").Append(launches).Append('\n');
            s.Append("launchTest\t").Append(launchTest).Append('\n');
            s.Append("multipass\t").Append(multipass ? "1" : "0").Append('\n');
            if (originalSinglePass != null)
                s.Append("singlePass\t").Append(originalSinglePass).Append('\n');
            if (running != null)
                s.Append("running\t").Append(running).Append('\n');
            foreach (var key in passed)
                s.Append("passed\t").Append(key).Append('\n');
            foreach (var pair in guilty)
                s.Append("guilty\t").Append(pair.Key).Append('\t').Append(pair.Value).Append('\n');
            foreach (var line in history)
                s.Append("history\t").Append(line).Append('\n');
            File.WriteAllText(statePath, s.ToString());

            var report = new StringBuilder();
            report.AppendLine($"GPU probe, {DateTime.Now:yyyy-MM-dd HH:mm:ss}; phase {phase}, launch {launches}" +
                              (multipass ? ", multi-pass stereo" : ""));
            report.AppendLine($"guilty: {GuiltyList()}");
            report.AppendLine($"passed: {string.Join(", ", passed)}");
            report.AppendLine();
            foreach (var line in history)
                report.AppendLine(line);
            File.WriteAllText(reportPath, report.ToString());
        }

        private void Note(string message)
        {
            var prefix = $"{DateTime.Now:HH:mm:ss} ";
            foreach (var line in message.Replace("\t", " ").Split('\n'))
            {
                history.Add(prefix + line);
                prefix = "         ";
                Debug.Log("[GpuProbe] " + line);
            }
        }

        private string GuiltyList() =>
            guilty.Count == 0 ? "none" : string.Join(", ", guilty.Select(p => $"{p.Key} ({p.Value})"));

        private string HeldDescription()
        {
            var held = Features.Where(IsHeld).ToList();
            if (IsHeld("shader:*"))
                held.Add("all shaders");
            held.AddRange(guilty.Keys.Where(k => k.StartsWith("shader:", StringComparison.Ordinal) && k != "shader:*"));
            return held.Count == 0 ? "nothing" : string.Join(", ", held.Distinct());
        }

        /// <summary>What the menu draws with: written once, after the first step</summary>
        private string Inventory()
        {
            var s = new StringBuilder();
            var desc = XRSettings.eyeTextureDesc;
            s.Append($"inventory: stereo {XRSettings.stereoRenderingMode}, eye texture {desc.width}x{desc.height} " +
                     $"x{desc.volumeDepth} {desc.graphicsFormat}; quality: shadows {gameShadows ?? QualitySettings.shadows}, " +
                     $"pixel lights {QualitySettings.pixelLightCount}; compute shaders {SystemInfo.supportsComputeShaders}");
            foreach (var camera in FindObjectsByType<Camera>(FindObjectsInactive.Exclude, FindObjectsSortMode.None))
                s.Append($"\n  camera {camera.name}: enabled {camera.enabled}, path {camera.renderingPath}/{camera.actualRenderingPath}, " +
                         $"eye {camera.stereoTargetEye}, target {(camera.targetTexture ? $"{camera.targetTexture.name} {camera.targetTexture.graphicsFormat}" : "-")}, " +
                         $"depth {camera.depth}");
            var lights = FindObjectsByType<Light>(FindObjectsInactive.Exclude, FindObjectsSortMode.None);
            s.Append($"\n  lights: {lights.Length} ({lights.Count(l => l.shadows != LightShadows.None)} with shadows): " +
                     string.Join(", ", lights.GroupBy(l => l.type).Select(g => $"{g.Key} {g.Count()}")));
            var renderers = FindObjectsByType<Renderer>(FindObjectsInactive.Exclude, FindObjectsSortMode.None);
            s.Append($"\n  renderers: {renderers.Length}, skinned {renderers.Count(r => r is SkinnedMeshRenderer)}, " +
                     $"particles {renderers.Count(r => r is ParticleSystemRenderer)}");
            foreach (var pair in counts.OrderByDescending(p => p.Value))
                s.Append($"\n  {pair.Key}: {pair.Value} ({(visibleCounts.TryGetValue(pair.Key, out var v) ? v : 0)} visible)");
            // Particle variants (soft particles read depth, distortion grabs the screen): the suspects after the first run
            foreach (var group in originals.Values.Where(o => o?.Shader != null && o.Shader.name.Contains("Particles"))
                         .GroupBy(o => $"{o.Shader.name} [{string.Join(" ", o.Keywords.OrderBy(k => k))}] queue {o.Queue}")
                         .OrderBy(g => g.Key, StringComparer.Ordinal))
                s.Append($"\n  particle material {group.Key}: {group.Count()}");
            return s.ToString();
        }

        // ---- Progress in front of the eyes

        private void CreateHud()
        {
            var go = new GameObject("GPU Probe HUD") { hideFlags = HideFlags.HideAndDontSave };
            DontDestroyOnLoad(go);
            canvas = go.AddComponent<Canvas>();
            canvas.renderMode = RenderMode.WorldSpace;
            canvas.sortingOrder = 1000;
            var rect = (RectTransform)go.transform;
            rect.sizeDelta = new Vector2(900, 220);
            rect.localScale = Vector3.one * 0.001f;

            var text = new GameObject("Text") { hideFlags = HideFlags.HideAndDontSave };
            text.transform.SetParent(go.transform, false);
            hud = text.AddComponent<Text>();
            hud.font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
            hud.fontSize = 34;
            hud.alignment = TextAnchor.MiddleCenter;
            hud.color = new Color(1f, 0.85f, 0.2f);
            var textRect = (RectTransform)text.transform;
            textRect.anchorMin = Vector2.zero;
            textRect.anchorMax = Vector2.one;
            textRect.sizeDelta = Vector2.zero;
        }

        private void UpdateHud()
        {
            if (!hud || quitAt >= 0)
                return;
            hud.text = phase switch
            {
                "done" => "GPU probe finished. Send files/gpu-probe and the log.",
                _ when !InMainMenu() => "GPU probe: waiting for the main menu",
                "A" when current == null => $"GPU probe: next step in a moment · guilty so far: {guilty.Count}",
                "A" => $"GPU probe step {StepNumber()}: {current}\nguilty so far: {guilty.Count} · keep the headset on, look ahead",
                _ => $"GPU probe launch test ({(phase == "B" ? "confirmation" : $"{launchTest + 1}/{StartSuspects.Length}")}): " +
                     $"{Mathf.Max(0, SurviveSeconds - menuTime):0} s\nheld back: {HeldDescription()}",
            };
        }

        private void LateUpdate()
        {
            if (!canvas)
                return;
            var eye = Camera.allCameras.FirstOrDefault(c => !IsSecondary(c)) ?? Camera.main;
            if (!eye)
                return;
            var forward = eye.transform.forward;
            canvas.transform.position = eye.transform.position + forward * 1.4f - eye.transform.up * 0.35f;
            canvas.transform.rotation = Quaternion.LookRotation(forward, eye.transform.up);
            // A layer the eyes' camera draws
            var layer = 0;
            while (layer < 31 && (eye.cullingMask & (1 << layer)) == 0)
                layer++;
            canvas.gameObject.layer = layer;
            hud.gameObject.layer = layer;
        }
    }
}
