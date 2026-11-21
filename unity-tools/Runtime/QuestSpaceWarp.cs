using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Reflection;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.XR;

namespace RepoQuest
{
    /// <summary>
    /// Application SpaceWarp (AppSW) for the built-in pipeline: the game renders every other display frame (36 a second
    /// at 72 Hz) and the Quest's runtime makes the frames between from a motion vector image and its depth. SpaceWarpXr
    /// asks for the extension; this draws the motion vectors, which URP would draw in its XRDepthMotionPass and the
    /// built-in pipeline never does: into XRDisplaySubsystem.XRRenderPass.motionVectorRenderTarget (the runtime's
    /// low-resolution RGBA16F array, one slice per eye, with depth), at the end of each frame, one slice at a time.
    ///
    /// What is drawn follows what the PEAK port found on its headset (PEAK VR docs/headset-checks.md, sp96-asw1/asw2):
    /// with every object's own motion vectors the whole world shook while the player's rig moved, and with only the
    /// camera's motion at the far plane (URP's fill) and an empty depth "everything is great", the hands and held
    /// items jittering a little. So by default every pixel gets the camera's ROTATION between the last frame and this
    /// one (head and rig turns; translation left out, as at an infinite distance), and the depth stays cleared to far.
    /// The app space pose is identity, set once (the plugin's starts all zeros, not even a unit quaternion).
    ///
    /// Modes (files/command "spacewarp ...", or lines of files/spacewarp.txt read at start):
    ///   spacewarp                 status (also logged every 10 s while on)
    ///   spacewarp on|off          the runtime's SpaceWarp (off: the game back at the display rate at once)
    ///   spacewarp mode rotation   the camera's rotation only (default)
    ///   spacewarp mode far        the camera's whole motion at the far clip plane (URP's fill: in R.E.P.O.'s levels the
    ///                             far plane is 15-16 m, so the rig's translation shows a little)
    ///   spacewarp mode zero       no motion at all: the runtime reprojects from the head pose alone
    ///   spacewarp mode gradient   a test picture, not motion: x = the NDC y the pixel is believed to have (0.5 at the
    ///                             top); with Meta's overlay (debug.oculus.MVOverlay 4: red = +x) the top of the view
    ///                             must be red, else "spacewarp rasterflip"
    ///   spacewarp rasterflip      which way up the image rows are (the y of the pixels, not of the values)
    ///   spacewarp valueflip       the y of the motion values (left-handed NDC, y up, is what the Quest wants: PEAK)
    ///   spacewarp maxdeg 15       a turn of more than this many degrees in one frame (snap turn, a cut) gets no motion
    ///   spacewarp when postrender right after the eye camera (Camera.onPostRender) instead of at the end of the frame
    /// /sdcard/Android/data/io.repoxr.quest/files/spacewarp-off leaves the extension out (no SpaceWarp at all).
    /// </summary>
    public sealed class QuestSpaceWarp : MonoBehaviour
    {
        public enum Mode
        {
            Rotation = 0,
            Far = 1,
            Zero = 2,
            Gradient = 3,
        }

        private const float StatusEverySeconds = 10f;

        /// <summary>Off until RepoXR's Application SpaceWarp setting is read (Graphics Mode Performance, the default, has it off)</summary>
        public static bool Wanted { get; private set; }
        public static bool On { get; private set; }
        // Zero by default: the runtime ignores the vectors where the depth is far (appsw2), so the image is only cleared
        public static Mode MotionMode { get; private set; } = Mode.Zero;
        public static float RasterFlip { get; private set; } = 1f;
        public static float ValueFlip { get; private set; } = 1f;
        public static float MaxDegrees { get; private set; } = 15f;
        public static string LastResult { get; private set; } = "never switched";
        public enum DrawTime
        {
            /// <summary>Graphics.ExecuteCommandBuffer in Camera.onPostRender of the eye camera</summary>
            PostRender,
            /// <summary>a command buffer on the eye camera at CameraEvent.AfterEverything, filled in onPreRender</summary>
            CameraEvent,
            /// <summary>Graphics.ExecuteCommandBuffer after WaitForEndOfFrame (appsw1: the runtime saw nothing of it)</summary>
            EndFrame,
        }

        /// <summary>When the vectors are drawn ("spacewarp when postrender|cameraevent|endframe")</summary>
        public static DrawTime When { get; private set; } = DrawTime.PostRender;
        /// <summary>The depth written with the vectors: false = cleared to far only, true = a mid depth ("spacewarp depth near|far")</summary>
        public static bool DepthNear { get; private set; }
        /// <summary>The clip z written by "depth near" (appsw2: 0.5 made the whole view black; "spacewarp depth 0.9")</summary>
        public static float DepthValue { get; private set; } = 0.5f;

        public enum AppSpace
        {
            /// <summary>identity, set once (appsw1/appsw2)</summary>
            Off,
            /// <summary>the eye camera's parent (the tracking space in the world: stick turns and walking) every frame</summary>
            Rig,
            /// <summary>the tracking space's rotation only (stick turns), position zero</summary>
            Turn,
        }

        /// <summary>The app space pose handed to the runtime ("spacewarp appspace off|rig|turn"): with the depth at far
        /// the runtime fills the motion from the app space's change between frames, not from the vectors (appsw2)</summary>
        // Turn by default: with it Meta's overlay shows the runtime's own motion for the far pixels while the rig turns
        // (left turn +x, right turn -x, as it should be), with Off it stays grey (appsw4, 2026-11-21)
        public static AppSpace AppSpaceMode { get; private set; } = AppSpace.Turn;

        private static QuestSpaceWarp instance;
        private static readonly int ReprojectId = Shader.PropertyToID("_RepoQuestSWReproject");
        private static readonly int ParamsId = Shader.PropertyToID("_RepoQuestSWParams");
        private static readonly int ZWriteId = Shader.PropertyToID("_RepoQuestSWZWrite");
        private static readonly int DepthId = Shader.PropertyToID("_RepoQuestSWDepth");

        private CommandBuffer cameraCommands;
        private Camera cameraCommandsOn;
        private bool probeRequested;
        private RenderTexture probeTexture;

        private Material material;
        private CommandBuffer commands;
        private readonly List<XRDisplaySubsystem> displays = new List<XRDisplaySubsystem>();

        private Camera head;
        private float headCheckedAt = -1f;
        private int capturedFrame = -1;
        private bool hasPass;
        private int passCount;
        private RenderTargetIdentifier target;
        private RenderTextureDescriptor targetDesc;
        private readonly Matrix4x4[] views = new Matrix4x4[2];
        private readonly Matrix4x4[] projections = new Matrix4x4[2];
        private readonly Matrix4x4[] current = new Matrix4x4[2];
        private readonly Matrix4x4[] previous = new Matrix4x4[2];
        private readonly Matrix4x4[] reproject = new Matrix4x4[2];
        private Matrix4x4 previousView;
        private bool havePrevious;

        // Statistics since the last status line
        private int frames, drawn, noPass, cuts;
        private float degreesSum, degreesMax;
        private int rigFrames;
        private float rigMetresSum, rigMetresMax;
        private Vector3 rigLast;
        private Transform rigLastTransform;
        private float statsSince;
        private bool appSpaceIdentity = true;
        private string appSpaceResult = "";
        private float spinSpeed, spinUntil;
        // RepoXR's "Application SpaceWarp" setting (Graphics Mode, patch 0041), read once a second
        private PropertyInfo repoXRConfig, spaceWarpSetting;
        private bool? settingSeen;
        private float settingAt;
        private bool loggedTarget;

        public static bool ExtensionOff()
        {
            try
            {
                return File.Exists(Path.Combine(Application.persistentDataPath, "spacewarp-off"));
            }
            catch (Exception)
            {
                return false;
            }
        }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        private static void Install()
        {
            if (Application.platform != RuntimePlatform.Android || SpaceWarpXr.Instance == null)
                return;
            var go = new GameObject("RepoQuest SpaceWarp");
            DontDestroyOnLoad(go);
            go.hideFlags = HideFlags.HideAndDontSave;
            go.AddComponent<QuestSpaceWarp>();
        }

        private void Awake()
        {
            instance = this;
            var shader = Shader.Find("Hidden/RepoQuest/SpaceWarpMotion");
            if (shader == null || !shader.isSupported)
            {
                Debug.LogError($"[QuestSpaceWarp] motion vector shader {(shader == null ? "missing" : "not supported")}: SpaceWarp stays off");
                Wanted = false;
                enabled = false;
                return;
            }
            material = new Material(shader) { hideFlags = HideFlags.HideAndDontSave };
            commands = new CommandBuffer { name = "RepoQuest SpaceWarp motion vectors" };
            ReadConfig();
            Camera.onPreRender += PreRender;
            Camera.onPostRender += PostRender;
            StartCoroutine(EndOfFrames());
            statsSince = Time.unscaledTime;
            Debug.Log($"[QuestSpaceWarp] ready: wanted {Wanted}, mode {MotionMode}, feature {SpaceWarpXr.Status}");
        }

        private void OnDestroy()
        {
            Camera.onPreRender -= PreRender;
            Camera.onPostRender -= PostRender;
            AttachCameraCommands(null);
            if (instance == this)
                instance = null;
        }

        private static void ReadConfig()
        {
            try
            {
                var path = Path.Combine(Application.persistentDataPath, "spacewarp.txt");
                if (!File.Exists(path))
                    return;
                foreach (var line in File.ReadAllLines(path).Select(l => l.Trim()).Where(l => l.Length > 0 && !l.StartsWith("#")))
                {
                    var words = ("spacewarp " + line).Split(' ', StringSplitOptions.RemoveEmptyEntries);
                    Debug.Log($"[QuestSpaceWarp] spacewarp.txt {line}: {Command(words)}");
                }
            }
            catch (Exception e)
            {
                Debug.LogError($"[QuestSpaceWarp] spacewarp.txt not read (pushed by adb? chmod 666 it): {e.GetType().Name}: {e.Message}");
            }
        }

        /// <summary>files/command "spacewarp ..." (QuestDiagnostics); words[0] is "spacewarp"</summary>
        public static string Command(string[] words)
        {
            var self = instance;
            var verb = words.Length > 1 ? words[1].ToLowerInvariant() : "";
            switch (verb)
            {
                case "":
                case "status":
                    return self != null ? self.StatusLine(false) : $"not running: feature {SpaceWarpXr.Status}";
                case "on":
                case "off":
                    Wanted = verb == "on";
                    if (self != null)
                        self.Apply();
                    return $"wanted {Wanted}: {LastResult}";
                case "mode":
                    if (words.Length > 2 && Enum.TryParse(words[2], true, out Mode mode))
                    {
                        MotionMode = mode;
                        if (self != null)
                            self.havePrevious = false;
                    }
                    return $"mode {MotionMode}";
                case "rasterflip":
                    RasterFlip = -RasterFlip;
                    return $"raster y {(RasterFlip > 0 ? "as clip y" : "flipped")}";
                case "valueflip":
                    ValueFlip = -ValueFlip;
                    return $"value y {(ValueFlip > 0 ? "up (left-handed NDC)" : "down")}";
                case "when":
                    if (words.Length > 2 && Enum.TryParse(words[2], true, out DrawTime when))
                        When = when;
                    return $"drawn {When}";
                case "depth":
                    if (words.Length > 2)
                    {
                        if (float.TryParse(words[2], NumberStyles.Float, CultureInfo.InvariantCulture, out var clipZ))
                        {
                            DepthValue = Mathf.Clamp01(clipZ);
                            DepthNear = true;
                        }
                        else
                            DepthNear = words[2].ToLowerInvariant() == "near";
                    }
                    return DepthNear ? FormattableString.Invariant($"depth written: clip z {DepthValue:0.####}") : "depth far (cleared)";
                case "appspace":
                    if (words.Length > 2 && Enum.TryParse(words[2], true, out AppSpace appSpace))
                        AppSpaceMode = appSpace;
                    return $"app space {AppSpaceMode}";
                case "spin":
                    // A smooth stick turn without a person: degrees a second for some seconds (VRCameraAim.TurnAimNow)
                    if (self == null)
                        return "not running";
                    self.spinSpeed = words.Length > 2 && float.TryParse(words[2], NumberStyles.Float, CultureInfo.InvariantCulture, out var speed) ? speed : 90f;
                    self.spinUntil = Time.unscaledTime + (words.Length > 3 && float.TryParse(words[3], NumberStyles.Float, CultureInfo.InvariantCulture, out var spinSeconds) ? spinSeconds : 5f);
                    return FormattableString.Invariant($"spinning {self.spinSpeed:0.#} deg/s until +{self.spinUntil - Time.unscaledTime:0.#} s");
                case "probe":
                    if (self == null)
                        return "not running";
                    self.probeRequested = true;
                    return "reading the motion vector image back after the next draw";
                case "maxdeg":
                    if (words.Length > 2 && float.TryParse(words[2], NumberStyles.Float, CultureInfo.InvariantCulture, out var degrees))
                        MaxDegrees = degrees;
                    return $"max {MaxDegrees:0.#} degrees a frame";
                default:
                    return "spacewarp [status|on|off|mode rotation|far|zero|gradient|rasterflip|valueflip|maxdeg N|when postrender|cameraevent|endframe|depth near|far|<clip z>|appspace off|rig|turn|spin <deg/s> <s>|probe]";
            }
        }

        private void Update()
        {
            ReadSetting();
            if (Wanted != On)
                Apply();
            if (spinUntil > Time.unscaledTime)
                QuestDiagnostics.Aim("TurnAimNow", spinSpeed * Time.unscaledDeltaTime);
            if (Time.unscaledTime - statsSince >= StatusEverySeconds && (On || frames > 0))
                Debug.Log(StatusLine(true));
        }

        /// <summary>
        /// Follows RepoXR's Application SpaceWarp setting: each change of it sets Wanted (the "spacewarp on|off" commands
        /// still switch it in between, for tests)
        /// </summary>
        private void ReadSetting()
        {
            if (Time.unscaledTime < settingAt)
                return;
            settingAt = Time.unscaledTime + 1f;
            if (repoXRConfig == null)
            {
                foreach (var assembly in AppDomain.CurrentDomain.GetAssemblies())
                {
                    var plugin = assembly.GetType("RepoXR.Plugin");
                    if (plugin == null)
                        continue;
                    repoXRConfig = plugin.GetProperty("Config", BindingFlags.Public | BindingFlags.Static | BindingFlags.DeclaredOnly);
                    spaceWarpSetting = repoXRConfig?.PropertyType.GetProperty("SpaceWarp");
                    break;
                }
                if (repoXRConfig == null)
                {
                    settingAt = float.MaxValue; // no RepoXR: commands and files/spacewarp.txt only
                    return;
                }
            }
            var config = repoXRConfig.GetValue(null);
            var entry = config != null ? spaceWarpSetting?.GetValue(config) : null;
            if (!(entry?.GetType().GetProperty("Value")?.GetValue(entry) is bool value) || value == settingSeen)
                return;
            settingSeen = value;
            Wanted = value;
            Debug.Log($"[QuestSpaceWarp] setting Application SpaceWarp: {(value ? "on" : "off")}");
        }

        /// <summary>Switches the runtime's SpaceWarp to Wanted once XR runs single pass with a head camera</summary>
        private void Apply()
        {
            if (Wanted == On)
                return;
            if (Wanted)
            {
                if (!SpaceWarpXr.SessionRunning || Head() == null || !DisplayRunning(out var display))
                    return;  // not yet: tried again next frame
                if (!SpaceWarpXr.ExtensionEnabled)
                {
                    LastResult = "the runtime did not enable XR_FB_space_warp";
                    Wanted = false;
                    Debug.Log($"[QuestSpaceWarp] {LastResult}");
                    return;
                }
                passCount = display.GetRenderPassCount();
                if (passCount == 0)
                    return;  // no frame set up yet (loading): tried again next frame
                if (passCount != 1)
                {
                    // Multi-pass (RepoXR's SinglePassRendering off): two render passes, which URP refuses too
                    LastResult = $"{passCount} render passes (multi-pass): single pass only";
                    Wanted = false;
                    Debug.Log($"[QuestSpaceWarp] {LastResult}");
                    return;
                }
                // The plugin's app space pose starts all zeros, not even a unit quaternion: identity, once
                var position = SpaceWarpXr.SetAppSpacePosition(Vector3.zero);
                var rotation = SpaceWarpXr.SetAppSpaceRotation(Quaternion.identity);
                var result = SpaceWarpXr.SetSpaceWarp(true);
                On = result == 0;
                havePrevious = false;
                LastResult = $"on: XrResult {result} (app space {position}/{rotation})";
                if (!On)
                    Wanted = false;
            }
            else
            {
                var result = SpaceWarpXr.SetSpaceWarp(false);
                On = false;
                LastResult = $"off: XrResult {result}";
            }
            Debug.Log($"[QuestSpaceWarp] {LastResult}");
        }

        private bool DisplayRunning(out XRDisplaySubsystem display)
        {
            displays.Clear();
            SubsystemManager.GetSubsystems(displays);
            display = displays.FirstOrDefault(d => d.running);
            return display != null;
        }

        /// <summary>The eyes' world camera: the stereo camera drawing to the eyes first (R.E.P.O.'s Camera Main)</summary>
        private Camera Head()
        {
            if (head != null && head.isActiveAndEnabled && head.stereoTargetEye != StereoTargetEyeMask.None && head.targetTexture == null &&
                Time.unscaledTime - headCheckedAt < 1f)
                return head;
            headCheckedAt = Time.unscaledTime;
            head = null;
            foreach (var camera in Camera.allCameras)
                if (camera.stereoTargetEye == StereoTargetEyeMask.Both && camera.targetTexture == null &&
                    (head == null || camera.depth < head.depth))
                    head = camera;
            return head;
        }

        private void PreRender(Camera camera)
        {
            if (!On || capturedFrame == Time.frameCount || camera != Head())
                return;
            capturedFrame = Time.frameCount;
            hasPass = false;
            if (!DisplayRunning(out var display) || display.GetRenderPassCount() != 1)
                return;
            display.GetRenderPass(0, out var pass);
            hasPass = pass.hasMotionVectorPass;
            target = pass.motionVectorRenderTarget;
            targetDesc = pass.motionVectorRenderTargetDesc;
            for (var eye = 0; eye < 2; eye++)
            {
                views[eye] = camera.GetStereoViewMatrix((Camera.StereoscopicEye)eye);
                projections[eye] = camera.GetStereoProjectionMatrix((Camera.StereoscopicEye)eye);
            }
            if (!loggedTarget && hasPass)
            {
                loggedTarget = true;
                Debug.Log($"[QuestSpaceWarp] motion vector target {targetDesc.width}x{targetDesc.height}x{targetDesc.volumeDepth} " +
                          $"{targetDesc.graphicsFormat} depth {targetDesc.depthStencilFormat} msaa {targetDesc.msaaSamples} " +
                          $"dim {targetDesc.dimension}; eye camera {camera.name}, far {camera.farClipPlane:0.#} m, reversed z {SystemInfo.usesReversedZBuffer}");
            }

            if (When == DrawTime.CameraEvent && hasPass)
            {
                AttachCameraCommands(camera);
                Record(cameraCommands);
            }
            else
                AttachCameraCommands(null);

            var rig = camera.transform.parent;
            if (rig != null)
            {
                if (rig == rigLastTransform)
                {
                    var metres = Vector3.Distance(rig.position, rigLast);
                    rigFrames++;
                    rigMetresSum += metres;
                    rigMetresMax = Mathf.Max(rigMetresMax, metres);
                }
                rigLastTransform = rig;
                rigLast = rig.position;
            }
            SubmitAppSpace(rig);
        }

        /// <summary>The app space pose for this frame: the plugin hands the runtime its change since the last frame
        /// (XrCompositionLayerSpaceWarpInfoFB.appSpaceDeltaPose), with which the runtime moves what has no depth</summary>
        private void SubmitAppSpace(Transform rig)
        {
            if (AppSpaceMode == AppSpace.Off || rig == null)
            {
                if (!appSpaceIdentity)
                {
                    var p = SpaceWarpXr.SetAppSpacePosition(Vector3.zero);
                    var r = SpaceWarpXr.SetAppSpaceRotation(Quaternion.identity);
                    appSpaceResult = $"identity {p}/{r}";
                    appSpaceIdentity = true;
                }
                return;
            }
            var position = SpaceWarpXr.SetAppSpacePosition(AppSpaceMode == AppSpace.Rig ? rig.position : Vector3.zero);
            var rotation = SpaceWarpXr.SetAppSpaceRotation(rig.rotation);
            if (appSpaceIdentity || position != 0 || rotation != 0)
                appSpaceResult = $"{AppSpaceMode} {position}/{rotation}";
            appSpaceIdentity = false;
        }

        private void PostRender(Camera camera)
        {
            if (When != DrawTime.PostRender || !On || camera != head || capturedFrame != Time.frameCount)
                return;
            Guarded(Draw);
        }

        private IEnumerator EndOfFrames()
        {
            var wait = new WaitForEndOfFrame();
            while (true)
            {
                yield return wait;
                if (When == DrawTime.EndFrame && On)
                    Guarded(Draw);
                else if (On && capturedFrame == Time.frameCount && !hasPass)
                    noPass++;
                if (On)
                    frames++;
            }
        }

        private void Guarded(Action action)
        {
            try
            {
                action();
            }
            catch (Exception e)
            {
                Debug.LogError($"[QuestSpaceWarp] draw failed, SpaceWarp off: {e}");
                Wanted = false;
                Apply();
            }
        }

        private void AttachCameraCommands(Camera camera)
        {
            if (cameraCommandsOn == camera)
                return;
            if (cameraCommandsOn != null && cameraCommands != null)
                cameraCommandsOn.RemoveCommandBuffer(CameraEvent.AfterEverything, cameraCommands);
            cameraCommandsOn = camera;
            if (camera == null)
                return;
            cameraCommands ??= new CommandBuffer { name = "RepoQuest SpaceWarp motion vectors (camera)" };
            cameraCommands.Clear();
            camera.AddCommandBuffer(CameraEvent.AfterEverything, cameraCommands);
        }

        private int drawnFrame = -1;

        /// <summary>Records this frame's vectors into the buffer and runs it now</summary>
        private void Draw()
        {
            if (!On || capturedFrame != Time.frameCount || !hasPass || drawnFrame == Time.frameCount)
                return;
            Record(commands);
            Graphics.ExecuteCommandBuffer(commands);
        }

        /// <summary>This frame's vectors (and the probe's copy) into a cleared command buffer, once a frame</summary>
        private void Record(CommandBuffer buffer)
        {
            buffer.Clear();
            if (drawnFrame == Time.frameCount)
                return;
            drawnFrame = Time.frameCount;
            Reprojections();

            var slices = Mathf.Clamp(targetDesc.volumeDepth, 1, 2);
            var farZ = SystemInfo.usesReversedZBuffer ? 0f : 1f;
            material.SetFloat(ZWriteId, DepthNear ? 1f : 0f);
            // A mid depth in clip z: half way through the depth range (some metres away), for the "depth near" test
            material.SetFloat(DepthId, DepthValue);
            for (var slice = 0; slice < slices; slice++)
            {
                buffer.SetRenderTarget(target, target, 0, CubemapFace.Unknown, slice);
                // Depth to far: no parallax for the runtime, as with PEAK's passes off
                buffer.ClearRenderTarget(true, true, Color.clear, 1f);
                if (MotionMode == Mode.Zero && !DepthNear)
                    continue;
                buffer.SetGlobalMatrix(ReprojectId, reproject[slice]);
                buffer.SetGlobalVector(ParamsId, new Vector4(RasterFlip, ValueFlip, farZ, MotionMode == Mode.Zero ? 4f : (float)MotionMode));
                buffer.DrawProcedural(Matrix4x4.identity, material, 0, MeshTopology.Triangles, 3, 1);
            }
            drawn++;

            if (probeRequested)
            {
                probeRequested = false;
                if (probeTexture == null || probeTexture.width != targetDesc.width || probeTexture.height != targetDesc.height)
                {
                    if (probeTexture != null)
                        probeTexture.Release();
                    probeTexture = new RenderTexture(targetDesc.width, targetDesc.height, 0, RenderTextureFormat.ARGBHalf) { name = "RepoQuest SpaceWarp probe" };
                    probeTexture.Create();
                }
                buffer.CopyTexture(target, 0, 0, probeTexture, 0, 0);
                var mode = MotionMode;
                var when = When;
                buffer.RequestAsyncReadback(probeTexture, 0, TextureFormat.RGBAHalf, request => LogProbe(request, mode, when));
            }
        }

        private static void LogProbe(AsyncGPUReadbackRequest request, Mode mode, DrawTime when)
        {
            if (request.hasError)
            {
                Debug.Log("[QuestSpaceWarp] probe: readback failed");
                return;
            }
            var data = request.GetData<ushort>();
            int w = request.width, h = request.height;
            string At(string name, int x, int y)
            {
                var i = (y * w + x) * 4;
                return FormattableString.Invariant(
                    $"{name} ({x},{y}) {Mathf.HalfToFloat(data[i]):0.0000} {Mathf.HalfToFloat(data[i + 1]):0.0000} {Mathf.HalfToFloat(data[i + 2]):0.0000} {Mathf.HalfToFloat(data[i + 3]):0.0000}");
            }
            Debug.Log($"[QuestSpaceWarp] probe (left eye, mode {mode}, {when}; texture row 0 first): " +
                      $"{At("row0", w / 2, 0)}; {At("centre", w / 2, h / 2)}; {At("last row", w / 2, h - 1)}; {At("left", 0, h / 2)}; {At("right", w - 1, h / 2)}");
        }
        /// <summary>
        /// Per eye: last frame's view-projection times the inverse of this frame's, both as the GPU takes them for the
        /// screen (GL.GetGPUProjectionMatrix(.., false): y up, as URP's XRDepthMotionPass has them). A pixel's NDC through
        /// it gives where that point was a frame ago; the difference is its motion
        /// </summary>
        private void Reprojections()
        {
            var cut = false;
            if (havePrevious)
            {
                // Rows 1 and 2 of a view matrix: the camera's up and its backward axis in world space
                var degrees = Mathf.Max(Vector3.Angle(previousView.GetRow(2), views[0].GetRow(2)),
                    Vector3.Angle(previousView.GetRow(1), views[0].GetRow(1)));
                degreesSum += degrees;
                degreesMax = Mathf.Max(degreesMax, degrees);
                cut = degrees > MaxDegrees;
                if (cut)
                    cuts++;
            }
            for (var eye = 0; eye < 2; eye++)
            {
                var view = views[eye];
                if (MotionMode != Mode.Far)
                {
                    // Translation out: the camera turns about its own eye point
                    view.m03 = 0f;
                    view.m13 = 0f;
                    view.m23 = 0f;
                }
                current[eye] = GL.GetGPUProjectionMatrix(projections[eye], false) * view;
                reproject[eye] = havePrevious && !cut ? previous[eye] * current[eye].inverse : Matrix4x4.identity;
                previous[eye] = current[eye];
            }
            previousView = views[0];
            havePrevious = true;
        }

        private string StatusLine(bool reset)
        {
            var seconds = Mathf.Max(0.001f, Time.unscaledTime - statsSince);
            var line =
                FormattableString.Invariant($"[QuestSpaceWarp] {(On ? "on" : "off")} (wanted {Wanted}, {LastResult}), mode {MotionMode}, raster y {RasterFlip:+0;-0}, value y {ValueFlip:+0;-0}; ") +
                FormattableString.Invariant($"{frames / seconds:0.0} frames/s, drawn {drawn}, no target {noPass}, cuts {cuts}; turn {(drawn > 0 ? degreesSum / drawn : 0f):0.00} deg/frame (max {degreesMax:0.0}); ") +
                FormattableString.Invariant($"rig {(rigFrames > 0 ? rigMetresSum / rigFrames * 1000f : 0f):0.0} mm/frame (max {rigMetresMax * 1000f:0}); camera {(head != null ? head.name : "none")}; feature {SpaceWarpXr.Status}; ") +
                FormattableString.Invariant($"app space {AppSpaceMode} {appSpaceResult}; depth {(DepthNear ? DepthValue.ToString("0.####", CultureInfo.InvariantCulture) : "far")}");
            if (reset)
            {
                statsSince = Time.unscaledTime;
                frames = drawn = noPass = cuts = rigFrames = 0;
                degreesSum = degreesMax = rigMetresSum = rigMetresMax = 0f;
            }
            return line;
        }
    }
}
