using RepoQuest.Builder.Pipeline;

namespace RepoQuest.Builder.Setup;

/// <summary>
/// Python for the pipeline's scripts, inside the workspace: uv installs a managed CPython and a venv with the packages
/// pinned (with hashes) in the kit's requirements.lock. Nothing is installed system-wide.
/// </summary>
public sealed class PythonEnv
{
    private PythonEnv(string python, Workspace workspace)
    {
        PythonExe = python;
        this.workspace = workspace;
    }

    private readonly Workspace workspace;

    public string PythonExe { get; }

    public static async Task<PythonEnv> EnsureAsync(Workspace workspace, Kit kit, ThirdParty thirdParty,
        CancellationToken ct)
    {
        var venv = Path.Combine(workspace.Tools, "venv");
        var python = Platform.IsWindows ? Path.Combine(venv, "Scripts", "python.exe") : Path.Combine(venv, "bin", "python");
        var marker = Path.Combine(venv, ".repoquest-lock");
        var wanted = Hashing.Combine(kit.Manifest.Python, Hashing.Sha256File(kit.PythonLock));
        if (File.Exists(python) && File.Exists(marker) && File.ReadAllText(marker).Trim() == wanted)
            return new PythonEnv(python, workspace);

        var uv = await thirdParty.UvAsync(ct);
        var environment = new Dictionary<string, string?>
        {
            ["UV_PYTHON_INSTALL_DIR"] = Path.Combine(workspace.Tools, "python"),
            ["UV_CACHE_DIR"] = Path.Combine(workspace.Tools, "uv-cache"),
            ["UV_NO_CONFIG"] = "1",
            ["UV_PYTHON_PREFERENCE"] = "only-managed",
            ["VIRTUAL_ENV"] = null,
            ["PYTHONHOME"] = null,
            ["PYTHONPATH"] = null,
        };
        Log.Info("setting up Python");
        FileOps.DeleteDirectory(venv);
        await ProcessRunner.RunCheckedAsync(new ProcessSpec
        {
            FileName = uv,
            Arguments = ["venv", venv, "--python", kit.Manifest.Python],
            Environment = environment,
            LogPrefix = "[uv] ",
        }, "Could not set up Python", ct: ct);
        await ProcessRunner.RunCheckedAsync(new ProcessSpec
        {
            FileName = uv,
            Arguments = ["pip", "install", "--python", python, "--require-hashes", "-r", kit.PythonLock],
            Environment = environment,
            LogPrefix = "[uv] ",
        }, "Could not install the Python packages", ct: ct);
        File.WriteAllText(marker, wanted);
        return new PythonEnv(python, workspace);
    }

    /// <summary>Runs one of the kit's scripts (scripts/&lt;name&gt;) the way the Mac checkout runs it, pointed at this
    /// build's folders through REPOQUEST_* (scripts/lib/paths.py)</summary>
    public Task<ProcessResult> RunScriptAsync(BuildContext context, string script, IEnumerable<string> args,
        CancellationToken ct) =>
        ProcessRunner.RunCheckedAsync(new ProcessSpec
        {
            FileName = PythonExe,
            Arguments = [Path.Combine(context.Kit.Scripts, script), .. args],
            WorkingDirectory = workspace.Root,
            Environment = new Dictionary<string, string?>
            {
                ["REPOQUEST_WORK"] = workspace.Root,
                ["REPOQUEST_GAME"] = context.Game.Path,
                ["REPOQUEST_UNITY_EDITOR"] = context.Unity?.Root,
                ["REPOQUEST_TOOL"] = context.Tools.Json,
                // Same text handling on every OS (Windows would read and write in the ANSI code page)
                ["PYTHONUTF8"] = "1",
                ["PYTHONIOENCODING"] = "utf-8",
                ["PYTHONDONTWRITEBYTECODE"] = "1",
                ["PYTHONHOME"] = null,
                ["PYTHONPATH"] = null,
            },
            LogPrefix = $"[{Path.GetFileNameWithoutExtension(script)}] ",
        }, $"{script} failed", ct: ct);
}
