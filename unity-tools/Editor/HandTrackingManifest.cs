using System.IO;
using System.Linq;
using System.Xml;
using UnityEditor.Android;
using UnityEngine;

namespace RepoQuest.Editor
{
    /// <summary>
    /// An app that only declares controllers is not started while they sleep: Horizon OS shows "controllers required"
    /// instead (LaunchCheckControllerRequiredDialogActivity), which also stops adb from starting the game for a test.
    /// Hand tracking declared as optional (oculus.software.handtracking, required false) lets it start; the game still
    /// plays with controllers only, as it never asks the runtime for hands.
    /// </summary>
    public class HandTrackingManifest : IPostGenerateGradleAndroidProject
    {
        private const string AndroidNs = "http://schemas.android.com/apk/res/android";
        private const string Feature = "oculus.software.handtracking";
        private const string Permission = "com.oculus.permission.HAND_TRACKING";

        public int callbackOrder => 0;

        public void OnPostGenerateGradleAndroidProject(string path)
        {
            // path is the unityLibrary module, whose manifest is merged into the app's
            var manifest = Path.Combine(path, "src/main/AndroidManifest.xml");
            var doc = new XmlDocument();
            doc.Load(manifest);
            if (doc.SelectSingleNode("/manifest") is not XmlElement root)
                return;

            foreach (var existing in root.ChildNodes.OfType<XmlElement>().Where(e =>
                         (e.Name == "uses-feature" && e.GetAttribute("name", AndroidNs) == Feature) ||
                         (e.Name == "uses-permission" && e.GetAttribute("name", AndroidNs) == Permission)).ToList())
                root.RemoveChild(existing);

            var feature = doc.CreateElement("uses-feature");
            feature.SetAttribute("name", AndroidNs, Feature);
            feature.SetAttribute("required", AndroidNs, "false");
            root.AppendChild(feature);
            var permission = doc.CreateElement("uses-permission");
            permission.SetAttribute("name", AndroidNs, Permission);
            root.AppendChild(permission);
            doc.Save(manifest);
            Debug.Log($"[QuestBuild] {Feature} (optional): {Path.GetFullPath(manifest)}");
        }
    }
}
