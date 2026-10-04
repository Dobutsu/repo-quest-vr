using Mono.Cecil;

// AssemblyRenamer --map Old=New [--map ...] [--search <dir>...] <dll>...
// Rewrites the given assemblies in place: renamed assemblies get their new identity (and file name), every assembly
// reference to a renamed one is retargeted. Unity treats Assembly-CSharp(-firstpass) as names of its own compiled
// scripts, so the game's precompiled copies must not keep them.
// typeof(X) in attribute arguments is stored as a string ("X, Assembly-CSharp, ..."); those are rewritten too, or
// IL2CPP's linker fails to resolve the old name (e.g. RepoXR's [HarmonyPatch(typeof(GameDirector))]).

var map = new Dictionary<string, string>(StringComparer.Ordinal);
var files = new List<string>();
var searchDirs = new List<string>();
for (var i = 0; i < args.Length; i++)
{
    if (args[i] == "--map" && i + 1 < args.Length)
    {
        var parts = args[++i].Split('=', 2);
        map[parts[0]] = parts[1];
    }
    else if (args[i] == "--search" && i + 1 < args.Length)
    {
        // Attribute arguments of enum types need their definitions (Unity, BepInEx) to be decoded
        searchDirs.Add(Path.GetFullPath(args[++i]));
    }
    else
    {
        files.Add(Path.GetFullPath(args[i]));
    }
}

if (map.Count == 0 || files.Count == 0)
{
    Console.Error.WriteLine("usage: AssemblyRenamer --map Old=New [--map ...] [--search <dir>...] <dll>...");
    return 64;
}

var resolver = new DefaultAssemblyResolver();
foreach (var dir in files.Select(Path.GetDirectoryName).Concat(searchDirs).Distinct())
    resolver.AddSearchDirectory(dir!);

foreach (var file in files)
{
    var bytes = File.ReadAllBytes(file);
    using var assembly = AssemblyDefinition.ReadAssembly(new MemoryStream(bytes), new ReaderParameters { AssemblyResolver = resolver });
    var changes = new List<string>();

    // Parse every attribute blob first, so its type references share the module's (about to be renamed) references
    var attributeScopes = AttributeTypeScopes(assembly).ToList();

    if (map.TryGetValue(assembly.Name.Name, out var newName))
    {
        changes.Add($"name {assembly.Name.Name} -> {newName}");
        assembly.Name.Name = newName;
        assembly.MainModule.Name = newName + ".dll";
    }

    foreach (var reference in assembly.MainModule.AssemblyReferences)
    {
        if (map.TryGetValue(reference.Name, out var target))
        {
            changes.Add($"ref {reference.Name} -> {target}");
            reference.Name = target;
        }
    }

    var attributeRenames = 0;
    foreach (var scope in attributeScopes.Distinct())
    {
        if (map.TryGetValue(scope.Name, out var target))
        {
            scope.Name = target;
            attributeRenames++;
        }
    }
    if (attributeRenames > 0)
        changes.Add($"{attributeRenames} attribute type scopes");

    if (changes.Count == 0)
        continue;

    var output = assembly.Name.Name + ".dll" == Path.GetFileName(file)
        ? file
        : Path.Combine(Path.GetDirectoryName(file)!, assembly.Name.Name + ".dll");
    assembly.Write(output);
    if (output != file)
        File.Delete(file);

    Console.WriteLine($"{Path.GetFileName(file)}: {string.Join(", ", changes)}{(output != file ? $" (now {Path.GetFileName(output)})" : "")}");
}

return 0;

static IEnumerable<AssemblyNameReference> AttributeTypeScopes(AssemblyDefinition assembly)
{
    var providers = new List<ICustomAttributeProvider> { assembly, assembly.MainModule };
    foreach (var type in assembly.MainModule.GetTypes())
    {
        providers.Add(type);
        providers.AddRange(type.GenericParameters);
        providers.AddRange(type.Interfaces);
        providers.AddRange(type.Fields);
        providers.AddRange(type.Properties);
        providers.AddRange(type.Events);
        foreach (var method in type.Methods)
        {
            providers.Add(method);
            providers.Add(method.MethodReturnType);
            providers.AddRange(method.Parameters);
            providers.AddRange(method.GenericParameters);
        }
    }

    foreach (var attribute in providers.Where(p => p.HasCustomAttributes).SelectMany(p => p.CustomAttributes))
    {
        var arguments = attribute.ConstructorArguments
            .Concat(attribute.Fields.Select(f => f.Argument))
            .Concat(attribute.Properties.Select(p => p.Argument));
        foreach (var scope in arguments.SelectMany(ArgumentTypes).SelectMany(TypeScopes))
            yield return scope;
    }
}

static IEnumerable<TypeReference> ArgumentTypes(CustomAttributeArgument argument)
{
    yield return argument.Type;
    switch (argument.Value)
    {
        case TypeReference type:
            yield return type;
            break;
        case CustomAttributeArgument boxed:
            foreach (var t in ArgumentTypes(boxed))
                yield return t;
            break;
        case CustomAttributeArgument[] array:
            foreach (var t in array.SelectMany(ArgumentTypes))
                yield return t;
            break;
    }
}

static IEnumerable<AssemblyNameReference> TypeScopes(TypeReference type)
{
    switch (type)
    {
        case GenericInstanceType generic:
            foreach (var scope in TypeScopes(generic.ElementType).Concat(generic.GenericArguments.SelectMany(TypeScopes)))
                yield return scope;
            yield break;
        case TypeSpecification spec:
            foreach (var scope in TypeScopes(spec.ElementType))
                yield return scope;
            yield break;
    }

    for (var t = type; t != null; t = t.DeclaringType)
        if (t.DeclaringType == null && t.Scope is AssemblyNameReference reference)
            yield return reference;
}
