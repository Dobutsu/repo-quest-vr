using System.Reflection;
using System.Runtime.Loader;

namespace HarmonyBaker;

/// <summary>
/// Maps assembly names to files across the search directories (first directory wins), and plugs into the default
/// load context so the game's assemblies load next to our own HarmonyX. Framework assemblies are left to the runtime.
/// </summary>
internal sealed class AssemblyIndex
{
    private readonly Dictionary<string, string> paths = new(StringComparer.OrdinalIgnoreCase);

    public IReadOnlyList<string> Directories { get; }

    public AssemblyIndex(IEnumerable<string> directories)
    {
        Directories = directories.Where(Directory.Exists).ToList();

        foreach (var dir in Directories)
        foreach (var file in Directory.EnumerateFiles(dir, "*.dll", SearchOption.AllDirectories))
        {
            string name;
            try
            {
                name = AssemblyName.GetAssemblyName(file).Name!;
            }
            catch (BadImageFormatException)
            {
                continue; // native plugin
            }

            paths.TryAdd(name, file);
        }
    }

    public bool TryGetPath(string name, out string path) => paths.TryGetValue(name, out path!);

    public static bool IsFrameworkName(string name) =>
        name is "mscorlib" or "netstandard" or "System" or "Microsoft.Win32.Primitives" ||
        name.StartsWith("System.", StringComparison.Ordinal);

    /// <param name="preprocess">Returns replacement bytes for an assembly, or null to load the file as-is</param>
    public void InstallResolver(Func<string, string, byte[]?> preprocess)
    {
        AssemblyLoadContext.Default.Resolving += (context, name) =>
        {
            if (name.Name is null || IsFrameworkName(name.Name) || !TryGetPath(name.Name, out var path))
                return null;

            var replacement = preprocess(name.Name, path);
            return replacement is null ? context.LoadFromAssemblyPath(path) : context.LoadFromStream(new MemoryStream(replacement));
        };
    }
}
