using System.IO.Compression;
using System.Reflection;
using System.Text.Json;

namespace RepoQuest.Builder.Setup;

/// <summary>
/// The .NET tools of tools/ (HarmonyBaker, AssemblyRenamer, Il2CppFixes) are compiled into the builder and run as
/// its own child processes: <c>&lt;builder&gt; --tool &lt;name&gt; &lt;args&gt;</c>. The player needs no .NET install.
/// </summary>
public static class ToolHost
{
    public static readonly IReadOnlyDictionary<string, string> Tools = new Dictionary<string, string>
    {
        ["harmony-baker"] = "HarmonyBaker",
        ["assembly-renamer"] = "AssemblyRenamer",
        ["il2cpp-fixes"] = "Il2CppFixes",
    };

    /// <summary>Called first thing in Main: runs the tool and returns true when the process was started as one</summary>
    public static bool TryRun(string[] args, out int exitCode)
    {
        exitCode = 0;
        if (args.Length < 2 || args[0] != "--tool")
            return false;
        if (!Tools.TryGetValue(args[1], out var assemblyName))
        {
            Console.Error.WriteLine($"unknown tool {args[1]}; known: {string.Join(", ", Tools.Keys)}");
            exitCode = 64;
            return true;
        }

        var entry = Assembly.Load(new AssemblyName(assemblyName)).EntryPoint
                    ?? throw new InvalidOperationException($"{assemblyName} has no entry point");
        try
        {
            var result = entry.Invoke(null, entry.GetParameters().Length == 0 ? null : [args[2..]]);
            exitCode = result switch
            {
                int code => code,
                Task<int> task => task.GetAwaiter().GetResult(),
                Task task => Wait(task),
                _ => 0,
            };
        }
        catch (TargetInvocationException e) when (e.InnerException is not null)
        {
            Console.Error.WriteLine(e.InnerException);
            exitCode = 1;
        }
        Console.Out.Flush();
        return true;
    }

    private static int Wait(Task task)
    {
        task.GetAwaiter().GetResult();
        return 0;
    }
}

/// <summary>How this builder starts one of its tools: its own executable with --tool</summary>
public sealed class ToolLauncher
{
    public ToolLauncher(IReadOnlyList<string> command)
    {
        Command = command;
    }

    /// <summary>The command prefix: [exe, "--tool"] or, under `dotnet x.dll`, [dotnet, x.dll, "--tool"]</summary>
    public IReadOnlyList<string> Command { get; }

    /// <summary>For the Python scripts (REPOQUEST_TOOL, see scripts/lib/paths.py)</summary>
    public string Json => JsonSerializer.Serialize(Command);

    /// <summary>
    /// The tools for a build in this workspace. A release builder is a single-file exe, where MonoMod (HarmonyBaker's
    /// detours) cannot find the JIT it hooks; it carries the tools as a normal self-contained folder (tools.zip: the
    /// Cli, published without bundling) and runs them from the workspace. Development builds run themselves.
    /// </summary>
    public static ToolLauncher For(Workspace workspace)
    {
        using var zip = Assembly.GetEntryAssembly()?.GetManifestResourceStream("tools.zip");
        if (zip is null)
            return ForThisProcess();

        var dir = Path.Combine(workspace.Tools, $"builder-tools-{Platform.BuilderVersion}");
        var marker = Path.Combine(dir, ".zip-hash");
        using var buffer = new MemoryStream();
        zip.CopyTo(buffer);
        var hash = Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(buffer.ToArray()));
        if (!File.Exists(marker) || File.ReadAllText(marker).Trim() != hash)
        {
            var temp = dir + ".extracting";
            FileOps.DeleteDirectory(temp);
            buffer.Position = 0;
            using (var archive = new System.IO.Compression.ZipArchive(buffer, System.IO.Compression.ZipArchiveMode.Read, leaveOpen: true))
                archive.ExtractToDirectory(temp, overwriteFiles: true);
            File.WriteAllText(Path.Combine(temp, ".zip-hash"), hash);
            FileOps.DeleteDirectory(dir);
            Directory.Move(temp, dir);
            foreach (var old in Directory.EnumerateDirectories(workspace.Tools, "builder-tools-*").Where(d => d != dir && !d.EndsWith(".extracting")))
                FileOps.DeleteDirectory(old);
        }
        var exe = Path.Combine(dir, Platform.Exe("QuestBuilder.Cli"));
        if (!OperatingSystem.IsWindows())
            File.SetUnixFileMode(exe, UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute |
                                      UnixFileMode.GroupRead | UnixFileMode.GroupExecute | UnixFileMode.OtherRead | UnixFileMode.OtherExecute);
        return new ToolLauncher([exe, "--tool"]);
    }

    public static ToolLauncher ForThisProcess()
    {
        var process = Environment.ProcessPath ?? throw new InvalidOperationException("no process path");
        if (Path.GetFileNameWithoutExtension(process).Equals("dotnet", StringComparison.OrdinalIgnoreCase))
            return new ToolLauncher([process, Assembly.GetEntryAssembly()!.Location, "--tool"]);
        return new ToolLauncher([process, "--tool"]);
    }

    public Task<ProcessResult> RunAsync(string tool, IEnumerable<string> args, string failure, CancellationToken ct) =>
        ProcessRunner.RunCheckedAsync(new ProcessSpec
        {
            FileName = Command[0],
            Arguments = [.. Command.Skip(1), tool, .. args],
            LogPrefix = $"[{tool}] ",
        }, failure, ct: ct);
}
