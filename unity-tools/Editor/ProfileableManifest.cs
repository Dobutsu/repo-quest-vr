using System.IO;
using System.Xml;
using UnityEditor.Android;
using UnityEngine;

namespace RepoQuest.Editor
{
    /// <summary>
    /// For QuestBuild.BuildApk -profiling: marks the app &lt;profileable android:shell="true"/&gt; (Android 10+), which
    /// lets simpleperf and Perfetto sample a release build over adb. Only debuggable (development) builds are profileable
    /// otherwise, and those run slower than what players get.
    /// </summary>
    public class ProfileableManifest : IPostGenerateGradleAndroidProject
    {
        private const string AndroidNs = "http://schemas.android.com/apk/res/android";

        public int callbackOrder => 0;

        public void OnPostGenerateGradleAndroidProject(string path)
        {
            if (!QuestBuild.Profiling)
                return;

            // path is the unityLibrary module; the launcher module's manifest is the application's
            foreach (var manifest in new[]
                     {
                         Path.Combine(path, "src/main/AndroidManifest.xml"),
                         Path.Combine(path, "../launcher/src/main/AndroidManifest.xml"),
                     })
            {
                if (!File.Exists(manifest))
                    continue;

                var doc = new XmlDocument();
                doc.Load(manifest);
                var application = doc.SelectSingleNode("/manifest/application") as XmlElement;
                if (application == null || application.SelectSingleNode("profileable") != null)
                    continue;

                var profileable = doc.CreateElement("profileable");
                profileable.SetAttribute("shell", AndroidNs, "true");
                application.AppendChild(profileable);
                doc.Save(manifest);
                Debug.Log($"[QuestBuild] profileable: {Path.GetFullPath(manifest)}");
            }
        }
    }
}
