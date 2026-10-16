using System.Text.RegularExpressions;

namespace RepoQuest.Builder.Quest;

/// <summary>A device as `adb devices -l` lists it</summary>
public sealed record AdbDevice(string Serial, string State, string? Model)
{
    /// <summary>Connected and USB debugging allowed in the headset</summary>
    public bool Ready => State == "device";
    /// <summary>The headset asks to allow USB debugging (put it on and accept)</summary>
    public bool Unauthorized => State == "unauthorized";
    public bool IsQuest => Model?.Contains("Quest", StringComparison.OrdinalIgnoreCase) ?? false;
    /// <summary>Pico 4 reports its model as A8110, Pico 4 Ultra as A9210</summary>
    public bool IsPico => Model is { } model && (model.Contains("Pico", StringComparison.OrdinalIgnoreCase) ||
                                                 model.StartsWith("A8110", StringComparison.Ordinal) ||
                                                 model.StartsWith("A9210", StringComparison.Ordinal));
}

/// <summary>adb from the Android SDK that comes with Unity's Android module</summary>
public sealed partial class Adb
{
    public Adb(string executable)
    {
        Executable = executable;
    }

    public string Executable { get; }

    public async Task<IReadOnlyList<AdbDevice>> DevicesAsync(CancellationToken ct = default) =>
        ParseDevices((await RunAsync(null, ["devices", "-l"], ct)).Tail);

    /// <summary>The device lines of `adb devices -l` (the header and daemon messages are skipped)</summary>
    internal static IReadOnlyList<AdbDevice> ParseDevices(IEnumerable<string> lines) =>
        lines
            .Select(line => DeviceRegex().Match(line))
            .Where(m => m.Success)
            .Select(m => new AdbDevice(m.Groups[1].Value, m.Groups[2].Value,
                ModelRegex().Match(m.Groups[3].Value) is { Success: true } model ? model.Groups[1].Value.Replace('_', ' ') : null))
            .ToList();

    public Task<ProcessResult> RunAsync(string? serial, IEnumerable<string> args, CancellationToken ct = default) =>
        ProcessRunner.RunAsync(new ProcessSpec
        {
            FileName = Executable,
            Arguments = [.. serial is null ? Array.Empty<string>() : ["-s", serial], .. args],
            LogPrefix = "[adb] ",
        }, ct: ct);

    public async Task<string> ShellAsync(string serial, string command, CancellationToken ct = default)
    {
        var result = await RunAsync(serial, ["shell", command], ct);
        return result.TailText.Trim();
    }

    public async Task<bool> IsInstalledAsync(string serial, string package, CancellationToken ct = default) =>
        (await ShellAsync(serial, $"pm list packages {package}", ct))
        .Split('\n').Any(l => l.Trim() == $"package:{package}");

    public async Task<bool> IsRunningAsync(string serial, string package, CancellationToken ct = default) =>
        (await RunAsync(serial, ["shell", "pidof", package], ct)).ExitCode == 0;

    [GeneratedRegex(@"^(\S+)\s+(device|unauthorized|offline|no permissions|authorizing|recovery|bootloader)\b(.*)$")]
    private static partial Regex DeviceRegex();

    [GeneratedRegex(@"\bmodel:(\S+)")]
    private static partial Regex ModelRegex();
}
