using System.IO;

namespace RepoXR.Standalone
{
    internal static class CloudConfigFile
    {
        internal static void Write(string path, string json)
        {
            var temp = path + ".tmp";
            File.WriteAllText(temp, json);
            if (File.Exists(path)) File.Replace(temp, path, path + ".bak");
            else File.Move(temp, path);
        }
    }
}
