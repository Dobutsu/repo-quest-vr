using System.IO;
using System.Linq;
using System.Xml;
using UnityEditor.Android;
using UnityEngine;

namespace RepoQuest.Editor
{
    /// <summary>
    /// Unity asks for every dangerous permission in the manifest (the microphone, for voice chat) as the game starts,
    /// while the OpenXR session is still coming up. On Pico 4 the system's permission window takes the game's focus
    /// then, and Unity's OpenXR plugin crashes (SIGSEGV in libUnityOpenXR) right after the game gets it back.
    /// unityplayer.SkipPermissionsDialog stops that; RepoQuest.MicPermission asks instead (on Pico, in the main menu).
    /// </summary>
    public class SkipPermissionsManifest : IPostGenerateGradleAndroidProject
    {
        private const string AndroidNs = "http://schemas.android.com/apk/res/android";
        private const string Name = "unityplayer.SkipPermissionsDialog";

        public int callbackOrder => 0;

        public void OnPostGenerateGradleAndroidProject(string path)
        {
            // path is the unityLibrary module, where Unity puts its own unityplayer.* meta-data
            var manifest = Path.Combine(path, "src/main/AndroidManifest.xml");
            var doc = new XmlDocument();
            doc.Load(manifest);
            var application = doc.SelectSingleNode("/manifest/application") as XmlElement;
            if (application == null)
                return;
            foreach (var existing in application.SelectNodes("meta-data").Cast<XmlElement>().ToList())
            {
                if (existing.GetAttribute("name", AndroidNs) == Name)
                    application.RemoveChild(existing);
            }

            var metaData = doc.CreateElement("meta-data");
            metaData.SetAttribute("name", AndroidNs, Name);
            metaData.SetAttribute("value", AndroidNs, "true");
            application.AppendChild(metaData);
            doc.Save(manifest);
            Debug.Log($"[QuestBuild] {Name}: {Path.GetFullPath(manifest)}");
        }
    }
}
