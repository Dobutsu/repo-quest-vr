using System;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Reflection;
using BepInEx;
using BepInEx.Bootstrap;
using BepInEx.Logging;
using UnityEngine;
using Object = UnityEngine.Object;

namespace RepoXR.Standalone
{
    /// <summary>
    /// Does the two jobs BepInEx did for RepoXR once its patches are baked into the game assemblies:
    /// resolving the mod's dependencies from its folder, and creating the plugin component.
    /// Layout: RepoXR.dll and its dependencies sit in &lt;Game&gt;_Data/Managed (so the player's native serializer knows
    /// their types), next to repoxrassets, Addressables/ and an empty RuntimeDeps/.
    /// </summary>
    public static class Bootstrap
    {
        private const string PluginTypeName = "RepoXR.Plugin, RepoXR";

        private static string modDirectory;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void InstallResolver()
        {
            // Baked game code references RepoXR directly, so this must be in place before any scene loads
            modDirectory = Path.Combine(Application.dataPath, "Managed");
            AppDomain.CurrentDomain.AssemblyResolve += ResolveFromModDirectory;
        }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
        private static void StartPlugin()
        {
            if (Application.platform == RuntimePlatform.Android)
                InstallSteam();

            try
            {
                InitializeBepInExPaths();
                BepInEx.Logging.Logger.Listeners.Add(new UnityLogListener());

                var pluginType = Type.GetType(PluginTypeName, true);
                if (Application.platform == RuntimePlatform.Android)
                    RegisterPluginLocation(pluginType, ExtractModFiles());

                var host = new GameObject("RepoXR (standalone)");
                Object.DontDestroyOnLoad(host);
                host.AddComponent(pluginType);

                Debug.Log("[RepoXR.Standalone] plugin started");
            }
            catch (Exception e)
            {
                Debug.LogError($"[RepoXR.Standalone] failed to start RepoXR: {e}");
            }
        }

        // Separate method: its body names shim-only types, which must not even be compiled on PC (real Facepunch)
        private static void InstallSteam()
        {
            try
            {
                SteamSetup.Install();
            }
            catch (Exception e)
            {
                Debug.LogError($"[RepoXR.Standalone] Steam setup failed, staying offline: {e}");
            }
        }

        /// <summary>BaseUnityPlugin reads Paths.ConfigPath; normally the preloader sets these</summary>
        private static void InitializeBepInExPaths()
        {
            var gameRoot = Path.GetDirectoryName(Application.dataPath);
            var executable = Path.Combine(gameRoot, Path.GetFileName(Application.dataPath).Replace("_Data", ".exe"));
            var bepinRoot = Path.Combine(Application.persistentDataPath, "RepoXR");

            typeof(Paths).GetMethod("SetExecutablePath", BindingFlags.NonPublic | BindingFlags.Static)
                .Invoke(null, new object[] { executable, bepinRoot, Path.Combine(Application.dataPath, "Managed"), null });

            Directory.CreateDirectory(Paths.ConfigPath);
        }

        /// <summary>
        /// RepoXR keeps its asset bundle, Addressables and RuntimeDeps next to its DLL. On Android they ship in the
        /// APK under assets/RepoXR, which is not a directory the file APIs can read, so they are copied out to
        /// persistent storage once per installed APK. Returns the path RepoXR should believe it was loaded from.
        /// </summary>
        private static string ExtractModFiles()
        {
            const string prefix = "assets/RepoXR/";
            var target = Path.Combine(Application.persistentDataPath, "RepoXR");
            var apk = new FileInfo(Application.dataPath);
            var stampFile = Path.Combine(target, ".apk-stamp");
            var stamp = $"{apk.Length}:{apk.LastWriteTimeUtc.Ticks}";

            if (!File.Exists(stampFile) || File.ReadAllText(stampFile) != stamp)
            {
                // Replace what the previous APK extracted, but keep config/: BepInEx keeps RepoXR's settings there,
                // and wiping it reset the player's settings with every new build
                if (Directory.Exists(target))
                {
                    foreach (var dir in Directory.GetDirectories(target).Where(d => Path.GetFileName(d) != "config"))
                        Directory.Delete(dir, true);
                    foreach (var file in Directory.GetFiles(target))
                        File.Delete(file);
                }

                using (var zip = ZipFile.OpenRead(apk.FullName))
                {
                    foreach (var entry in zip.Entries.Where(e => e.FullName.StartsWith(prefix) && !e.FullName.EndsWith("/")))
                    {
                        var destination = Path.Combine(target, entry.FullName.Substring(prefix.Length));
                        Directory.CreateDirectory(Path.GetDirectoryName(destination));
                        entry.ExtractToFile(destination, true);
                    }
                }

                File.WriteAllText(stampFile, stamp);
                Debug.Log($"[RepoXR.Standalone] extracted mod files to {target}");
            }

            // RepoXR loads every DLL in here; under IL2CPP they are compiled into the player, so it stays empty
            Directory.CreateDirectory(Path.Combine(target, "RuntimeDeps"));
            return Path.Combine(target, "RepoXR.dll");
        }

        /// <summary>
        /// BaseUnityPlugin takes Info from the chainloader's table when the plugin is listed there, and otherwise
        /// uses Assembly.Location, which IL2CPP leaves empty
        /// </summary>
        private static void RegisterPluginLocation(Type pluginType, string location)
        {
            var info = new PluginInfo();
            SetInternal(info, nameof(PluginInfo.Metadata), MetadataHelper.GetMetadata(pluginType));
            SetInternal(info, nameof(PluginInfo.Processes), MetadataHelper.GetAttributes<BepInProcess>(pluginType));
            SetInternal(info, nameof(PluginInfo.Dependencies), MetadataHelper.GetDependencies(pluginType));
            SetInternal(info, nameof(PluginInfo.Location), location);
            Chainloader.PluginInfos[info.Metadata.GUID] = info;
        }

        private static void SetInternal(object target, string property, object value) =>
            target.GetType().GetProperty(property).GetSetMethod(true).Invoke(target, new[] { value });

        private static Assembly ResolveFromModDirectory(object sender, ResolveEventArgs args)
        {
            var wanted = new AssemblyName(args.Name).Name;

            // RepoXR itself loads RuntimeDeps with Assembly.LoadFile; reuse those instead of loading a second copy
            var loaded = AppDomain.CurrentDomain.GetAssemblies().FirstOrDefault(a => a.GetName().Name == wanted);
            if (loaded != null)
                return loaded;

            if (modDirectory == null || !Directory.Exists(modDirectory))
                return null;

            foreach (var file in Directory.GetFiles(modDirectory, "*.dll", SearchOption.AllDirectories))
            {
                try
                {
                    if (AssemblyName.GetAssemblyName(file).Name == wanted)
                        return Assembly.LoadFrom(file);
                }
                catch (BadImageFormatException)
                {
                    // native library (UnityOpenXR, openxr_loader)
                }
            }

            return null;
        }
    }
}
