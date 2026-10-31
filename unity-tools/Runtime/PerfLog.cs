using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using Unity.Profiling;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;
using UnityEngine.XR;

namespace RepoQuest
{
    /// <summary>
    /// Frame rate where the headset logs none (Pico has no VrApi lines). Idle unless the version carries "-perf" or
    /// files/perf-log exists. Writes files/perf/log.txt: what the eyes are rendered with (device, stereo mode, eye
    /// texture, refresh rate, quality, RepoXR's config) at launch and on every level, then a line a second: FPS,
    /// main thread / render thread / GPU time (FrameTimingManager; needs Frame Timing Stats, QuestBuild turns them
    /// on), ShaderWarmup's scan time, garbage collections and Android's thermal status (3+ = throttling); under it the
    /// draw calls, batches and SetPass calls, the busiest threads (PerfThreads: user and kernel time, core, policy) and
    /// the Vulkan image copies and command pool resets (PerfVk: on Pico 4 Ultra the driver allocates and frees GPU memory
    /// in the kernel for each copy).
    /// A second line per second: render passes, blits, GPU memory allocations, submits, fence waits, pipelines created
    /// (PerfVk.TakeMore), frames over 25/50/100 ms.
    /// An A/B test, one summary line per phase, after the place has settled: in the shop the whole list (ShopPhases,
    /// 10 s each), in the arena a short one (ArenaPhases, 8 s each: the round may end first, then the summary has what
    /// was measured); none elsewhere. A file files/perf-phases-shop or perf-phases-arena with one phase name a line
    /// replaces a list; files/perf-phases gives the first other level of a launch a list too.
    /// "as is (profiling)" is where scripts/pico-perf8.sh profiles over adb. To get there without playing a run, the game
    /// goes straight to the places of files/perf-levels (one a line: "shop", "arena" or a level's name such as "shop
    /// harbor"; "off" for none; without the file DefaultLevels): each level change but to the menus goes to the first
    /// one not reached yet, through RunManager's debugLevel (what the game's own "/level" command sets). Before and after a test, a census of the
    /// place: lights, renderers, visible shaders, canvases, rigidbodies, animators.
    /// Also: lights (pixel lights 0, real-time lights off), far clip 50%, grabbable items hidden, world-space UI off,
    /// every other mesh hidden, LOD bias 30%, textures at half resolution, physics at half rate, animators off, audio
    /// paused; image copies done as blits, Camera Top without its depth texture, Camera Top off, every eye camera but the
    /// first off, HDR off on every camera, eye texture at LowScale. (On Pico 4 Ultra the copy was RepoXR's focus
    /// sphere's GrabPass, rendered by Camera Overlay every frame: patches/repoxr/0008.)
    /// Also available: render-texture cameras, shadows, particles, skinned meshes, video players, post-processing, Lamp
    /// Lighting, ShaderWarmup's scan off, all of those; main and render threads on the fastest cores, 72 Hz, CPU and GPU
    /// level hints through PerfXr, the render thread without its real-time priority (none changed Pico 4 Ultra's 13 FPS;
    /// the level hints and SCHED_FIFO cannot be put back, so they go last, and later places' logs say they were left).
    /// </summary>
    public class PerfLog : MonoBehaviour
    {
        private const float LowScale = 0.6f;
        private const string Profiling = "as is (profiling)";
        private const string Normal = "render thread normal priority";
        private const string FastCores = "main and render threads on fast cores";
        private const string Rate72 = "72 Hz";
        private const string LevelsHigh = "CPU and GPU levels high";
        private const string LevelsBoost = "CPU and GPU levels boost";
        private const string NoCopies = "image copies skipped";
        private const string Blits = "image copies as blits";
        private const string HdrOff = "HDR off on all cameras";
        private const string Overlays = "eye cameras other than the first off";
        private const string TopNoDepth = "Camera Top without depth texture";
        private const string TopOff = "Camera Top off";
        private const string OverlayOff = "Camera Overlay off";
        private const string RtCameras = "render-texture cameras off";
        private const string Shadows = "shadows off";
        private const string Particles = "particles hidden";
        private const string Skinned = "skinned meshes hidden";
        private const string Videos = "video players paused";
        private const string PostProcessing = "post-processing off";
        private const string Lamps = "lamp lighting off";
        private const string Scan = "warmup scan paused";
        private static readonly string LowEyes = $"eye texture {LowScale:0%}";
        private const string PixelLights = "pixel lights 0";
        private const string LightsOff = "real-time lights off";
        private const string FarClip = "far clip 50%";
        private const string Items = "items hidden";
        private const string WorldUi = "world-space UI off";
        private const string HalfMeshes = "every other mesh hidden";
        private const string Lod = "LOD bias 30%";
        private const string Mips = "textures at half resolution";
        private const string Physics = "physics at half rate";
        private const string Animators = "animators off";
        private const string Audio = "audio paused";
        private const string AllOff = "all of the above";

        private enum Place { None, Shop, Arena, Level }

        // On Pico 4 Ultra the shop and the arena lag. The likeliest first (players may leave the shop early), then the
        // earlier builds' tests, then what cannot be put back
        private static readonly string[] ShopPhases =
        {
            "as is", Profiling, NoCopies, LowEyes, PixelLights, LightsOff, Items, FarClip, WorldUi, HalfMeshes, Particles,
            Shadows, Skinned, OverlayOff, Physics, Animators, Audio, Lod, Mips, AllOff, Blits, TopNoDepth, TopOff, Overlays,
            HdrOff, RtCameras, Videos, PostProcessing, Lamps, Scan, Rate72, FastCores, "as is again", LevelsHigh, LevelsBoost,
        };

        // The round may be short: the likeliest first; the render thread's priority only here (the last place of a run)
        private static readonly string[] ArenaPhases =
        {
            "as is", Profiling, NoCopies, LowEyes, PixelLights, Items, FarClip, Particles, Skinned, OverlayOff, Physics,
            AllOff, "as is again", LevelsBoost, Normal,
        };

        private string path;
        private readonly FrameTiming[] timing = new FrameTiming[1];
        private readonly List<XRDisplaySubsystem> displays = new();
        private readonly List<float> main = new(), render = new(), gpu = new();
        private int frames;
        private float frameSeconds, worstFrame;
        private float secondStart;
        private int gcCount;
        private string level = "";
        private AndroidJavaObject power;
        private PerfThreads threads;
        private int mainTid;
        private ulong fastCores;
        private ProfilerRecorder drawCalls, batches, setPass, triangles, vertices, gcAlloc;
        private int over25, over50, over100;

        // The A/B test
        private string[] phases = Array.Empty<string>();
        private int phase = -1;
        private float phaseStart;
        private float phaseSeconds = 12f, profilingSeconds = 30f;
        private bool levelTested;
        private string leftOver = "";
        private float originalScale;
        private readonly Dictionary<string, List<(float fps, float main, float render, float gpu)>> perPhase = new();

        private static FieldInfo runInstance, levelCurrent, debugLevel, levelShop, levelArena, levelList;

        // Going straight to the places being tested
        private static readonly string[] DefaultLevels = { "shop", "arena" };
        private List<string> levelQueue;
        private UnityEngine.Object forced;

        private Canvas canvas;
        private Text hud;

        public static bool Wanted() =>
            Application.version.Contains("-perf") || File.Exists(Path.Combine(Application.persistentDataPath, "perf-log"));

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        private static void Install()
        {
            if (!Wanted())
                return;
            var go = new GameObject("RepoQuest Perf Log");
            DontDestroyOnLoad(go);
            go.hideFlags = HideFlags.HideAndDontSave;
            go.AddComponent<PerfLog>();
        }

        private void Awake()
        {
            var dir = Path.Combine(Application.persistentDataPath, "perf");
            Directory.CreateDirectory(dir);
            path = Path.Combine(dir, "log.txt");

            var runManager = AppDomain.CurrentDomain.GetAssemblies()
                .Select(a => a.GetType("RunManager")).FirstOrDefault(t => t != null);
            if (runManager != null)
            {
                runInstance = runManager.GetField("instance", BindingFlags.Public | BindingFlags.Static);
                levelCurrent = runManager.GetField("levelCurrent");
                const BindingFlags any = BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance;
                debugLevel = runManager.GetField("debugLevel", any);
                levelShop = runManager.GetField("levelShop", any);
                levelArena = runManager.GetField("levelArena", any);
                levelList = runManager.GetField("levels", any);
            }

            try
            {
                using var player = new AndroidJavaClass("com.unity3d.player.UnityPlayer");
                using var activity = player.GetStatic<AndroidJavaObject>("currentActivity");
                power = activity.Call<AndroidJavaObject>("getSystemService", "power");
            }
            catch (Exception) { }

            mainTid = PerfThreads.CurrentTid();
            threads = new PerfThreads();
            drawCalls = ProfilerRecorder.StartNew(ProfilerCategory.Render, "Draw Calls Count");
            batches = ProfilerRecorder.StartNew(ProfilerCategory.Render, "Batches Count");
            setPass = ProfilerRecorder.StartNew(ProfilerCategory.Render, "SetPass Calls Count");
            triangles = ProfilerRecorder.StartNew(ProfilerCategory.Render, "Triangles Count");
            vertices = ProfilerRecorder.StartNew(ProfilerCategory.Render, "Vertices Count");
            gcAlloc = ProfilerRecorder.StartNew(ProfilerCategory.Memory, "GC Allocated In Frame");

            Write("");
            Write($"==== launch {Application.version}");
            Write(Device());
            PerfVk.Enable();
            Write($"cores: {PerfThreads.Cores(out fastCores)}; fastest {PerfThreads.Mask(fastCores)}; " +
                  $"debuggable {Debuggable()}; main thread {mainTid}; RepoQuestVk {PerfVk.Status}");
            SceneManager.sceneLoaded += (scene, _) => Changed($"scene {scene.name}");
            secondStart = Time.realtimeSinceStartup;
            gcCount = GC.CollectionCount(0);
            CreateHud();
            CreateToggles();
        }

        private void OnDestroy()
        {
            threads?.Dispose();
            drawCalls.Dispose();
            batches.Dispose();
            setPass.Dispose();
            triangles.Dispose();
            vertices.Dispose();
            gcAlloc.Dispose();
        }

        // ---- What the eyes are rendered with

        private static string Device()
        {
            var s = $"device: {SystemInfo.deviceModel} | {SystemInfo.graphicsDeviceName} | {SystemInfo.graphicsDeviceVersion} | " +
                    $"{SystemInfo.processorType} x{SystemInfo.processorCount} | {SystemInfo.systemMemorySize} MB";
            try
            {
                using var build = new AndroidJavaClass("android.os.Build");
                s += $" | {build.GetStatic<string>("BRAND")} {build.GetStatic<string>("MODEL")} ({build.GetStatic<string>("DEVICE")})";
            }
            catch (Exception) { }
            var runtime = Type.GetType("UnityEngine.XR.OpenXR.OpenXRRuntime, Unity.XR.OpenXR");
            if (runtime != null)
                s += $" | OpenXR {runtime.GetProperty("name")?.GetValue(null)} {runtime.GetProperty("version")?.GetValue(null)}";
            return s;
        }

        /// <summary>ovrport marks the Pico APK debuggable (patch_mark_as_debuggable); Android runs such apps slower</summary>
        private static string Debuggable()
        {
            try
            {
                using var player = new AndroidJavaClass("com.unity3d.player.UnityPlayer");
                using var activity = player.GetStatic<AndroidJavaObject>("currentActivity");
                using var info = activity.Call<AndroidJavaObject>("getApplicationInfo");
                return (info.Get<int>("flags") & 2) != 0 ? "yes" : "no";
            }
            catch (Exception)
            {
                return "?";
            }
        }

        private string Rendering()
        {
            var desc = XRSettings.eyeTextureDesc;
            var hz = 0f;
            SubsystemManager.GetSubsystems(displays);
            var display = displays.FirstOrDefault(d => d.running);
            display?.TryGetDisplayRefreshRate(out hz);
            var s = $"rendering: stereo {XRSettings.stereoRenderingMode}, eye texture {desc.width}x{desc.height} x{desc.volumeDepth} " +
                    $"{desc.graphicsFormat} msaa {desc.msaaSamples}, scale {XRSettings.eyeTextureResolutionScale}, " +
                    $"viewport {XRSettings.renderViewportScale}, display {hz:0.#} Hz, xr {(display != null ? "running" : "off")}; " +
                    $"quality '{QualitySettings.names[QualitySettings.GetQualityLevel()]}', pixel lights {QualitySettings.pixelLightCount}, " +
                    $"shadows {QualitySettings.shadows}/{QualitySettings.shadowResolution} {QualitySettings.shadowDistance:0} m, " +
                    $"aa {QualitySettings.antiAliasing}, vsync {QualitySettings.vSyncCount}, target fps {Application.targetFrameRate}; " +
                    $"warmup {(ShaderWarmup.Instance ? $"on, {ShaderWarmup.Instance.Pending} pending" : "off")}" +
                    $"\n  cpuset {PerfThreads.ReadFile("/proc/self/cpuset")}, cores allowed {PerfThreads.AllowedCores()}; " +
                    $"PerfXr: {PerfXr.Status}, refresh rate {PerfXr.RefreshRate():0.#} of {PerfXr.RefreshRates()} Hz";
            foreach (var camera in Camera.allCameras.Where(c => c.isActiveAndEnabled))
            {
                var pp = camera.GetComponent("PostProcessLayer") as Behaviour;
                s += $"\n  camera {camera.name}: {camera.actualRenderingPath}, eye {camera.stereoTargetEye}, " +
                     $"target {(camera.targetTexture ? $"{camera.targetTexture.width}x{camera.targetTexture.height}" : "-")}, " +
                     $"hdr {camera.allowHDR}, pp {(pp == null ? "none" : pp.enabled ? "on" : "off")}, depth {camera.depth}, " +
                     $"clip {camera.nearClipPlane:0.##}-{camera.farClipPlane:0}, mask {camera.cullingMask:x}, " +
                     $"clear {camera.clearFlags}, msaa {camera.allowMSAA}, cmdbufs {camera.commandBufferCount}, " +
                     $"into rt {camera.forceIntoRenderTexture}, depth tex {camera.depthTextureMode}, " +
                     $"scripts {string.Join("/", camera.GetComponents<MonoBehaviour>().Where(b => b && b.enabled).Select(b => b.GetType().Name))}";
            }
            // Render textures the size of the eyes: the per-frame vkCmdCopyImage on Pico 4 Ultra copies the eyes into one
            var eyes = XRSettings.eyeTextureDesc;
            foreach (var rt in Resources.FindObjectsOfTypeAll<RenderTexture>().Where(r => r.width == eyes.width && r.height == eyes.height))
                s += $"\n  eye-size rt '{rt.name}': {rt.graphicsFormat} x{rt.volumeDepth} {rt.dimension}, depth {rt.depth}, " +
                     $"created {rt.IsCreated()}";
            var config = Path.Combine(Application.persistentDataPath, "RepoXR", "config", "io.daxcess.repoxr.cfg");
            try
            {
                if (File.Exists(config))
                    s += "\n  repoxr: " + string.Join(", ", File.ReadAllLines(config)
                        .Where(l => l.Length > 0 && !l.StartsWith("#") && !l.StartsWith("[") && l.Contains(" = "))
                        .Select(l => l.Replace(" = ", "=")));
            }
            catch (IOException) { }
            return s;
        }

        private string Level()
        {
            if (runInstance?.GetValue(null) is not UnityEngine.Object run || !run)
                return "";
            return levelCurrent.GetValue(run) is UnityEngine.Object current && current ? current.name : "";
        }

        /// <summary>A new place: describe it and run the A/B test there</summary>
        private void Changed(string what)
        {
            EndTest();
            Write($"---- {what}");
            Write(Rendering());
            if (leftOver.Length > 0)
                Write($"  left by earlier tests: {leftOver}");
            var name = Level();
            var place = Kind(name);
            var settle = 15f;
            (phaseSeconds, profilingSeconds) = (12f, 30f);
            switch (place)
            {
                case Place.Shop:
                    phases = Phases("perf-phases-shop", ShopPhases);
                    (phaseSeconds, profilingSeconds) = (10f, 30f);
                    break;
                case Place.Arena:
                    phases = Phases("perf-phases-arena", ArenaPhases);
                    (phaseSeconds, profilingSeconds, settle) = (8f, 20f, 5f);
                    break;
                case Place.Level when !levelTested:
                    phases = Phases("perf-phases", Array.Empty<string>());
                    levelTested = true;
                    break;
                default:
                    phases = Array.Empty<string>();
                    break;
            }
            if (phases.Length > 0)
                Write($"  {place}: test of {phases.Length} phases in {settle:0} s");
            phase = -1;
            phaseStart = Time.realtimeSinceStartup + settle;
            perPhase.Clear();
        }

        private static Place Kind(string name) =>
            name.Length == 0 || name.Contains("Menu") || name.Contains("Splash") ? Place.None :
            name.Contains("Shop") ? Place.Shop :
            name.Contains("Arena") ? Place.Arena : Place.Level;

        /// <summary>What the place holds that costs frames, from the scene itself</summary>
        private string Census()
        {
            var eye = Camera.main;
            var at = eye ? eye.transform.position : Vector3.zero;
            var lights = FindObjectsByType<Light>(FindObjectsSortMode.None).Where(l => l.isActiveAndEnabled).ToList();
            var local = lights.Where(l => l.type != LightType.Directional).ToList();
            var renderers = FindObjectsByType<Renderer>(FindObjectsSortMode.None)
                .Where(r => r.enabled && r.gameObject.activeInHierarchy).ToList();
            var visible = renderers.Where(r => r.isVisible).ToList();
            var bodies = FindObjectsByType<Rigidbody>(FindObjectsSortMode.None);
            var grab = Type.GetType("PhysGrabObject, REPO.Game") ?? AppDomain.CurrentDomain.GetAssemblies()
                .Select(a => a.GetType("PhysGrabObject")).FirstOrDefault(t => t != null);
            var shaders = visible.Select(r => r.sharedMaterial ? r.sharedMaterial.shader.name : "-")
                .GroupBy(n => n).OrderByDescending(g => g.Count()).Take(10).Select(g => $"{g.Key} {g.Count()}");
            return $"scene: lights {lights.Count} (directional {lights.Count - local.Count}, point {local.Count(l => l.type == LightType.Point)}, " +
                   $"spot {local.Count(l => l.type == LightType.Spot)}, within 15 m {local.Count(l => (l.transform.position - at).sqrMagnitude < 225f)}, " +
                   $"shadowed {lights.Count(l => l.shadows != LightShadows.None)}, forced pixel {lights.Count(l => l.renderMode == LightRenderMode.ForcePixel)}), " +
                   $"renderers {renderers.Count}, visible {visible.Count} (mesh {visible.Count(r => r is MeshRenderer)}, " +
                   $"skinned {visible.Count(r => r is SkinnedMeshRenderer)}, particles {visible.Count(r => r is ParticleSystemRenderer)}, " +
                   $"lines/trails {visible.Count(r => r is LineRenderer or TrailRenderer)}, sprites {visible.Count(r => r is SpriteRenderer)}, " +
                   $"shadow casters {visible.Count(r => r.shadowCastingMode != UnityEngine.Rendering.ShadowCastingMode.Off)}, " +
                   $"materials {visible.Sum(r => r.sharedMaterials.Length)}), " +
                   $"canvases world {FindObjectsByType<Canvas>(FindObjectsSortMode.None).Count(c => c.isActiveAndEnabled && c.renderMode == RenderMode.WorldSpace)}, " +
                   $"grabbable {(grab != null ? FindObjectsByType(grab, FindObjectsSortMode.None).Length.ToString() : "?")}, " +
                   $"rigidbodies {bodies.Length} awake {bodies.Count(b => !b.IsSleeping() && !b.isKinematic)}, " +
                   $"animators {FindObjectsByType<Animator>(FindObjectsSortMode.None).Count(a => a.isActiveAndEnabled)}, " +
                   $"audio playing {FindObjectsByType<AudioSource>(FindObjectsSortMode.None).Count(a => a.isPlaying)}, " +
                   $"fog {(RenderSettings.fog ? $"{RenderSettings.fogMode} {RenderSettings.fogDensity:0.###} {RenderSettings.fogStartDistance:0}-{RenderSettings.fogEndDistance:0}" : "off")}, " +
                   $"lod bias {QualitySettings.lodBias:0.##}, fixed dt {Time.fixedDeltaTime * 1000:0.#} ms" +
                   $"\n  visible shaders: {string.Join(", ", shaders)}";
        }

        private void ForceLevel(string current)
        {
            if (runInstance?.GetValue(null) is not UnityEngine.Object run || !run || debugLevel == null)
                return;
            levelQueue ??= LevelQueue();
            if (forced && forced.name == current)
            {
                Write($"  forced level {current} reached");
                levelQueue.RemoveAt(0);
                forced = null;
                debugLevel.SetValue(run, null);
            }
            while (!forced && levelQueue.Count > 0)
            {
                forced = FindLevel(run, levelQueue[0]);
                if (forced)
                {
                    debugLevel.SetValue(run, forced);
                    Write($"  the next level change goes to {forced.name}");
                }
                else
                {
                    Write($"  level '{levelQueue[0]}' not found");
                    levelQueue.RemoveAt(0);
                }
            }
        }

        private static List<string> LevelQueue()
        {
            try
            {
                var file = Path.Combine(Application.persistentDataPath, "perf-levels");
                if (File.Exists(file))
                    return File.ReadAllLines(file).Select(l => l.Trim().ToLowerInvariant())
                        .Where(l => l.Length > 0 && l != "off").ToList();
            }
            catch (IOException) { }
            return DefaultLevels.ToList();
        }

        private static IEnumerable<UnityEngine.Object> Levels(FieldInfo field, object run) =>
            (field?.GetValue(run) as System.Collections.IEnumerable)?.OfType<UnityEngine.Object>().Where(l => l) ??
            Enumerable.Empty<UnityEngine.Object>();

        /// <summary>"shop": any shop; "arena": the one-player arena (the game's first); else a level by name</summary>
        private static UnityEngine.Object FindLevel(object run, string what)
        {
            var shops = Levels(levelShop, run).ToList();
            var arenas = Levels(levelArena, run).ToList();
            if (what == "shop" && shops.Count > 0)
                return shops[UnityEngine.Random.Range(0, shops.Count)];
            if (what == "arena")
                return arenas.FirstOrDefault();
            return Levels(levelList, run).Concat(arenas).Concat(shops)
                .FirstOrDefault(l => l.name.ToLowerInvariant().Replace("level - ", "") == what);
        }

        // ---- Measuring

        private void Update()
        {
            var dt = Time.unscaledDeltaTime;
            frames++;
            frameSeconds += dt;
            worstFrame = Mathf.Max(worstFrame, dt);
            if (dt > 0.025f)
                over25++;
            if (dt > 0.05f)
                over50++;
            if (dt > 0.1f)
                over100++;
            FrameTimingManager.CaptureFrameTimings();
            if (FrameTimingManager.GetLatestTimings(1, timing) > 0)
            {
                main.Add((float)timing[0].cpuMainThreadFrameTime);
                render.Add((float)timing[0].cpuRenderThreadFrameTime);
                gpu.Add((float)timing[0].gpuFrameTime);
            }

            var now = Time.realtimeSinceStartup;
            Test(now);
            if (now - secondStart < 1f)
                return;

            var name = Level();
            if (name != level)
            {
                level = name;
                Changed($"level {(name.Length > 0 ? name : "(none)")}");
            }
            Run("forced level", () => ForceLevel(name));

            var fps = frames / Mathf.Max(frameSeconds, 0.001f);
            var gcs = GC.CollectionCount(0);
            var scan = ShaderWarmup.ScanMilliseconds / Mathf.Max(frames, 1);
            ShaderWarmup.ScanMilliseconds = 0;
            var tag = phase >= 0 && phase < phases.Length ? $" [{phases[phase]}]" : "";
            Write($"{fps,5:0.0} fps, worst {worstFrame * 1000:0} ms | main {Median(main),5:0.0} render {Median(render),5:0.0} " +
                  $"gpu {Median(gpu),5:0.0} ms | scan {scan:0.00} ms | gc {gcs - gcCount} | {Thermal()}" +
                  $"{(Application.isFocused ? "" : " | unfocused")}{tag}");
            Write($"  draws {Count(drawCalls)}, batches {Count(batches)}, setpass {Count(setPass)}, tris {Count(triangles)}, " +
                  $"verts {Count(vertices)}, gc alloc {Count(gcAlloc)} B | frames >25 ms {over25}, >50 {over50}, >100 {over100} | " +
                  $"{threads.Summary(5)}");
            Write($"  {PerfVk.Take(4)}");
            Write($"  {PerfVk.TakeMore(3)}");
            if (phase >= 0 && phase < phases.Length && Application.isFocused)
            {
                if (!perPhase.TryGetValue(phases[phase], out var list))
                    perPhase[phases[phase]] = list = new();
                list.Add((fps, Median(main), Median(render), Median(gpu)));
            }

            gcCount = gcs;
            over25 = over50 = over100 = 0;
            frames = 0;
            frameSeconds = worstFrame = 0;
            main.Clear();
            render.Clear();
            gpu.Clear();
            secondStart = now;
        }

        private static string Count(ProfilerRecorder recorder) => recorder.Valid ? recorder.LastValue.ToString() : "?";

        private static float Median(List<float> values)
        {
            if (values.Count == 0)
                return 0;
            values.Sort();
            return values[values.Count / 2];
        }

        private string Thermal()
        {
            try
            {
                // 0 none, 1 light, 2 moderate, 3 severe (throttling), 4+ critical
                if (power != null)
                    return $"thermal {power.Call<int>("getCurrentThermalStatus")}";
            }
            catch (Exception) { }
            return "thermal ?";
        }

        // ---- The A/B test: each phase switches one thing off, then puts it back

        private readonly Dictionary<string, (Action apply, Action undo)> toggles = new();
        private readonly List<Behaviour> switchedOff = new();
        private readonly List<Renderer> hidden = new();
        private readonly List<UnityEngine.Video.VideoPlayer> pausedVideos = new();
        private ShadowQuality originalShadows;
        private object lampEntry, lampFile;
        private List<string> allOffNames = new();
        private readonly List<(int tid, int policy, int priority)> schedulers = new();
        private readonly List<(int tid, ulong mask)> affinities = new();
        private float originalRate;
        private readonly List<Camera> hdrCameras = new();
        private Camera topCamera;
        private DepthTextureMode topDepthMode;
        private int originalPixelLights, originalMips;
        private float originalLod, originalFixed;
        private readonly List<(Camera camera, float far)> farClips = new();

        private void CreateToggles()
        {
            toggles[RtCameras] = (
                () => Disable(Camera.allCameras.Where(c => c.targetTexture != null && c.enabled)),
                Reenable);
            toggles[Shadows] = (
                () => { originalShadows = QualitySettings.shadows; QualitySettings.shadows = ShadowQuality.Disable; },
                () => QualitySettings.shadows = originalShadows);
            toggles[Particles] = (() => Hide(FindObjectsByType<ParticleSystemRenderer>(FindObjectsSortMode.None)), Unhide);
            toggles[Skinned] = (() => Hide(FindObjectsByType<SkinnedMeshRenderer>(FindObjectsSortMode.None)), Unhide);
            toggles[Videos] = (PauseVideos, ResumeVideos);
            toggles[PostProcessing] = (
                () => Disable(Camera.allCameras.Select(c => c.GetComponent("PostProcessLayer") as Behaviour)
                    .Where(b => b != null && b.enabled)),
                Reenable);
            toggles[Lamps] = (() => SetLampLighting(false), () => SetLampLighting(true));
            toggles[Scan] = (() => ShaderWarmup.ScanPaused = true, () => ShaderWarmup.ScanPaused = false);
            toggles[LowEyes] = (
                () => { originalScale = XRSettings.eyeTextureResolutionScale; XRSettings.eyeTextureResolutionScale = originalScale * LowScale; },
                () => XRSettings.eyeTextureResolutionScale = originalScale);
            toggles[PixelLights] = (
                () => { originalPixelLights = QualitySettings.pixelLightCount; QualitySettings.pixelLightCount = 0; },
                () => QualitySettings.pixelLightCount = originalPixelLights);
            toggles[LightsOff] = (() => Disable(FindObjectsByType<Light>(FindObjectsSortMode.None).Where(l => l.enabled)), Reenable);
            toggles[FarClip] = (
                () =>
                {
                    foreach (var camera in Camera.allCameras.Where(c => c.targetTexture == null && c.stereoTargetEye != StereoTargetEyeMask.None))
                    {
                        farClips.Add((camera, camera.farClipPlane));
                        camera.farClipPlane *= 0.5f;
                    }
                    Write($"  far clip: {string.Join(", ", farClips.Select(c => $"{c.camera.name} {c.far:0} -> {c.camera.farClipPlane:0}"))}");
                },
                () =>
                {
                    foreach (var (camera, far) in farClips.Where(c => c.camera))
                        camera.farClipPlane = far;
                    farClips.Clear();
                });
            toggles[Items] = (() => Hide(GrabbableRenderers()), Unhide);
            toggles[WorldUi] = (
                () => Disable(FindObjectsByType<Canvas>(FindObjectsSortMode.None)
                    .Where(c => c.enabled && c.renderMode == RenderMode.WorldSpace && c != canvas)),
                Reenable);
            toggles[HalfMeshes] = (
                () => Hide(FindObjectsByType<MeshRenderer>(FindObjectsSortMode.InstanceID)
                    .Where(r => r.enabled && r.gameObject.activeInHierarchy).Where((_, i) => i % 2 == 0)),
                Unhide);
            toggles[Lod] = (
                () => { originalLod = QualitySettings.lodBias; QualitySettings.lodBias = originalLod * 0.3f; },
                () => QualitySettings.lodBias = originalLod);
            toggles[Mips] = (
                () => { originalMips = QualitySettings.globalTextureMipmapLimit; QualitySettings.globalTextureMipmapLimit = originalMips + 1; },
                () => QualitySettings.globalTextureMipmapLimit = originalMips);
            toggles[Physics] = (
                () => { originalFixed = Time.fixedDeltaTime; Time.fixedDeltaTime = originalFixed * 2f; },
                () => Time.fixedDeltaTime = originalFixed);
            toggles[Animators] = (() => Disable(FindObjectsByType<Animator>(FindObjectsSortMode.None).Where(a => a.enabled)), Reenable);
            toggles[Audio] = (() => AudioListener.pause = true, () => AudioListener.pause = false);
            allOffNames = toggles.Keys.ToList();

            toggles[Normal] = (RenderThreadNormal, RestoreSchedulers);
            toggles[FastCores] = (ToFastCores, RestoreAffinities);
            toggles[Rate72] = (
                () =>
                {
                    originalRate = PerfXr.RefreshRate();
                    Write($"  refresh rate {originalRate:0.#} -> 72: {PerfXr.RequestRefreshRate(72f)}");
                },
                () =>
                {
                    if (originalRate > 0)
                        Write($"  refresh rate back to {originalRate:0.#}: {PerfXr.RequestRefreshRate(originalRate)}");
                });
            toggles[LevelsHigh] = (
                () =>
                {
                    Write($"  levels SUSTAINED_HIGH: {PerfXr.SetLevels(PerfXr.SustainedHigh)}");
                    leftOver = "CPU and GPU levels SUSTAINED_HIGH";
                },
                () => { });
            toggles[LevelsBoost] = (
                () =>
                {
                    Write($"  levels BOOST: {PerfXr.SetLevels(PerfXr.Boost)}");
                    leftOver = "CPU and GPU levels BOOST";
                },
                () => { });
            toggles[NoCopies] = (() => PerfVk.SetMode(PerfVk.SkipCopies), () => PerfVk.SetMode(PerfVk.Copy));
            toggles[Blits] = (() => PerfVk.SetMode(PerfVk.BlitCopies), () => PerfVk.SetMode(PerfVk.Copy));
            toggles[HdrOff] = (
                () =>
                {
                    foreach (var camera in Camera.allCameras.Where(c => c.allowHDR))
                    {
                        camera.allowHDR = false;
                        hdrCameras.Add(camera);
                    }
                    Write($"  HDR off: {string.Join(", ", hdrCameras.Select(c => c.name))}");
                },
                () =>
                {
                    foreach (var camera in hdrCameras.Where(c => c))
                        camera.allowHDR = true;
                    hdrCameras.Clear();
                });
            toggles[TopNoDepth] = (
                () =>
                {
                    topCamera = Camera.allCameras.FirstOrDefault(c => c.name == "Camera Top");
                    if (!topCamera)
                    {
                        Write("  no Camera Top");
                        return;
                    }
                    topDepthMode = topCamera.depthTextureMode;
                    topCamera.depthTextureMode = DepthTextureMode.None;
                    Write($"  Camera Top depth texture {topDepthMode} -> None");
                },
                () =>
                {
                    if (topCamera)
                        topCamera.depthTextureMode = topDepthMode;
                });
            toggles[TopOff] = (() => Disable(Camera.allCameras.Where(c => c.enabled && c.name == "Camera Top")), Reenable);
            toggles[OverlayOff] = (() => Disable(Camera.allCameras.Where(c => c.enabled && c.name == "Camera Overlay")), Reenable);
            toggles[Overlays] = (
                () => Disable(Camera.allCameras.Where(c => c.enabled && c.targetTexture == null && c.stereoTargetEye != StereoTargetEyeMask.None)
                    .OrderBy(c => c.depth).Skip(1)),
                Reenable);
        }

        private static string[] Phases(string name, string[] defaults)
        {
            try
            {
                var file = Path.Combine(Application.persistentDataPath, name);
                if (File.Exists(file))
                {
                    var listed = File.ReadAllLines(file).Select(l => l.Trim()).Where(l => l.Length > 0).ToArray();
                    if (listed.Length > 0)
                        return listed;
                }
            }
            catch (IOException) { }
            return defaults;
        }

        private void Apply(string what)
        {
            if (what == AllOff)
                foreach (var name in allOffNames)
                    Run(name, toggles[name].apply);
            else if (toggles.TryGetValue(what, out var toggle))
                Run(what, toggle.apply);
        }

        private void Undo(string what)
        {
            if (what == AllOff)
                foreach (var name in Enumerable.Reverse(allOffNames))
                    Run(name, toggles[name].undo);
            else if (toggles.TryGetValue(what, out var toggle))
                Run(what, toggle.undo);
        }

        private void Run(string name, Action action)
        {
            try
            {
                action();
            }
            catch (Exception e)
            {
                Write($"test {name} failed: {e.GetType().Name}: {e.Message}");
            }
        }

        private void Disable(IEnumerable<Behaviour> behaviours)
        {
            foreach (var behaviour in behaviours.ToList())
            {
                behaviour.enabled = false;
                switchedOff.Add(behaviour);
            }
            Write($"  switched off {switchedOff.Count}: {string.Join(", ", switchedOff.Select(b => b.name).Distinct().Take(12))}");
        }

        private void Reenable()
        {
            foreach (var behaviour in switchedOff.Where(b => b))
                behaviour.enabled = true;
            switchedOff.Clear();
        }

        private void Hide(IEnumerable<Renderer> renderers)
        {
            foreach (var renderer in renderers.Where(r => !r.forceRenderingOff).ToList())
            {
                renderer.forceRenderingOff = true;
                hidden.Add(renderer);
            }
            Write($"  hidden {hidden.Count}");
        }

        private void Unhide()
        {
            foreach (var renderer in hidden.Where(r => r))
                renderer.forceRenderingOff = false;
            hidden.Clear();
        }

        /// <summary>Everything a player can pick up: valuables, the shop's goods, weapons</summary>
        private IEnumerable<Renderer> GrabbableRenderers()
        {
            var grab = AppDomain.CurrentDomain.GetAssemblies().Select(a => a.GetType("PhysGrabObject")).FirstOrDefault(t => t != null);
            if (grab == null)
            {
                Write("  PhysGrabObject not found");
                return Array.Empty<Renderer>();
            }
            var objects = FindObjectsByType(grab, FindObjectsSortMode.None).OfType<Component>().ToList();
            Write($"  grabbable objects {objects.Count}");
            return objects.SelectMany(o => o.GetComponentsInChildren<Renderer>());
        }

        private void PauseVideos()
        {
            var all = FindObjectsByType<UnityEngine.Video.VideoPlayer>(FindObjectsSortMode.None);
            foreach (var player in all.Where(p => p.isActiveAndEnabled && p.isPlaying))
            {
                player.Pause();
                pausedVideos.Add(player);
            }
            Write($"  video players: {all.Length}, playing and paused {pausedVideos.Count}: " +
                  string.Join(", ", pausedVideos.Select(v => v.name).Distinct().Take(12)));
        }

        private void ResumeVideos()
        {
            foreach (var player in pausedVideos.Where(v => v))
                player.Play();
            pausedVideos.Clear();
        }

        /// <summary>The busiest thread with a real-time policy (the render thread on Pico), else the busiest one, not main.
        /// A real-time thread must be busy to count: Quest's AudioTrack is SCHED_FIFO at 1%</summary>
        private int RenderTid()
        {
            var others = threads.Latest.Where(t => t.Tid != mainTid).ToList();
            return (others.FirstOrDefault(t => t.Policy is 1 or 2 && t.Total >= 15f) ?? others.FirstOrDefault())?.Tid ?? 0;
        }

        private string Describe(int tid) =>
            $"{threads.Latest.FirstOrDefault(t => t.Tid == tid)?.Name ?? "?"}#{tid}";

        private void RenderThreadNormal()
        {
            var tid = RenderTid();
            if (tid == 0 || !PerfThreads.GetScheduler(tid, out var policy, out var priority))
            {
                Write($"  render thread {tid}: not found");
                return;
            }
            var done = PerfThreads.SetNormalScheduler(tid, policy, out var error);
            if (done)
            {
                schedulers.Add((tid, policy, priority));
                leftOver += (leftOver.Length > 0 ? ", " : "") + "render thread maybe OTHER";
            }
            Write($"  {Describe(tid)}: policy {policy}/{priority} -> OTHER: {(done ? "done" : error)}");
        }

        private void RestoreSchedulers()
        {
            foreach (var (tid, policy, priority) in schedulers)
                Write($"  {Describe(tid)} back to policy {policy}/{priority}: " +
                      $"{(PerfThreads.SetScheduler(tid, policy, priority, out var error) ? "done" : error)}");
            schedulers.Clear();
        }

        private void ToFastCores()
        {
            if (fastCores == 0)
            {
                Write("  core speeds unknown");
                return;
            }
            foreach (var tid in new[] { mainTid, RenderTid() }.Where(t => t != 0).Distinct())
            {
                if (!PerfThreads.GetAffinity(tid, out var mask, out var error))
                {
                    Write($"  {Describe(tid)}: {error}");
                    continue;
                }
                var done = PerfThreads.SetAffinity(tid, fastCores, out error);
                if (done)
                    affinities.Add((tid, mask));
                Write($"  {Describe(tid)}: cores {PerfThreads.Mask(mask)} -> {PerfThreads.Mask(fastCores)}: {(done ? "done" : error)}");
            }
        }

        private void RestoreAffinities()
        {
            foreach (var (tid, mask) in affinities)
                Write($"  {Describe(tid)} back to cores {PerfThreads.Mask(mask)}: " +
                      $"{(PerfThreads.SetAffinity(tid, mask, out var error) ? "done" : error)}");
            affinities.Clear();
        }

        /// <summary>RepoXR's Lamp Lighting setting, without saving the change to its config file</summary>
        private void SetLampLighting(bool on)
        {
            if (lampEntry == null)
            {
                var plugin = AppDomain.CurrentDomain.GetAssemblies()
                    .Select(a => a.GetType("RepoXR.Plugin")).FirstOrDefault(t => t != null);
                var config = plugin?.GetProperty("Config", BindingFlags.Public | BindingFlags.Static)?.GetValue(null);
                lampEntry = config?.GetType().GetProperty("LampLighting")?.GetValue(config);
                lampFile = lampEntry?.GetType().GetProperty("ConfigFile")?.GetValue(lampEntry);
                if (lampEntry == null)
                {
                    Write("  RepoXR's LampLighting not found");
                    return;
                }
            }
            var save = lampFile?.GetType().GetProperty("SaveOnConfigSet");
            var saving = save?.GetValue(lampFile);
            save?.SetValue(lampFile, false);
            lampEntry.GetType().GetProperty("Value").SetValue(lampEntry, on);
            if (saving != null)
                save.SetValue(lampFile, saving);
        }

        private void Test(float now)
        {
            if (phases.Length == 0 || now < phaseStart)
                return;
            if (phase >= 0)
                Undo(phases[phase]);
            phase++;
            phaseStart = now + (phase < phases.Length && phases[phase] == Profiling ? profilingSeconds : phaseSeconds);
            if (phase >= phases.Length)
            {
                Summary();
                phases = Array.Empty<string>();
                return;
            }
            Write($"test: {phases[phase]}");
            // Render textures, camera state and what is in the place once it has rendered a while
            if (phase == 0)
            {
                Write(Rendering());
                Run("census", () => Write(Census()));
            }
            Apply(phases[phase]);
        }

        private void EndTest()
        {
            if (phase >= 0 && phase < phases.Length)
            {
                Undo(phases[phase]);
                Write($"test interrupted at {phase + 1}/{phases.Length}");
                Summary();
            }
            phases = Array.Empty<string>();
            phase = -1;
        }

        private void Summary()
        {
            Run("census", () => Write(Census()));
            if (leftOver.Length > 0)
                Write($"summary after: {leftOver}");
            foreach (var (what, list) in perPhase)
            {
                // The first second of a phase still carries the one before
                var rest = list.Skip(1).ToList();
                if (rest.Count == 0)
                    continue;
                Write($"summary [{what}]: {rest.Average(x => x.fps):0.0} fps, main {rest.Average(x => x.main):0.0}, " +
                      $"render {rest.Average(x => x.render):0.0}, gpu {rest.Average(x => x.gpu):0.0} ms ({rest.Count} s)");
            }
        }

        private void Write(string line)
        {
            try
            {
                File.AppendAllText(path, (line.Length > 0 ? $"{DateTime.Now:MM-dd HH:mm:ss} {line}" : "") + "\n");
            }
            catch (IOException) { }
        }

        // ---- A line in front of the eyes while a test changes something, as ShaderWarmup shows its own

        private void CreateHud()
        {
            var go = new GameObject("Perf Log HUD") { hideFlags = HideFlags.HideAndDontSave };
            DontDestroyOnLoad(go);
            canvas = go.AddComponent<Canvas>();
            canvas.renderMode = RenderMode.WorldSpace;
            canvas.sortingOrder = 1000;
            var rect = (RectTransform)go.transform;
            rect.sizeDelta = new Vector2(900, 80);
            rect.localScale = Vector3.one * 0.001f;

            var text = new GameObject("Text") { hideFlags = HideFlags.HideAndDontSave };
            text.transform.SetParent(go.transform, false);
            hud = text.AddComponent<Text>();
            hud.font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
            hud.fontSize = 28;
            hud.alignment = TextAnchor.MiddleCenter;
            hud.color = new Color(1f, 0.85f, 0.2f);
            var textRect = (RectTransform)text.transform;
            textRect.anchorMin = Vector2.zero;
            textRect.anchorMax = Vector2.one;
            textRect.sizeDelta = Vector2.zero;
        }

        private void LateUpdate()
        {
            var testing = phase > 0 && phase < phases.Length;
            hud.enabled = testing;
            if (!testing)
                return;
            hud.text = $"perf test {phase}/{phases.Length - 1}: {phases[phase]}";
            var eye = Camera.allCameras.FirstOrDefault(c => c.targetTexture == null && c.stereoTargetEye != StereoTargetEyeMask.None)
                      ?? Camera.main;
            if (!eye)
                return;
            var forward = eye.transform.forward;
            canvas.transform.position = eye.transform.position + forward * 1.4f - eye.transform.up * 0.6f;
            canvas.transform.rotation = Quaternion.LookRotation(forward, eye.transform.up);
            var layer = 0;
            while (layer < 31 && (eye.cullingMask & (1 << layer)) == 0)
                layer++;
            canvas.gameObject.layer = layer;
            hud.gameObject.layer = layer;
        }
    }
}
