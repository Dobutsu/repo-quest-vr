using UnityEditor;
using UnityEngine;

namespace RepoQuest.Editor
{
    /// <summary>
    /// Checks whether prefab import artifacts lost their MonoBehaviour references (Map - Valuable's MapValuable came out
    /// of the Quest build with every reference null although its YAML sets them).
    ///   -executeMethod RepoQuest.Editor.ImportDiagnostics.CheckMapValuable
    /// </summary>
    public static class ImportDiagnostics
    {
        private const string PrefabPath = "Assets/GameObject/Map - Valuable.prefab";

        public static void CheckMapValuable()
        {
            Report("as imported");
            AssetDatabase.ImportAsset(PrefabPath, ImportAssetOptions.ForceUpdate);
            Report("after forced reimport");
            EditorApplication.Exit(0);
        }

        private static void Report(string when)
        {
            var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(PrefabPath);
            var component = prefab ? prefab.GetComponent("MapValuable") : null;
            if (component == null)
            {
                Debug.Log($"[ImportDiagnostics] {when}: MapValuable not found on {PrefabPath}");
                return;
            }

            var serialized = new SerializedObject(component);
            foreach (var name in new[] { "spriteRenderer", "spriteSmall", "spriteBig" })
            {
                var property = serialized.FindProperty(name);
                Debug.Log($"[ImportDiagnostics] {when}: {name} = {(property == null ? "<no property>" : property.objectReferenceValue ? property.objectReferenceValue.name : "null")}");
            }
        }
    }
}
