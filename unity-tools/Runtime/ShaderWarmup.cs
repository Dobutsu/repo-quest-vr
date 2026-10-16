using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.UI;
using UnityEngine.XR;

namespace RepoQuest
{
    /// <summary>
    /// Pico 4 (Adreno 650) hangs the GPU in the main menu ("kgsl-3d0: GPU hang detected", then xrAcquireSwapchainImage
    /// fails and Unity shuts XR down) on most launches, with a warm Vulkan pipeline cache too. Letting renderers in a
    /// few at a time stops it (tested 16.10 on a friend's Pico 4: no hang in the menu or a level after that), so:
    /// every renderer whose material variant (renderer kind | shader | keywords) has not come in yet is held back
    /// (forceRenderingOff), and the variants come in one per RevealSeconds, in the real cameras. A variant that went
    /// SettleSeconds without XR being lost is listed in files/shader-warmup/warm.txt and is not held next launch, so
    /// only the first launch and new places are slow. files/shader-warmup/log.txt tells what came in when.
    /// On for Pico only; files/shader-warmup containing "on" or "off" overrides that. Off while GpuHangProbe runs.
    /// UI, post-processing and deferred lighting are not held. Log lines start with [ShaderWarmup].
    /// </summary>
    public class ShaderWarmup : MonoBehaviour
    {
        private const float RevealSeconds = 0.5f;
        private const float SettleSeconds = 10f;
        private const float SaveSeconds = 2f;

        private string dir;
        private string header;
        private readonly HashSet<string> warm = new();
        private readonly HashSet<string> revealed = new();
        private readonly List<(string key, float time)> recent = new();
        private readonly List<string> pending = new();
        private readonly HashSet<string> pendingSet = new();
        private readonly Dictionary<Material, (Shader shader, string kind, string key)> keys = new();
        private readonly HashSet<Renderer> forcedOff = new();
        private readonly List<Material> materials = new();
        private readonly List<XRDisplaySubsystem> displays = new();
        private float lastReveal = -100f;
        private float nextSave;
        private int scannedFrame = -1;
        private bool xrRunning;
        private bool lost;
        private float quitAt = -1;
        private Canvas canvas;
        private Text hud;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
        private static void Install()
        {
            if (!Wanted())
                return;
            var go = new GameObject("RepoQuest Shader Warmup");
            DontDestroyOnLoad(go);
            go.hideFlags = HideFlags.HideAndDontSave;
            go.AddComponent<ShaderWarmup>();
        }

        private static bool Wanted()
        {
            var data = Application.persistentDataPath;
            if (File.Exists(Path.Combine(data, "gpu-probe-request")))
                return false;
            if (SystemInfo.graphicsDeviceType != GraphicsDeviceType.Vulkan)
                return false;
            try
            {
                var choice = Path.Combine(data, "shader-warmup");
                if (File.Exists(choice))
                    return File.ReadAllText(choice).Trim().ToLowerInvariant() == "on";
            }
            catch (IOException) { }
            return Brand().Contains("pico");
        }

        private static string Brand()
        {
            try
            {
                using var build = new AndroidJavaClass("android.os.Build");
                return (build.GetStatic<string>("BRAND") + " " + build.GetStatic<string>("MANUFACTURER")).ToLowerInvariant();
            }
            catch (Exception)
            {
                return "";
            }
        }

        private string FilePath(string name) => Path.Combine(dir, name);

        private void Awake()
        {
            dir = Path.Combine(Application.persistentDataPath, "shader-warmup");
            Directory.CreateDirectory(dir);
            header = $"{Application.version} | {SystemInfo.graphicsDeviceName} | {SystemInfo.graphicsDeviceVersion}";

            try
            {
                var path = FilePath("warm.txt");
                var lines = File.Exists(path) ? File.ReadAllLines(path) : Array.Empty<string>();
                if (lines.Length > 0 && lines[0] == header)
                    warm.UnionWith(lines.Skip(1).Where(l => l.Length > 0));
            }
            catch (IOException) { }
            Note($"launch: {Application.version}, {Brand().Trim()}, {SystemInfo.graphicsDeviceName}; {warm.Count} variants warm");

            Camera.onPreCull += PreCull;
            Application.logMessageReceived += OnLog;
            CreateHud();
        }

        private void OnDestroy()
        {
            Camera.onPreCull -= PreCull;
            Application.logMessageReceived -= OnLog;
        }

        // ---- What is held back

        private void PreCull(Camera camera)
        {
            if (Time.frameCount == scannedFrame)
                return;
            scannedFrame = Time.frameCount;
            Scan();
        }

        /// <summary>Once per frame, before the first camera culls: hold back what would draw a variant not let in yet</summary>
        private void Scan()
        {
            foreach (var renderer in FindObjectsByType<Renderer>(FindObjectsInactive.Exclude, FindObjectsSortMode.None))
            {
                var kind = renderer switch
                {
                    SkinnedMeshRenderer => "skinned",
                    ParticleSystemRenderer => "particles",
                    _ => renderer.GetType().Name,
                };
                var held = false;
                renderer.GetSharedMaterials(materials);
                foreach (var material in materials)
                {
                    if (!material || !material.shader)
                        continue;
                    var key = KeyOf(material, kind);
                    if (warm.Contains(key) || revealed.Contains(key))
                        continue;
                    held = true;
                    if (pendingSet.Add(key))
                        pending.Add(key);
                }

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
            forcedOff.RemoveWhere(r => !r);
        }

        private string KeyOf(Material material, string kind)
        {
            if (keys.TryGetValue(material, out var cached) && cached.shader == material.shader && cached.kind == kind)
                return cached.key;
            var words = material.shaderKeywords;
            Array.Sort(words, StringComparer.Ordinal);
            var key = $"{kind}|{material.shader.name}|{string.Join(" ", words)}";
            keys[material] = (material.shader, kind, key);
            return key;
        }

        // ---- Letting variants in

        private void Update()
        {
            if (quitAt >= 0)
            {
                if (Time.realtimeSinceStartup >= quitAt)
                    Application.Quit();
                return;
            }
            PollXr();
            var now = Time.realtimeSinceStartup;
            if (!lost && Application.isFocused && pending.Count > 0 && now - lastReveal >= RevealSeconds)
            {
                var key = pending[0];
                pending.RemoveAt(0);
                pendingSet.Remove(key);
                revealed.Add(key);
                recent.Add((key, now));
                lastReveal = now;
                Note("in: " + key, log: false);
            }
            if (now >= nextSave)
            {
                nextSave = now + SaveSeconds;
                Settle(now);
            }
            if (hud)
            {
                hud.enabled = pending.Count > 0;
                hud.text = $"Preparing graphics for this headset: {pending.Count}";
            }
        }

        /// <summary>Variants that went SettleSeconds without XR being lost are warm</summary>
        private void Settle(float now)
        {
            if (lost || !recent.Any(r => now - r.time >= SettleSeconds))
                return;
            foreach (var (key, _) in recent.Where(r => now - r.time >= SettleSeconds))
                warm.Add(key);
            recent.RemoveAll(r => now - r.time >= SettleSeconds);
            try
            {
                File.WriteAllLines(FilePath("warm.txt"), new[] { header }.Concat(warm));
            }
            catch (IOException e)
            {
                Debug.LogWarning($"[ShaderWarmup] cannot write {dir}: {e.Message}");
            }
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
            // Leaving the game (Home) stops XR too, but the game is no longer focused by then
            if (lost || !Application.isFocused)
                return;
            lost = true;
            var now = Time.realtimeSinceStartup;
            var last = recent.Where(r => now - r.time < SettleSeconds).Select(r => r.key).ToList();
            Note($"XR lost in focus ({how}); came in during the last {SettleSeconds:0} s: " +
                 (last.Count > 0 ? string.Join(" ; ", last) : "nothing"));
            // The headset shows nothing once XR is gone: quit rather than run on blind
            if (hud)
            {
                hud.enabled = true;
                hud.text = "The graphics stopped. The game closes now: start it again.";
            }
            quitAt = Time.realtimeSinceStartup + 6f;
        }

        private void Note(string message, bool log = true)
        {
            var line = $"{DateTime.Now:MM-dd HH:mm:ss.fff} {message}";
            try
            {
                File.AppendAllText(FilePath("log.txt"), line + "\n");
            }
            catch (IOException) { }
            if (log)
                Debug.Log("[ShaderWarmup] " + message);
        }

        // ---- Progress in front of the eyes, as GpuHangProbe shows its own

        private void CreateHud()
        {
            var go = new GameObject("Shader Warmup HUD") { hideFlags = HideFlags.HideAndDontSave };
            DontDestroyOnLoad(go);
            canvas = go.AddComponent<Canvas>();
            canvas.renderMode = RenderMode.WorldSpace;
            canvas.sortingOrder = 1000;
            var rect = (RectTransform)go.transform;
            rect.sizeDelta = new Vector2(900, 120);
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

        private void LateUpdate()
        {
            if (!canvas || !hud.enabled)
                return;
            var eye = Camera.allCameras.FirstOrDefault(c => c.targetTexture == null && c.stereoTargetEye != StereoTargetEyeMask.None)
                      ?? Camera.main;
            if (!eye)
                return;
            var forward = eye.transform.forward;
            canvas.transform.position = eye.transform.position + forward * 1.4f - eye.transform.up * 0.45f;
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
