using System.IO.Compression;
using System.Text;

namespace RepoQuest.Builder.Tests;

public sealed class KitArchiveTests
{
    public const string FixtureValue = "fixture-value";

    [Fact]
    public void ReadsEmbeddedResourceAndConstantsWithoutLoadingTargetAssembly()
    {
        var path = typeof(KitArchiveTests).Assembly.Location;
        var resource = KitArchive.ReadResource(File.ReadAllBytes(path), "kit-test.txt");
        Assert.Equal("Embedded kit resource fixture.", Encoding.UTF8.GetString(resource).Trim());
        Assert.Equal(FixtureValue, BuilderDiagnostics.ReadStringConstant(path, typeof(KitArchiveTests).FullName!, nameof(FixtureValue)));
        BuilderDiagnostics.RequireType(path, typeof(KitArchiveTests).FullName!);
        Assert.Throws<BuildException>(() => BuilderDiagnostics.RequireType(path, "Missing.CloudMenu"));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void ReadsCompressedAndUncompressedV6Bundles(bool compressed)
    {
        var assembly = File.ReadAllBytes(typeof(KitArchiveTests).Assembly.Location);
        var host = (byte[])assembly.Clone();
        var optionalHeader = BitConverter.ToInt32(host, 0x3c) + 24;
        var directories = optionalHeader + (BitConverter.ToUInt16(host, optionalHeader) == 0x20b ? 112 : 96);
        Array.Clear(host, directories + 14 * 8, 8); // a valid native PE without a CLR metadata directory
        using var content = new MemoryStream();
        if (compressed)
        {
            using (var deflate = new DeflateStream(content, CompressionLevel.Optimal, leaveOpen: true)) deflate.Write(assembly);
        }
        else content.Write(assembly);
        using var bundle = new MemoryStream();
        using var writer = new BinaryWriter(bundle, Encoding.UTF8, leaveOpen: true);
        writer.Write(host);
        var pointer = bundle.Position;
        writer.Write(0L);
        writer.Write(Convert.FromHexString("8B1202B96A612038727B930214D7A03213F5B9E6EFAE3318EE3B2DCE24B36AAE"));
        var offset = bundle.Position;
        writer.Write(content.ToArray());
        var header = bundle.Position;
        writer.Write(6u); writer.Write(0u); writer.Write(1); writer.Write("fixture");
        writer.Write(new byte[40]);
        writer.Write(offset); writer.Write((long)assembly.Length); writer.Write(compressed ? content.Length : 0L);
        writer.Write((byte)1); writer.Write("RepoQuestBuilder.dll");
        bundle.Position = pointer;
        writer.Write(header);
        Assert.Equal(assembly, KitArchive.ReadManagedAssembly(bundle.ToArray()));
    }

    [Fact]
    public void RejectsArchiveTraversalWithoutWritingOutsideItsStagingFolder()
    {
        var root = Directory.CreateTempSubdirectory("kit-traversal-").FullName;
        try
        {
            using var bytes = new MemoryStream();
            using (var zip = new ZipArchive(bytes, ZipArchiveMode.Create, leaveOpen: true))
            {
                using var text = new StreamWriter(zip.CreateEntry("../outside.txt").Open());
                text.Write("must not escape");
            }
            var destination = Path.Combine(root, "kit");
            Assert.Throws<BuildException>(() => KitArchive.ExtractZip(bytes.ToArray(), destination));
            Assert.False(File.Exists(Path.Combine(root, "outside.txt")));
            Assert.False(Directory.Exists(destination));
            Assert.Empty(Directory.EnumerateFileSystemEntries(root));
        }
        finally { Directory.Delete(root, recursive: true); }
    }

    [Fact]
    public void ExtractionNeverReplacesAnExistingNonemptyFolder()
    {
        var root = Directory.CreateTempSubdirectory("kit-preserve-").FullName;
        try
        {
            var keep = Path.Combine(root, "keep.txt");
            File.WriteAllText(keep, "user content");
            Assert.Throws<BuildException>(() => KitArchive.ExtractZip([], root));
            Assert.Equal("user content", File.ReadAllText(keep));
        }
        finally { Directory.Delete(root, recursive: true); }
    }

    [Fact]
    public void StartupDefaultsAreOptionalAndMalformedJsonDoesNotPreventStartup()
    {
        var root = Directory.CreateTempSubdirectory("builder-defaults-").FullName;
        try
        {
            Assert.Null(BuilderLaunchDefaults.Load(root).GamePath);
            var path = Path.Combine(root, "builder-startup.json");
            File.WriteAllText(path, """{"gamePath":"C:\\Games\\REPO","workspacePath":"C:\\RepoQuest","language":"ru"}""");
            var options = BuilderLaunchDefaults.Load(root);
            Assert.Equal(@"C:\Games\REPO", options.GamePath);
            Assert.Equal(@"C:\RepoQuest", options.WorkspacePath);
            Assert.Equal("ru", options.Language);
            File.WriteAllText(path, "broken json");
            Assert.Null(BuilderLaunchDefaults.Load(root).GamePath);
        }
        finally { Directory.Delete(root, recursive: true); }
    }
}
