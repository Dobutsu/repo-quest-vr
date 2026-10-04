using System.Formats.Tar;
using System.IO.Compression;

namespace RepoQuest.Builder;

public static class Archive
{
    /// <summary>
    /// Extracts a .zip/.nupkg, .tar.gz or .tar.xz into <paramref name="targetDir"/> (replaced if it exists), through a
    /// temporary folder so an interrupted extraction never looks complete
    /// </summary>
    public static async Task ExtractAsync(string archive, string targetDir, CancellationToken ct = default)
    {
        var temp = targetDir + ".extracting";
        FileOps.DeleteDirectory(temp);
        Directory.CreateDirectory(temp);
        var name = Path.GetFileName(archive).ToLowerInvariant();

        if (name.EndsWith(".zip") || name.EndsWith(".nupkg"))
        {
            await Task.Run(() => ZipFile.ExtractToDirectory(archive, temp, overwriteFiles: true), ct);
        }
        else if (name.EndsWith(".tar.gz") || name.EndsWith(".tgz"))
        {
            await using var file = File.OpenRead(archive);
            await using var gzip = new GZipStream(file, CompressionMode.Decompress);
            await TarFile.ExtractToDirectoryAsync(gzip, temp, overwriteFiles: true, ct);
        }
        else if (name.EndsWith(".tar.xz"))
        {
            // No xz in .NET; macOS and Windows 10+ both have bsdtar
            await ProcessRunner.RunCheckedAsync(new ProcessSpec
            {
                FileName = "tar",
                Arguments = ["-xf", archive, "-C", temp],
            }, $"Could not unpack {Path.GetFileName(archive)}", ct: ct);
        }
        else
        {
            throw new BuildException($"Unknown archive type: {archive}");
        }

        FileOps.DeleteDirectory(targetDir);
        Directory.Move(temp, targetDir);
    }
}
