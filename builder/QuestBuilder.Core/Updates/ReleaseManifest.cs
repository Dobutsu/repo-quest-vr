using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace RepoQuest.Builder.Updates;

public sealed record ReleaseAsset(string Url, string Sha256, long Size);

/// <summary>
/// latest.json of a release: the version, what changed (per language) and the builder for each platform. Next to it,
/// latest.json.sig holds an ECDSA P-256 signature of the file's exact bytes; the builder carries the public key and
/// ignores releases that are not signed with the matching private key (kept outside the repository).
/// </summary>
public sealed record ReleaseManifest(
    string Version,
    DateTime Published,
    Dictionary<string, string> Notes,
    Dictionary<string, ReleaseAsset> Builder)
{
    public static ReleaseManifest Parse(byte[] json) =>
        JsonSerializer.Deserialize<ReleaseManifest>(json, KitManifest.Json)
        ?? throw new BuildException("The update manifest is empty");

    public string NotesFor(string language) =>
        Notes.TryGetValue(language, out var notes) ? notes : Notes.TryGetValue("en", out var en) ? en : "";

    public static bool IsNewer(string candidate, string current) =>
        System.Version.TryParse(candidate, out var a) && System.Version.TryParse(current, out var b) && a > b;
}

/// <summary>Signing and checking release manifests (scripts/release.sh signs with QuestBuilder.Cli sign-release)</summary>
public static class ReleaseSigning
{
    /// <summary>Generates a key pair: the private key as PKCS#8 PEM, the public key as base64 SubjectPublicKeyInfo</summary>
    public static (string PrivateKeyPem, string PublicKey) GenerateKey()
    {
        using var key = ECDsa.Create(ECCurve.NamedCurves.nistP256);
        return (key.ExportPkcs8PrivateKeyPem(), Convert.ToBase64String(key.ExportSubjectPublicKeyInfo()));
    }

    public static string Sign(byte[] data, string privateKeyPem)
    {
        using var key = ECDsa.Create();
        key.ImportFromPem(privateKeyPem);
        return Convert.ToBase64String(key.SignData(data, HashAlgorithmName.SHA256));
    }

    public static string PublicKeyOf(string privateKeyPem)
    {
        using var key = ECDsa.Create();
        key.ImportFromPem(privateKeyPem);
        return Convert.ToBase64String(key.ExportSubjectPublicKeyInfo());
    }

    public static bool Verify(byte[] data, string signatureBase64, string publicKey)
    {
        try
        {
            using var key = ECDsa.Create();
            key.ImportSubjectPublicKeyInfo(Convert.FromBase64String(publicKey), out _);
            return key.VerifyData(data, Convert.FromBase64String(signatureBase64.Trim()), HashAlgorithmName.SHA256);
        }
        catch (FormatException)
        {
            return false;
        }
        catch (CryptographicException)
        {
            return false;
        }
    }

    /// <summary>
    /// The release public key compiled into this builder (builder/release-public-key.txt), or null. The window's
    /// project (the entry assembly) embeds it, so both assemblies are looked at, like <see cref="Kit"/> does.
    /// </summary>
    public static string? EmbeddedPublicKey()
    {
        const string name = "release-public-key.txt";
        using var stream = typeof(ReleaseSigning).Assembly.GetManifestResourceStream(name)
                           ?? System.Reflection.Assembly.GetEntryAssembly()?.GetManifestResourceStream(name);
        if (stream is null)
            return null;
        using var reader = new StreamReader(stream, Encoding.UTF8);
        var key = reader.ReadToEnd().Trim();
        return key.Length > 0 ? key : null;
    }
}
