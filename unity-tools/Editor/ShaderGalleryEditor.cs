using System;
using System.Collections;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace RepoQuest.Editor
{
    /// <summary>
    /// The shader gallery in the editor, with the project's current (generated) shaders and no APK build:
    ///   Unity -batchmode -projectPath &lt;project&gt; -buildTarget Android -executeMethod RepoQuest.Editor.ShaderGalleryEditor.Run
    ///         -galleryList &lt;file with Resources paths&gt; -galleryOut &lt;folder&gt;
    /// (scripts/shader_gallery.py editor). Renders with the editor's graphics API (Metal on the Mac), not the Quest's
    /// Vulkan: right for the shaders' formulas, not for driver-specific problems.
    /// </summary>
    public static class ShaderGalleryEditor
    {
        public static void Run()
        {
            var code = 0;
            try
            {
                var list = Argument("-galleryList");
                var outDir = Argument("-galleryOut");
                var paths = File.ReadAllLines(list).Select(l => l.Trim()).Where(l => l.Length > 0 && !l.StartsWith("#")).ToList();
                EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
                Drain(ShaderGallery.Run(paths, outDir, message => Debug.Log(message)));
            }
            catch (Exception e)
            {
                Debug.LogException(e);
                code = 1;
            }
            EditorApplication.Exit(code);
        }

        /// <summary>Runs a coroutine to the end in one go; AsyncOperations (unloading assets) need no waiting here</summary>
        private static void Drain(IEnumerator routine)
        {
            while (routine.MoveNext())
                if (routine.Current is IEnumerator nested)
                    Drain(nested);
        }

        private static string Argument(string name)
        {
            var args = Environment.GetCommandLineArgs();
            var i = Array.IndexOf(args, name);
            if (i < 0 || i + 1 >= args.Length)
                throw new ArgumentException($"missing {name} <value>");
            return args[i + 1];
        }
    }
}
