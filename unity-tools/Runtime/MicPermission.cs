using System;
using System.IO;
using System.Linq;
using System.Reflection;
using UnityEngine;
using UnityEngine.Android;

namespace RepoQuest
{
    /// <summary>
    /// Asks for the microphone (voice chat) once per install, in place of Unity's own request at startup (switched off
    /// by Editor/SkipPermissionsManifest.cs). On Pico it asks in the main menu after the headset has shown the game
    /// steadily for SteadySeconds: Unity's request, made while the OpenXR session was starting, crashed the game on
    /// Pico 4 when it got its focus back. Elsewhere (Quest) it asks right away, as Unity did. The voice chat looks the
    /// microphone up every frame, so it picks up a late grant by itself. Once asked (files/mic-permission-asked), never
    /// again: the player can still allow it in the headset's settings. Log lines start with [MicPermission].
    /// </summary>
    public class MicPermission : MonoBehaviour
    {
        private const float SteadySeconds = 5f;

        private string askedPath;
        private bool inMenu;
        private float steady;
        private FieldInfo runInstance, levelCurrent, levelMainMenu;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        private static void Install()
        {
            if (Application.platform != RuntimePlatform.Android || Permission.HasUserAuthorizedPermission(Permission.Microphone))
                return;
            if (File.Exists(Path.Combine(Application.persistentDataPath, "mic-permission-asked")))
                return;
            var go = new GameObject("RepoQuest Mic Permission");
            DontDestroyOnLoad(go);
            go.hideFlags = HideFlags.HideAndDontSave;
            go.AddComponent<MicPermission>();
        }

        private void Awake()
        {
            askedPath = Path.Combine(Application.persistentDataPath, "mic-permission-asked");
            inMenu = IsPico();
            var runManager = AppDomain.CurrentDomain.GetAssemblies()
                .Select(a => a.GetType("RunManager")).FirstOrDefault(t => t != null);
            if (runManager != null)
            {
                runInstance = runManager.GetField("instance", BindingFlags.Public | BindingFlags.Static);
                levelCurrent = runManager.GetField("levelCurrent");
                levelMainMenu = runManager.GetField("levelMainMenu");
            }
        }

        private static bool IsPico()
        {
            try
            {
                using var build = new AndroidJavaClass("android.os.Build");
                return build.GetStatic<string>("BRAND").ToLowerInvariant().Contains("pico");
            }
            catch (Exception)
            {
                return false;
            }
        }

        private bool InMainMenu()
        {
            var run = runInstance?.GetValue(null);
            if (run == null || levelCurrent == null || levelMainMenu == null)
                return false;
            var current = levelCurrent.GetValue(run);
            return current != null && Equals(current, levelMainMenu.GetValue(run));
        }

        private void Update()
        {
            if (inMenu)
            {
                if (Application.isFocused && InMainMenu() && UnityEngine.XR.XRSettings.isDeviceActive)
                    steady += Time.unscaledDeltaTime;
                else
                    steady = 0;
                if (steady < SteadySeconds)
                    return;
            }

            try
            {
                File.WriteAllText(askedPath, DateTime.Now.ToString("u"));
            }
            catch (IOException) { }
            var callbacks = new PermissionCallbacks();
            callbacks.PermissionGranted += _ => Debug.Log("[MicPermission] granted");
            callbacks.PermissionDenied += _ => Debug.Log("[MicPermission] denied");
            callbacks.PermissionDeniedAndDontAskAgain += _ => Debug.Log("[MicPermission] denied, don't ask again");
            Debug.Log("[MicPermission] asking for the microphone");
            Permission.RequestUserPermission(Permission.Microphone, callbacks);
            Destroy(gameObject);
        }
    }
}
