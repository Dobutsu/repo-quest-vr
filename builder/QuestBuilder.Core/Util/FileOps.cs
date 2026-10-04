using System.Runtime.InteropServices;

namespace RepoQuest.Builder;

public static class FileOps
{
    /// <summary>Copies a folder tree; on macOS as an APFS clone (instant, no extra space) when the volume allows</summary>
    public static void CopyDirectory(string source, string target)
    {
        if (Directory.Exists(target))
            throw new IOException($"{target} already exists");
        Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(target))!);
        if (Platform.IsMac && clonefile(source, target, 0) == 0)
            return;

        foreach (var dir in Directory.EnumerateDirectories(source, "*", SearchOption.AllDirectories))
            Directory.CreateDirectory(Path.Combine(target, Path.GetRelativePath(source, dir)));
        Directory.CreateDirectory(target);
        foreach (var file in Directory.EnumerateFiles(source, "*", SearchOption.AllDirectories))
            File.Copy(file, Path.Combine(target, Path.GetRelativePath(source, file)));
    }

    /// <summary>Deletes a folder tree if it exists; clears read-only flags (Windows) and retries files held briefly
    /// by indexers or antivirus</summary>
    public static void DeleteDirectory(string path)
    {
        if (!Directory.Exists(path))
            return;
        for (var attempt = 1; ; attempt++)
        {
            try
            {
                if (Platform.IsWindows)
                {
                    foreach (var file in Directory.EnumerateFiles(path, "*", SearchOption.AllDirectories))
                        File.SetAttributes(file, FileAttributes.Normal);
                }
                Directory.Delete(path, recursive: true);
                return;
            }
            catch (Exception e) when (e is IOException or UnauthorizedAccessException && attempt < 5)
            {
                Log.Debug($"retrying delete of {path}: {e.Message}");
                Thread.Sleep(500 * attempt);
            }
        }
    }

    public static void DeleteFile(string path)
    {
        if (File.Exists(path))
        {
            File.SetAttributes(path, FileAttributes.Normal);
            File.Delete(path);
        }
    }

    public static void CopyFile(string source, string target)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(target))!);
        File.Copy(source, target, overwrite: true);
    }

    /// <summary>Copies the files matching a pattern from one folder into another (like `cp dir/*.cs target/`)</summary>
    public static void CopyFiles(string sourceDir, string pattern, string targetDir)
    {
        Directory.CreateDirectory(targetDir);
        foreach (var file in Directory.EnumerateFiles(sourceDir, pattern).Order(StringComparer.Ordinal))
            File.Copy(file, Path.Combine(targetDir, Path.GetFileName(file)), overwrite: true);
    }

    /// <summary>Writes text with "\n" line endings and without a BOM, the same bytes on every OS</summary>
    public static void WriteText(string path, string text)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(path))!);
        File.WriteAllBytes(path, new System.Text.UTF8Encoding(false).GetBytes(text.ReplaceLineEndings("\n")));
    }

    /// <summary>Writes a file atomically (temp file + rename), for state that must not be left half-written</summary>
    public static void WriteTextAtomic(string path, string text)
    {
        var temp = path + ".tmp";
        WriteText(temp, text);
        File.Move(temp, path, overwrite: true);
    }

    public static long FreeSpace(string path)
    {
        var root = Path.GetPathRoot(Path.GetFullPath(path))!;
        return new DriveInfo(root).AvailableFreeSpace;
    }

    // macOS: clones a file or a whole folder tree (APFS); strings marshal as UTF-8 on Unix
    [DllImport("libc", SetLastError = true)]
    private static extern int clonefile(string src, string dst, uint flags);
}
