using System.Collections;
using System.IO;
using System.Linq;
using BepInEx;
using BepInEx.Logging;
using UnityEngine;

namespace RepoQuest
{
    /// <summary>
    /// Waits for &lt;game&gt;/shader-gallery/request.txt (Resources paths, one per line; a "#quit" line closes the game
    /// afterwards), then renders those prefabs into &lt;game&gt;/shader-gallery/out/. Installed only in the CrossOver test
    /// copy by scripts/shader_gallery.py pc; idle otherwise.
    /// </summary>
    [BepInPlugin("dobutsu.repoquest.shadergallery", "RepoQuest Shader Gallery", "1.0.0")]
    public class ShaderGalleryPlugin : BaseUnityPlugin
    {
        private void Awake()
        {
            // BepInEx's manager object (and this plugin with it) is destroyed when the game loads its first scenes
            // (HideManagerGameObject = false by default), so the waiting runs on an object of our own
            var go = new GameObject("RepoQuest Shader Gallery Runner") { hideFlags = HideFlags.HideAndDontSave };
            DontDestroyOnLoad(go);
            go.AddComponent<ShaderGalleryRunner>().Log = Logger;
        }
    }

    public class ShaderGalleryRunner : MonoBehaviour
    {
        // The game loads its first scenes in this time; Resources loads during that would compete with it
        private const float StartAfterSeconds = 20f;

        public ManualLogSource Log;

        private IEnumerator Start()
        {
            var dir = Path.Combine(Paths.GameRootPath, "shader-gallery");
            var request = Path.Combine(dir, "request.txt");
            var wait = new WaitForSecondsRealtime(1f);
            while (Time.realtimeSinceStartup < StartAfterSeconds || !File.Exists(request))
                yield return wait;

            var lines = File.ReadAllLines(request).Select(l => l.Trim()).Where(l => l.Length > 0).ToList();
            File.Delete(request);
            var paths = lines.Where(l => !l.StartsWith("#")).ToList();
            var output = Path.Combine(dir, "out");
            if (Directory.Exists(output))
                Directory.Delete(output, true);
            Log.LogInfo($"rendering {paths.Count} prefabs into {output}");
            yield return ShaderGallery.Run(paths, output, message => Log.LogInfo(message));
            if (lines.Contains("#quit"))
                Application.Quit();
        }
    }
}
