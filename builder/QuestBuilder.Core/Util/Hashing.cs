using System.Security.Cryptography;
using System.Text;

namespace RepoQuest.Builder;

public static class Hashing
{
    public static string Sha256File(string path)
    {
        using var stream = File.OpenRead(path);
        return Convert.ToHexString(SHA256.HashData(stream)).ToLowerInvariant();
    }

    public static string Sha256(string text) =>
        Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(text))).ToLowerInvariant();

    public static string Sha512Base64File(string path)
    {
        using var stream = File.OpenRead(path);
        return Convert.ToBase64String(SHA512.HashData(stream));
    }

    /// <summary>
    /// MD5 of the text as lowercase hex: the .meta GUIDs of the plugins the pipeline adds (the same values the bash
    /// pipeline made with `printf '...' | md5`, so existing references and the Library stay valid)
    /// </summary>
    public static string Md5Hex(string text) =>
        Convert.ToHexString(MD5.HashData(Encoding.UTF8.GetBytes(text))).ToLowerInvariant();

    /// <summary>Hash of several parts (stamps of pipeline steps)</summary>
    public static string Combine(params string?[] parts) => Sha256(string.Join("\n", parts.Select(p => p ?? "")));

    /// <summary>Content hash of every file under a folder (relative path + file hash), in a stable order</summary>
    public static string Directory(string root, Func<string, bool>? include = null)
    {
        var lines = System.IO.Directory.EnumerateFiles(root, "*", SearchOption.AllDirectories)
            .Select(f => Path.GetRelativePath(root, f).Replace('\\', '/'))
            .Where(rel => include?.Invoke(rel) ?? true)
            .OrderBy(rel => rel, StringComparer.Ordinal)
            .Select(rel => rel + " " + Sha256File(Path.Combine(root, rel)));
        return Sha256(string.Join("\n", lines));
    }
}
