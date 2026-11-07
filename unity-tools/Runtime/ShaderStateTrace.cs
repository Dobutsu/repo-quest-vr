using System;
using System.Collections;
using System.IO;
using UnityEngine;
using UnityEngine.Experimental.Rendering;
using UnityEngine.Networking;
using UnityEngine.SceneManagement;

namespace RepoQuest
{
    /// <summary>
    /// Vulkan pipelines for the game's shaders are made the first time something is drawn with them, which stalls that
    /// frame (most on a first launch after install, when the driver's pipeline cache is empty). Unity 6's
    /// GraphicsStateCollection records the states actually used and creates their pipelines ahead of time.
    ///
    /// Warm-up (the default; files/gsc-warmup-off turns it off): the collection recorded on a Quest 3 and shipped in the
    /// APK (unity-tools/StreamingAssets/RepoQuest/shaders.graphicsstate), or files/gsc/warm.graphicsstate when it exists.
    /// After each scene load its pipelines are created on job threads (WarmUp), for the shaders loaded by then: about
    /// 600 in 0.5 s at the start, inside the first frame's stall, instead of ~400 one by one while playing (b25 A/B).
    /// Trace (files/gsc-trace exists; development builds only, scripts/codebuild.sh DEV=1): every state the game uses is
    /// recorded into files/gsc/repo.graphicsstate, added to what earlier launches recorded (push the shipped file there to
    /// extend it); saved on every scene load, once a minute and when the app pauses.
    /// The shipped collection names shaders by asset GUID: it stays right while the game's shaders keep their GUIDs
    /// (restore_guids.py), and a shader whose GUID changed is only not warmed. Log lines start with [ShaderStateTrace].
    /// </summary>
    public sealed class ShaderStateTrace : MonoBehaviour
    {
        private const float SaveSeconds = 60f;
        private const string Shipped = "RepoQuest/shaders.graphicsstate";

        private GraphicsStateCollection collection;
        private string tracePath;
        private bool tracing;
        private float saveAt;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        private static void Install()
        {
            if (Application.platform != RuntimePlatform.Android)
                return;
            var go = new GameObject("RepoQuest Shader State Trace");
            DontDestroyOnLoad(go);
            go.hideFlags = HideFlags.HideAndDontSave;
            go.AddComponent<ShaderStateTrace>();
        }

        private void Awake()
        {
            try
            {
                Debug.Log($"[ShaderStateTrace] parallel PSO creation {SystemInfo.supportsParallelPSOCreation}, " +
                          $"rendering threading {SystemInfo.renderingThreadingMode}, {SystemInfo.graphicsDeviceType}");
                var files = Application.persistentDataPath;
                var folder = Path.Combine(files, "gsc");
                Directory.CreateDirectory(folder);
                if (File.Exists(Path.Combine(files, "gsc-trace")))
                {
                    tracePath = Path.Combine(folder, "repo.graphicsstate");
                    collection = new GraphicsStateCollection();
                    if (File.Exists(tracePath) && !collection.LoadFromFile(tracePath))
                        Debug.LogWarning("[ShaderStateTrace] could not load the earlier trace; starting a new one");
                    tracing = collection.BeginTrace();
                    Debug.Log($"[ShaderStateTrace] trace {(tracing ? "on" : "FAILED to start")}: {Counts()} so far, {tracePath}");
                    saveAt = Time.realtimeSinceStartup + SaveSeconds;
                    SceneManager.sceneLoaded += (_, _) => StartCoroutine(SaveSoon());
                    return;
                }

                if (File.Exists(Path.Combine(files, "gsc-warmup-off")))
                {
                    Debug.Log("[ShaderStateTrace] off (files/gsc-warmup-off)");
                    enabled = false;
                    return;
                }
                var warmPath = Path.Combine(folder, "warm.graphicsstate");
                StartCoroutine(StartWarmUp(File.Exists(warmPath) ? warmPath : null));
            }
            catch (Exception e)
            {
                Debug.LogError($"[ShaderStateTrace] off: {e}");
                enabled = false;
            }
        }

        /// <param name="path">the collection to load; null: the one shipped in the APK</param>
        private IEnumerator StartWarmUp(string path)
        {
            if (path == null)
            {
                // StreamingAssets stay inside the APK on Android and LoadFromFile wants a file: copied out once per build
                path = Path.Combine(Application.temporaryCachePath, $"shaders-{Application.buildGUID}.graphicsstate");
                if (!File.Exists(path))
                {
                    using var request = UnityWebRequest.Get(Application.streamingAssetsPath + "/" + Shipped);
                    yield return request.SendWebRequest();
                    if (request.result != UnityWebRequest.Result.Success)
                    {
                        Debug.Log($"[ShaderStateTrace] off: no {Shipped} in the APK ({request.error})");
                        enabled = false;
                        yield break;
                    }
                    if (!CopyOut(request.downloadHandler.data, path))
                    {
                        enabled = false;
                        yield break;
                    }
                }
            }
            try
            {
                collection = new GraphicsStateCollection();
                if (!collection.LoadFromFile(path))
                {
                    Debug.LogWarning($"[ShaderStateTrace] could not load {path}; no warm-up");
                    enabled = false;
                    yield break;
                }
                Debug.Log($"[ShaderStateTrace] warm-up from {path}: {Counts()} ({collection.graphicsDeviceType}, " +
                          $"{collection.runtimePlatform}, version {collection.version})");
                SceneManager.sceneLoaded += (scene, _) => StartCoroutine(WarmUp(scene.name));
            }
            catch (Exception e)
            {
                Debug.LogError($"[ShaderStateTrace] off: {e}");
                enabled = false;
                yield break;
            }
            yield return WarmUp(SceneManager.GetActiveScene().name);
        }

        /// <summary>Writes the shipped collection to <paramref name="path"/>, removing earlier builds' copies</summary>
        private static bool CopyOut(byte[] data, string path)
        {
            try
            {
                var folder = Path.GetDirectoryName(path)!;
                foreach (var old in Directory.GetFiles(folder, "shaders-*.graphicsstate"))
                    File.Delete(old);
                File.WriteAllBytes(path + ".tmp", data);
                File.Move(path + ".tmp", path);
                return true;
            }
            catch (Exception e)
            {
                Debug.LogError($"[ShaderStateTrace] off: could not write {path}: {e.Message}");
                return false;
            }
        }

        private string Counts() => $"{collection.variantCount} variants, {collection.totalGraphicsStateCount} states";

        private IEnumerator WarmUp(string scene)
        {
            yield return null; // the scene's objects (and the shaders they bring) are loaded by the next frame
            var started = Time.realtimeSinceStartup;
            var before = collection.completedWarmupCount;
            var handle = collection.WarmUp();
            while (!handle.IsCompleted)
                yield return null;
            handle.Complete();
            Debug.Log($"[ShaderStateTrace] warm-up for {scene}: {collection.completedWarmupCount - before} pipelines in " +
                      $"{(Time.realtimeSinceStartup - started) * 1000f:0} ms (job threads), total {collection.completedWarmupCount}" +
                      $"/{collection.totalGraphicsStateCount}");
        }

        private IEnumerator SaveSoon()
        {
            // States are recorded as things get drawn: save once the new place has rendered a while
            yield return new WaitForSecondsRealtime(10f);
            Save("scene");
        }

        private void Update()
        {
            if (tracing && Time.realtimeSinceStartup >= saveAt)
                Save("minute");
        }

        private void OnApplicationPause(bool paused)
        {
            if (paused && tracing)
                Save("pause");
        }

        private void Save(string why)
        {
            saveAt = Time.realtimeSinceStartup + SaveSeconds;
            try
            {
                collection.EndTrace();
                var saved = collection.SaveToFile(tracePath);
                Debug.Log($"[ShaderStateTrace] saved ({why}): {(saved ? "ok" : "FAILED")}, {Counts()}");
            }
            catch (Exception e)
            {
                Debug.LogError($"[ShaderStateTrace] save failed: {e}");
            }
            finally
            {
                tracing = collection.BeginTrace();
            }
        }
    }
}
