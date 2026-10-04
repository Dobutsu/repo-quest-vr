using Mono.Cecil;
using Mono.Cecil.Cil;

// Il2CppFixes <dll>...   rewrites each given assembly in place with the fixes known for it (by assembly name)
//
// SteamKit2 2.5: SteamClient's constructor stamps job ids with Process.GetCurrentProcess().StartTime, which throws
// NotSupportedException under IL2CPP on Android ("does not support file handle duplication"). The client's own
// creation time serves the same purpose (a per-process constant): DateTime.Now instead of process.StartTime.
//
// protobuf-net 3.2: MetaType's tuple detection asks ParameterInfo.GetRequiredCustomModifiers() whether a setter is
// init-only; IL2CPP does not implement custom modifiers (NotSupportedException). No modifiers (null, which the code
// already treats as none) only means init-only setters count as public, which does not matter for Steam's messages.

var failed = false;
foreach (var path in args.Select(Path.GetFullPath))
{
    var bytes = File.ReadAllBytes(path);
    // Writing resolves enum constants from dependencies, which sit next to the assembly (Assets/Plugins/SteamKit)
    var resolver = new DefaultAssemblyResolver();
    resolver.AddSearchDirectory(Path.GetDirectoryName(path)!);
    using var assembly = AssemblyDefinition.ReadAssembly(new MemoryStream(bytes), new ReaderParameters { AssemblyResolver = resolver });
    var applied = assembly.Name.Name switch
    {
        "SteamKit2" => FixProcessStartTime(assembly, "SteamKit2.SteamClient", ".ctor"),
        "protobuf-net" => FixCustomModifiers(assembly, "ProtoBuf.Meta.MetaType", "IsPublicSetter"),
        _ => -1,
    };

    if (applied < 0)
    {
        Console.WriteLine($"{Path.GetFileName(path)}: no fixes known");
        continue;
    }
    if (applied == 0)
    {
        Console.Error.WriteLine($"{Path.GetFileName(path)}: expected call site not found (SteamKit2 changed?)");
        failed = true;
        continue;
    }

    assembly.Write(path);
    Console.WriteLine($"{Path.GetFileName(path)}: {applied} call site(s) fixed");
}

return failed ? 1 : 0;

static int FixCustomModifiers(AssemblyDefinition assembly, string typeName, string methodNamePart)
{
    var fixedSites = 0;
    foreach (var method in assembly.MainModule.GetType(typeName).Methods.Where(m => m.Name.Contains(methodNamePart) && m.HasBody))
    {
        var il = method.Body.GetILProcessor();
        foreach (var ins in method.Body.Instructions.ToList())
        {
            if (ins.Operand is MethodReference { Name: "GetRequiredCustomModifiers" or "GetOptionalCustomModifiers" } call &&
                call.DeclaringType.FullName is "System.Reflection.ParameterInfo" or "System.Reflection.FieldInfo" or "System.Reflection.PropertyInfo")
            {
                // callvirt ParameterInfo.GetRequiredCustomModifiers()  ->  pop; ldnull
                ins.OpCode = OpCodes.Pop;
                ins.Operand = null;
                il.InsertAfter(ins, il.Create(OpCodes.Ldnull));
                fixedSites++;
            }
        }
    }

    return fixedSites;
}

static int FixProcessStartTime(AssemblyDefinition assembly, string typeName, string methodName)
{
    var module = assembly.MainModule;
    // Built by hand against the BCL the assembly already references (importing from this tool's runtime would add
    // a System.Private.CoreLib reference IL2CPP cannot resolve)
    var bcl = module.AssemblyReferences.First(r => r.Name is "netstandard" or "mscorlib");
    var dateTime = new TypeReference("System", "DateTime", module, bcl, valueType: true);
    var now = new MethodReference("get_Now", dateTime, dateTime) { HasThis = false };

    var fixedSites = 0;
    foreach (var method in module.GetType(typeName).Methods.Where(m => m.Name == methodName && m.HasBody))
    {
        var il = method.Body.GetILProcessor();
        foreach (var ins in method.Body.Instructions.ToList())
        {
            if (ins.Operand is MethodReference { Name: "get_StartTime", DeclaringType.FullName: "System.Diagnostics.Process" })
            {
                // using (var process = Process.GetCurrentProcess()) processStartTime = process.StartTime;
                //   callvirt Process.get_StartTime()  ->  pop; call DateTime.get_Now()
                ins.OpCode = OpCodes.Pop;
                ins.Operand = null;
                il.InsertAfter(ins, il.Create(OpCodes.Call, now));
                fixedSites++;
            }
        }
    }

    return fixedSites;
}
