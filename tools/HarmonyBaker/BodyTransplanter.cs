using Mono.Cecil;
using Mono.Cecil.Cil;
using Mono.Cecil.Rocks;

namespace HarmonyBaker;

/// <summary>
/// Copies a Harmony-generated body (living in a MonoMod DMD module, compiled against the baker's .NET runtime)
/// into the real method of an on-disk assembly:
///  - DMD methods are static with an explicit `this` parameter; argument indices map 1:1 onto the original
///  - BCL references (System.Private.CoreLib etc.) are retargeted to netstandard, which the game references
///  - references back into the target assembly itself become local definitions
/// </summary>
internal sealed class BodyTransplanter
{
    private readonly ModuleDefinition module;
    private AssemblyNameReference? netstandard;

    public HashSet<string> NetstandardTypesUsed { get; } = [];

    /// <summary>Stack type repairs made in transplanted bodies (see <see cref="TypeRepairs"/>)</summary>
    public List<string> TypeRepairsMade { get; } = [];

    public BodyTransplanter(ModuleDefinition module)
    {
        this.module = module;

        // Decided before anything is imported: ImportReference adds the source's own BCL reference (netstandard) to
        // the module, which would then be picked here and dropped again as stale when the module is written.
        // Game assemblies reference netstandard; some runtime libraries (BepInEx's net35 HarmonyX) are mscorlib-based,
        // and the types a body uses live there under the same names
        netstandard = module.AssemblyReferences.FirstOrDefault(r => r.Name == "netstandard")
                      ?? module.AssemblyReferences.FirstOrDefault(r => r.Name == "mscorlib");
    }

    // Only needed once a baked body touches the BCL; assemblies that just get call sites redirected may have neither
    private AssemblyNameReference Netstandard => netstandard
        ?? throw new InvalidOperationException($"{module.Name} references neither netstandard nor mscorlib; cannot retarget BCL references");

    public void Transplant(MethodDefinition source, MethodDefinition target)
    {
        var src = source.Body;

        // DMD bodies run as DynamicMethods, which always zero locals; a static body must ask for it (IL2CPP honours this)
        var body = new MethodBody(target) { InitLocals = src.InitLocals || src.Variables.Count > 0 };

        var variables = src.Variables.Select(v => new VariableDefinition(Import(v.VariableType, target))).ToList();
        variables.ForEach(body.Variables.Add);

        var instructions = new Dictionary<Instruction, Instruction>();
        foreach (var ins in src.Instructions)
        {
            var copy = Instruction.Create(OpCodes.Nop);
            copy.OpCode = ins.OpCode;
            instructions[ins] = copy;
            body.Instructions.Add(copy);
        }

        foreach (var ins in src.Instructions)
            instructions[ins].Operand = MapOperand(ins.Operand, source, body, instructions, variables);

        foreach (var handler in src.ExceptionHandlers)
        {
            body.ExceptionHandlers.Add(new ExceptionHandler(handler.HandlerType)
            {
                TryStart = Map(handler.TryStart),
                TryEnd = Map(handler.TryEnd),
                HandlerStart = Map(handler.HandlerStart),
                HandlerEnd = Map(handler.HandlerEnd),
                FilterStart = Map(handler.FilterStart),
                CatchType = handler.CatchType is null ? null : Import(handler.CatchType, target),
            });
        }

        FixHandlerExits(body);
        TypeRepairsMade.AddRange(TypeRepairs.Apply(body).Select(r => $"{target.DeclaringType.Name}::{target.Name}: {r}"));

        // Branch forms from the DMD may no longer fit once operands change size
        body.SimplifyMacros();
        body.OptimizeMacros();
        target.Body = body;
        return;

        Instruction? Map(Instruction? ins) => ins is null ? null : instructions[ins];
    }

    /// <summary>
    /// MonoMod's CecilILGenerator closes fault/finally handlers with `leave` instead of `endfinally` (seen on
    /// iterator MoveNext methods). Mono tolerates it at runtime; it is invalid IL, so fix it before IL2CPP sees it.
    /// </summary>
    private static void FixHandlerExits(MethodBody body)
    {
        foreach (var handler in body.ExceptionHandlers)
        {
            if (handler.HandlerType is not (ExceptionHandlerType.Fault or ExceptionHandlerType.Finally))
                continue;

            var last = handler.HandlerEnd?.Previous ?? body.Instructions[^1];
            if (last.OpCode != OpCodes.Leave && last.OpCode != OpCodes.Leave_S)
                continue;

            last.OpCode = OpCodes.Endfinally;
            last.Operand = null;
        }
    }

    public static void ReplaceWithReturn(MethodDefinition method)
    {
        var body = new MethodBody(method);
        body.Instructions.Add(Instruction.Create(OpCodes.Ret));
        method.Body = body;
    }

    private object? MapOperand(object? operand, MethodDefinition source, MethodBody body,
        Dictionary<Instruction, Instruction> instructions, List<VariableDefinition> variables)
    {
        var target = body.Method;
        return operand switch
        {
            null => null,
            Instruction i => instructions[i],
            Instruction[] targets => targets.Select(t => instructions[t]).ToArray(),
            VariableDefinition v => variables[v.Index],
            ParameterDefinition p => MapParameter(p, source, body),
            TypeReference t => Import(t, target),
            MethodReference m => Import(m, target),
            FieldReference f => Import(f, target),
            CallSite => throw new NotSupportedException("calli in a Harmony body is not supported"),
            _ => operand, // string / numeric constants
        };
    }

    private static ParameterDefinition MapParameter(ParameterDefinition p, MethodDefinition source, MethodBody body)
    {
        var target = body.Method;
        var index = source.Parameters.IndexOf(p);
        if (index < 0)
            throw new InvalidOperationException($"Parameter {p.Name} does not belong to the DMD method");

        if (!target.HasThis)
            return target.Parameters[index];

        return index == 0 ? body.ThisParameter : target.Parameters[index - 1];
    }

    public TypeReference ImportType(Type type) => Fix(module.ImportReference(type));

    public MethodReference ImportMethod(System.Reflection.MethodBase method) => Fix(module.ImportReference(method));


    private TypeReference Import(TypeReference type, IGenericParameterProvider context) =>
        Fix(module.ImportReference(type, context));

    private MethodReference Import(MethodReference method, IGenericParameterProvider context) =>
        Fix(module.ImportReference(method, context));

    private FieldReference Import(FieldReference field, IGenericParameterProvider context)
    {
        var imported = module.ImportReference(field, context);
        var declaring = Fix(imported.DeclaringType);

        if (declaring is TypeDefinition def)
            return def.Fields.FirstOrDefault(f => f.Name == imported.Name)
                   ?? throw new MissingFieldException(def.FullName, imported.Name);

        return new FieldReference(imported.Name, Fix(imported.FieldType), declaring);
    }

    private TypeReference Fix(TypeReference type)
    {
        switch (type)
        {
            case GenericParameter:
            case TypeDefinition:
                return type;
            case GenericInstanceType git:
            {
                var result = new GenericInstanceType(Fix(git.ElementType));
                foreach (var arg in git.GenericArguments)
                    result.GenericArguments.Add(Fix(arg));
                return result;
            }
            case ArrayType array:
                return new ArrayType(Fix(array.ElementType), array.Rank);
            case ByReferenceType byRef:
                return new ByReferenceType(Fix(byRef.ElementType));
            case PointerType pointer:
                return new PointerType(Fix(pointer.ElementType));
            case PinnedType pinned:
                return new PinnedType(Fix(pinned.ElementType));
            case RequiredModifierType req:
                return new RequiredModifierType(Fix(req.ModifierType), Fix(req.ElementType));
            case OptionalModifierType opt:
                return new OptionalModifierType(Fix(opt.ModifierType), Fix(opt.ElementType));
            case TypeSpecification spec:
                throw new NotSupportedException($"Unhandled type specification {spec.GetType().Name}: {spec}");
        }

        if (type.IsNested)
            type.DeclaringType = Fix(type.DeclaringType);

        var scopeName = ScopeAssemblyName(type);

        if (scopeName == module.Assembly.Name.Name)
            return module.GetType(type.FullName) ?? throw new TypeLoadException($"{type.FullName} not found in {module.Name}");

        if (scopeName is not null && AssemblyIndex.IsFrameworkName(scopeName))
        {
            if (!type.IsNested)
                type.Scope = Netstandard;
            NetstandardTypesUsed.Add(TopLevel(type).FullName);
        }

        return type;
    }

    private MethodReference Fix(MethodReference method)
    {
        if (method is GenericInstanceMethod gim)
        {
            var result = new GenericInstanceMethod(Fix(gim.ElementMethod));
            foreach (var arg in gim.GenericArguments)
                result.GenericArguments.Add(Fix(arg));
            return result;
        }

        var declaring = Fix(method.DeclaringType);

        if (declaring is TypeDefinition def)
        {
            return def.Methods.FirstOrDefault(m => SameSignature(m, method))
                   ?? throw new MissingMethodException(def.FullName, method.Name);
        }

        var copy = new MethodReference(method.Name, method.ReturnType, declaring)
        {
            HasThis = method.HasThis,
            ExplicitThis = method.ExplicitThis,
            CallingConvention = method.CallingConvention,
        };

        foreach (var gp in method.GenericParameters)
            copy.GenericParameters.Add(new GenericParameter(gp.Name, copy));

        copy.ReturnType = Fix(method.ReturnType);
        foreach (var p in method.Parameters)
            copy.Parameters.Add(new ParameterDefinition(p.Name, p.Attributes, Fix(p.ParameterType)));

        return copy;
    }

    private static bool SameSignature(MethodDefinition candidate, MethodReference wanted) =>
        candidate.Name == wanted.Name &&
        candidate.HasThis == wanted.HasThis &&
        candidate.GenericParameters.Count == wanted.GenericParameters.Count &&
        candidate.Parameters.Count == wanted.Parameters.Count &&
        candidate.ReturnType.FullName == wanted.ReturnType.FullName &&
        candidate.Parameters.Select(p => p.ParameterType.FullName)
            .SequenceEqual(wanted.Parameters.Select(p => p.ParameterType.FullName));

    private static TypeReference TopLevel(TypeReference type)
    {
        while (type.IsNested)
            type = type.DeclaringType;
        return type;
    }

    private static string? ScopeAssemblyName(TypeReference type) => TopLevel(type).Scope switch
    {
        AssemblyNameReference asm => asm.Name,
        ModuleDefinition mod => mod.Assembly?.Name.Name,
        _ => null,
    };
}
