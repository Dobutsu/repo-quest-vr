using System.Reflection;
using HarmonyLib;
using Mono.Cecil;
using Mono.Cecil.Cil;
using MethodAttributes = Mono.Cecil.MethodAttributes;
using MethodBody = Mono.Cecil.Cil.MethodBody;
using TypeAttributes = Mono.Cecil.TypeAttributes;

namespace HarmonyBaker;

/// <summary>
/// Harmony can detour native engine methods (e.g. Camera.set_targetTexture) at runtime; there is no IL to bake into.
/// Instead we emit a static wrapper into the mod assembly that runs the prefixes, the original and the postfixes,
/// and point every managed call site at it. That covers all calls coming from C#, which is what a runtime detour of
/// an InternalCall intercepts as well.
/// </summary>
internal sealed class NativeHooks
{
    public const string Namespace = "RepoXR.Baked";
    public const string TypeName = "NativeHooks";

    public sealed record Hook(MethodBase Original, string WrapperName, IReadOnlyList<MethodInfo> Prefixes, IReadOnlyList<MethodInfo> Postfixes);

    public List<Hook> Hooks { get; } = [];
    public List<string> Errors { get; } = [];

    public NativeHooks(IEnumerable<BakingPatcher> nativePatchers)
    {
        foreach (var patcher in nativePatchers)
        {
            var original = patcher.Original;
            var info = Harmony.GetPatchInfo(original);
            var name = $"{original.DeclaringType!.FullName}::{original.Name}";

            if (info is null)
                continue;

            if (info.Transpilers.Count > 0 || info.Finalizers.Count > 0)
            {
                Errors.Add($"{name}: transpilers/finalizers on a native method cannot be baked");
                continue;
            }

            if (!original.IsStatic && original.DeclaringType.IsValueType)
            {
                Errors.Add($"{name}: native instance methods on value types are not supported");
                continue;
            }

            Hooks.Add(new Hook(original, $"{original.DeclaringType.Name}_{original.Name}_{Hooks.Count}",
                Sorted(info.Prefixes), Sorted(info.Postfixes)));
        }
    }

    public bool Any => Hooks.Count > 0;

    public void EmitWrappers(ModuleDefinition module, BodyTransplanter importer)
    {
        if (!Any)
            return;

        var type = new TypeDefinition(Namespace, TypeName,
            TypeAttributes.Public | TypeAttributes.Abstract | TypeAttributes.Sealed | TypeAttributes.BeforeFieldInit,
            importer.ImportType(typeof(object)));
        module.Types.Add(type);

        foreach (var hook in Hooks)
        {
            try
            {
                type.Methods.Add(EmitWrapper(hook, importer));
            }
            catch (Exception e)
            {
                Errors.Add($"{hook.WrapperName}: {e.Message}");
            }
        }
    }

    /// <summary>Rewrites call/callvirt of hooked native methods to the wrappers; returns the number of call sites</summary>
    public int RedirectCallSites(ModuleDefinition module, string modAssemblyName)
    {
        if (!Any)
            return 0;

        var redirected = 0;
        foreach (var type in AllTypes(module))
        {
            if (type.Namespace == Namespace && type.Name == TypeName)
                continue; // the wrappers themselves call the originals

            foreach (var method in type.Methods.Where(m => m.HasBody))
            foreach (var ins in method.Body.Instructions)
            {
                if ((ins.OpCode != OpCodes.Call && ins.OpCode != OpCodes.Callvirt) || ins.Operand is not MethodReference callee)
                    continue;

                var hook = Hooks.FirstOrDefault(h => Matches(callee, h.Original));
                if (hook is null)
                    continue;

                if (ins.Previous?.OpCode == OpCodes.Constrained)
                    throw new NotSupportedException($"constrained call to {callee.FullName} in {method.FullName}");

                ins.OpCode = OpCodes.Call;
                ins.Operand = WrapperReference(module, modAssemblyName, hook, callee);
                redirected++;
            }
        }

        return redirected;
    }

    private MethodDefinition EmitWrapper(Hook hook, BodyTransplanter importer)
    {
        var original = hook.Original;
        var returnType = original is MethodInfo mi ? mi.ReturnType : typeof(void);
        var hasResult = returnType != typeof(void);

        var wrapper = new MethodDefinition(hook.WrapperName,
            MethodAttributes.Public | MethodAttributes.Static | MethodAttributes.HideBySig, importer.ImportType(returnType));

        var argOffset = 0;
        if (!original.IsStatic)
        {
            wrapper.Parameters.Add(new ParameterDefinition("__instance", Mono.Cecil.ParameterAttributes.None, importer.ImportType(original.DeclaringType!)));
            argOffset = 1;
        }

        var originalParams = original.GetParameters();
        foreach (var p in originalParams)
            wrapper.Parameters.Add(new ParameterDefinition(p.Name, Mono.Cecil.ParameterAttributes.None, importer.ImportType(p.ParameterType)));

        var body = new MethodBody(wrapper) { InitLocals = true };
        var runOriginal = new VariableDefinition(importer.ImportType(typeof(bool)));
        body.Variables.Add(runOriginal);
        var result = hasResult ? new VariableDefinition(importer.ImportType(returnType)) : null;
        if (result is not null)
            body.Variables.Add(result);

        var il = body.GetILProcessor();
        il.Emit(OpCodes.Ldc_I4_1);
        il.Emit(OpCodes.Stloc, runOriginal);

        foreach (var prefix in hook.Prefixes)
        {
            EmitPatchArguments(il, prefix, originalParams, argOffset, runOriginal, result);
            il.Emit(OpCodes.Call, importer.ImportMethod(prefix));

            if (prefix.ReturnType == typeof(bool))
            {
                // Harmony: a prefix returning false skips the original
                il.Emit(OpCodes.Ldloc, runOriginal);
                il.Emit(OpCodes.And);
                il.Emit(OpCodes.Stloc, runOriginal);
            }
            else if (prefix.ReturnType != typeof(void))
            {
                il.Emit(OpCodes.Pop);
            }
        }

        var afterOriginal = Instruction.Create(OpCodes.Nop);
        il.Emit(OpCodes.Ldloc, runOriginal);
        il.Emit(OpCodes.Brfalse, afterOriginal);
        for (var i = 0; i < wrapper.Parameters.Count; i++)
            il.Emit(OpCodes.Ldarg, wrapper.Parameters[i]);
        il.Emit(original.IsStatic ? OpCodes.Call : OpCodes.Callvirt, importer.ImportMethod(original));
        if (result is not null)
            il.Emit(OpCodes.Stloc, result);
        il.Append(afterOriginal);

        foreach (var postfix in hook.Postfixes)
        {
            EmitPatchArguments(il, postfix, originalParams, argOffset, runOriginal, result);
            il.Emit(OpCodes.Call, importer.ImportMethod(postfix));

            // Pass-through postfix: returns the new result
            if (result is not null && postfix.ReturnType == returnType)
                il.Emit(OpCodes.Stloc, result);
            else if (postfix.ReturnType != typeof(void))
                il.Emit(OpCodes.Pop);
        }

        if (result is not null)
            il.Emit(OpCodes.Ldloc, result);
        il.Emit(OpCodes.Ret);

        wrapper.Body = body;
        return wrapper;
    }

    private static void EmitPatchArguments(ILProcessor il, MethodInfo patch, ParameterInfo[] originalParams, int argOffset,
        VariableDefinition runOriginal, VariableDefinition? result)
    {
        var wrapperParams = il.Body.Method.Parameters;

        foreach (var p in patch.GetParameters())
        {
            var byRef = p.ParameterType.IsByRef;
            switch (p.Name)
            {
                case "__instance":
                    il.Emit(OpCodes.Ldarg, wrapperParams[0]);
                    continue;
                case "__runOriginal":
                    il.Emit(byRef ? OpCodes.Ldloca : OpCodes.Ldloc, runOriginal);
                    continue;
                case "__result" when result is not null:
                    il.Emit(byRef ? OpCodes.Ldloca : OpCodes.Ldloc, result);
                    continue;
            }

            var index = Array.FindIndex(originalParams, op => op.Name == p.Name);
            if (index < 0 && p.Name is { } n && n.StartsWith("__") && int.TryParse(n[2..], out var positional))
                index = positional;
            if (index < 0 || index >= originalParams.Length)
                throw new NotSupportedException($"{patch.DeclaringType?.FullName}::{patch.Name}: unsupported injected parameter '{p.Name}'");

            il.Emit(byRef ? OpCodes.Ldarga : OpCodes.Ldarg, wrapperParams[index + argOffset]);
        }
    }

    private static MethodReference WrapperReference(ModuleDefinition module, string modAssemblyName, Hook hook, MethodReference callee)
    {
        var local = module.Assembly.Name.Name == modAssemblyName ? module.GetType(Namespace, TypeName) : null;
        TypeReference declaring = local ?? new TypeReference(Namespace, TypeName, module, ModReference(module, modAssemblyName));

        var reference = new MethodReference(hook.WrapperName, callee.ReturnType, declaring) { HasThis = false };
        if (callee.HasThis)
            reference.Parameters.Add(new ParameterDefinition(callee.DeclaringType));
        foreach (var p in callee.Parameters)
            reference.Parameters.Add(new ParameterDefinition(p.ParameterType));

        return reference;
    }

    private static AssemblyNameReference ModReference(ModuleDefinition module, string modAssemblyName)
    {
        var existing = module.AssemblyReferences.FirstOrDefault(r => r.Name == modAssemblyName);
        if (existing is not null)
            return existing;

        var created = new AssemblyNameReference(modAssemblyName, new Version(0, 0, 0, 0));
        module.AssemblyReferences.Add(created);
        return created;
    }

    private static bool Matches(MethodReference callee, MethodBase original) =>
        callee.Name == original.Name &&
        callee.DeclaringType.FullName == original.DeclaringType!.FullName &&
        callee.Parameters.Select(p => p.ParameterType.FullName)
            .SequenceEqual(original.GetParameters().Select(p => CecilName(p.ParameterType)));

    private static string CecilName(Type type) => type.FullName!.Replace('+', '/');

    private static IEnumerable<TypeDefinition> AllTypes(ModuleDefinition module)
    {
        var stack = new Stack<TypeDefinition>(module.Types);
        while (stack.Count > 0)
        {
            var type = stack.Pop();
            yield return type;
            foreach (var nested in type.NestedTypes)
                stack.Push(nested);
        }
    }

    private static List<MethodInfo> Sorted(IEnumerable<Patch> patches) =>
        patches.OrderByDescending(p => p.priority).ThenBy(p => p.index).Select(p => p.PatchMethod).ToList();
}
