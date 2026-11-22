using System.Buffers.Binary;
using System.IO.Compression;
using System.Reflection.Metadata;
using System.Reflection.PortableExecutable;
using System.Text;

namespace RepoQuest.Builder;

/// <summary>Reads kit.zip from an existing .NET builder without executing it or loading its assemblies.</summary>
public static class KitArchive
{
    // .NET apphost bundle marker and v6 manifest layout, as defined by dotnet/runtime's HostModel/Bundle.
    private static readonly byte[] Signature = Convert.FromHexString(
        "8B1202B96A612038727B930214D7A03213F5B9E6EFAE3318EE3B2DCE24B36AAE");

    public static void Extract(string builder, string destination)
    {
        var zip = ReadResource(ReadManagedAssembly(File.ReadAllBytes(builder)), "kit.zip");
        ExtractZip(zip, destination);
    }

    internal static byte[] ReadManagedAssembly(byte[] executable)
    {
        using (var pe = new PEReader(new MemoryStream(executable)))
            if (pe.HasMetadata) return executable;

        var marker = executable.AsSpan().IndexOf(Signature);
        if (marker < 8) throw new BuildException("The source EXE is not a supported .NET single-file builder");
        var header = BinaryPrimitives.ReadInt64LittleEndian(executable.AsSpan(marker - 8, 8));
        if (header < 0 || header > executable.Length - 12) throw new BuildException("Invalid bundle header");
        using var stream = new MemoryStream(executable, writable: false);
        using var reader = new BinaryReader(stream, Encoding.UTF8, leaveOpen: true);
        stream.Position = header;
        var major = reader.ReadUInt32();
        var minor = reader.ReadUInt32();
        var count = reader.ReadInt32();
        if (major is not (1 or 2 or 6) || minor != 0 || count is < 1 or > 10000)
            throw new BuildException($"Unsupported bundle manifest {major}.{minor}");
        _ = reader.ReadString(); // bundle ID
        if (major >= 2) stream.Position += 40; // deps/runtimeconfig locations, extraction flags
        for (var i = 0; i < count; i++)
        {
            var offset = reader.ReadInt64();
            var size = reader.ReadInt64();
            var compressed = major >= 6 ? reader.ReadInt64() : 0;
            var kind = reader.ReadByte();
            var name = reader.ReadString();
            if (kind != 1 || name != "RepoQuestBuilder.dll") continue;
            var stored = compressed == 0 ? size : compressed;
            if (offset < 0 || size is < 1 or > 512L * 1024 * 1024 || stored < 1 || stored > executable.Length - offset)
                throw new BuildException("Invalid bundled builder assembly bounds");
            using var content = new MemoryStream(executable, checked((int)offset), checked((int)stored), writable: false);
            if (compressed == 0) return content.ToArray();
            using var deflate = new DeflateStream(content, CompressionMode.Decompress);
            var result = new byte[checked((int)size)];
            deflate.ReadExactly(result);
            if (deflate.ReadByte() != -1) throw new BuildException("Bundled assembly exceeds its declared size");
            return result;
        }
        throw new BuildException("RepoQuestBuilder.dll is missing from the source EXE");
    }

    internal static byte[] ReadResource(byte[] assembly, string name)
    {
        using var pe = new PEReader(new MemoryStream(assembly));
        var metadata = pe.GetMetadataReader();
        var directory = pe.PEHeaders.CorHeader?.ResourcesDirectory ?? throw new BuildException("No CLR resources");
        foreach (var handle in metadata.ManifestResources)
        {
            var resource = metadata.GetManifestResource(handle);
            if (!resource.Implementation.IsNil || metadata.GetString(resource.Name) != name) continue;
            var data = pe.GetSectionData(directory.RelativeVirtualAddress).GetContent(0, directory.Size);
            var offset = checked((int)resource.Offset);
            if (offset < 0 || offset > data.Length - 4) throw new BuildException("Invalid embedded resource offset");
            var length = BinaryPrimitives.ReadInt32LittleEndian(data.AsSpan(offset, 4));
            if (length < 0 || length > data.Length - offset - 4) throw new BuildException("Invalid embedded resource length");
            return data.AsSpan(offset + 4, length).ToArray();
        }
        throw new BuildException($"The source builder has no embedded {name}");
    }

    internal static void ExtractZip(byte[] bytes, string destination)
    {
        destination = Path.GetFullPath(destination);
        if (Directory.Exists(destination) && Directory.EnumerateFileSystemEntries(destination).Any())
            throw new BuildException($"Refusing to overwrite a non-empty kit folder: {destination}");
        var staging = destination + ".extracting-" + Guid.NewGuid().ToString("N");
        Directory.CreateDirectory(staging);
        try
        {
            using var archive = new ZipArchive(new MemoryStream(bytes), ZipArchiveMode.Read);
            var prefix = staging + Path.DirectorySeparatorChar;
            foreach (var entry in archive.Entries)
            {
                var target = Path.GetFullPath(Path.Combine(staging, entry.FullName.Replace('/', Path.DirectorySeparatorChar)));
                if (target != staging && !target.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
                    throw new BuildException("A kit archive entry leaves the destination folder");
            }
            archive.ExtractToDirectory(staging);
            Kit.Load(staging).ValidateContent();
            if (Directory.Exists(destination)) Directory.Delete(destination); // verified empty above
            Directory.Move(staging, destination);
        }
        finally
        {
            // Only this invocation's uniquely named staging folder is removed, never the requested destination.
            if (Directory.Exists(staging)) Directory.Delete(staging, recursive: true);
        }
    }
}
