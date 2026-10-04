using Mono.Cecil;
using Mono.Cecil.Cil;

namespace HarmonyBaker;

/// <summary>
/// Transpilers run at bake time and some of them read engine state (e.g. RepoXR's Plugin.GameDebugBuild reads
/// Debug.isDebugBuild). Engine calls are native and cannot run outside Unity, so the baker loads an in-memory copy
/// of UnityEngine.CoreModule where those few getters return the value the game would have reported.
/// </summary>
internal static class UnityBakeShim
{
    public static byte[]? Preprocess(string assemblyName, string path, bool isDebugBuild)
    {
        if (assemblyName != "UnityEngine.CoreModule")
            return null;

        using var assembly = AssemblyDefinition.ReadAssembly(path);
        var debug = assembly.MainModule.GetType("UnityEngine.Debug");
        var getter = debug.Properties.Single(p => p.Name == "isDebugBuild").GetMethod;

        getter.IsInternalCall = false;
        getter.IsRuntime = false;
        getter.IsIL = true;
        getter.IsManaged = true;

        var body = new MethodBody(getter);
        body.Instructions.Add(Instruction.Create(isDebugBuild ? OpCodes.Ldc_I4_1 : OpCodes.Ldc_I4_0));
        body.Instructions.Add(Instruction.Create(OpCodes.Ret));
        getter.Body = body;

        using var stream = new MemoryStream();
        assembly.Write(stream);
        return stream.ToArray();
    }

    /// <summary>
    /// Development builds compile scripts without optimizations, which is what RepoXR's GameDebugBuild branches on
    /// (different local slots / instruction counts). DebuggableAttribute tells us how Assembly-CSharp was compiled.
    /// </summary>
    public static bool IsDebugCompiled(string assemblyPath)
    {
        using var assembly = AssemblyDefinition.ReadAssembly(assemblyPath);
        var attribute = assembly.CustomAttributes.FirstOrDefault(a => a.AttributeType.FullName == "System.Diagnostics.DebuggableAttribute");
        if (attribute is null)
            return false;

        const int disableOptimizations = 0x100;
        var arg = attribute.ConstructorArguments[0].Value;
        var modes = arg is bool jit ? (jit ? disableOptimizations : 0) : Convert.ToInt32(arg);
        return (modes & disableOptimizations) != 0;
    }
}
