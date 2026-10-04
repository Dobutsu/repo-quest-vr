using System.Text.Json;
using RepoQuest.Builder.Updates;

namespace RepoQuest.Builder.Cli;

/// <summary>
/// Release tooling for scripts/release.sh: the signing key, latest.json and its signature.
///   keygen --key &lt;private.pem&gt; --public &lt;public.txt&gt;   new key pair (refuses to overwrite a key)
///   manifest --version &lt;x.y.z&gt; --asset &lt;rid&gt;=&lt;zip&gt; [--asset ...] [--notes-ru &lt;file&gt;] [--notes-en &lt;file&gt;] --out &lt;latest.json&gt;
///   sign --key &lt;private.pem&gt; --file &lt;latest.json&gt;   writes &lt;file&gt;.sig
/// </summary>
internal static class ReleaseCommands
{
    public static int KeyGen(Options options)
    {
        var keyPath = Require(options, "key");
        var publicPath = Require(options, "public");
        if (File.Exists(keyPath))
            throw new BuildException($"{keyPath} exists; a new key would orphan every builder that carries the old one");
        var (privatePem, publicKey) = ReleaseSigning.GenerateKey();
        Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(keyPath))!);
        File.WriteAllText(keyPath, privatePem);
        if (!OperatingSystem.IsWindows())
            File.SetUnixFileMode(keyPath, UnixFileMode.UserRead | UnixFileMode.UserWrite);
        FileOps.WriteText(publicPath, publicKey + "\n");
        Console.WriteLine($"private key: {keyPath} (keep a backup: without it no update can be published)");
        Console.WriteLine($"public key:  {publicPath}");
        return 0;
    }

    public static int Manifest(Options options, string[] rawArgs)
    {
        var version = Require(options, "version");
        var assets = new Dictionary<string, ReleaseAsset>();
        for (var i = 0; i < rawArgs.Length - 1; i++)
        {
            if (rawArgs[i] != "--asset")
                continue;
            var (rid, zip) = (rawArgs[i + 1].Split('=', 2)[0], rawArgs[i + 1].Split('=', 2)[1]);
            assets[rid] = new ReleaseAsset(Path.GetFileName(zip), Hashing.Sha256File(zip), new FileInfo(zip).Length);
        }
        if (assets.Count == 0)
            throw new BuildException("manifest: at least one --asset <rid>=<zip>");
        var notes = new Dictionary<string, string>();
        foreach (var language in new[] { "ru", "en" })
        {
            if (options.Get($"notes-{language}") is { } file && File.Exists(file))
                notes[language] = File.ReadAllText(file).Trim();
        }
        var manifest = new ReleaseManifest(version, DateTime.UtcNow, notes, assets);
        FileOps.WriteText(Require(options, "out"), JsonSerializer.Serialize(manifest, KitManifest.Json) + "\n");
        return 0;
    }

    public static int Sign(Options options)
    {
        var file = Require(options, "file");
        var signature = ReleaseSigning.Sign(File.ReadAllBytes(file), File.ReadAllText(Require(options, "key")));
        FileOps.WriteText(file + ".sig", signature + "\n");
        var publicKey = options.Get("public") is { } publicPath ? File.ReadAllText(publicPath).Trim() : null;
        if (publicKey is not null && !ReleaseSigning.Verify(File.ReadAllBytes(file), signature, publicKey))
            throw new BuildException("the signature does not verify with the public key the builder carries");
        return 0;
    }

    private static string Require(Options options, string key) =>
        options.Get(key) ?? throw new BuildException($"--{key} is required");
}
