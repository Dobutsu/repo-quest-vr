using Mono.Cecil;
using Mono.Cecil.Cil;

namespace HarmonyBaker;

/// <summary>
/// Repairs stack type mismatches that RepoXR transpilers leave behind when the game changed under them. Mono does not
/// verify IL, so they happen to work on PC; IL2CPP turns them into C++ that does not compile. Two shapes, both only
/// where the consumer is the very next instruction:
///   - an instance call whose receiver has an unrelated type and which returns that same type: the call is dropped
///     (ValuableArcticSnowBike.UpdateSteering: GetSourceTransform() now returns the Transform that
///     localCamera.GetOverrideTransform() used to)
///   - a value of a base type consumed where a subtype is required: a castclass is inserted
///     (CosmeticTokenUIElement.OnDestroy: non-generic Object.Instantiate where the game used Instantiate&lt;GameObject&gt;)
/// </summary>
internal static class TypeRepairs
{
    public static List<string> Apply(MethodBody body)
    {
        var repairs = new List<string>();
        var il = body.GetILProcessor();

        foreach (var ins in body.Instructions.ToList())
        {
            if (ins.Previous is { } previous && ins.OpCode.FlowControl == FlowControl.Call &&
                ins.Operand is MethodReference { HasThis: true, Parameters.Count: 0 } call &&
                ResultType(previous) is { } receiver && !IsAssignable(receiver, call.DeclaringType) &&
                call.ReturnType.FullName == receiver.FullName)
            {
                repairs.Add($"dropped {call.DeclaringType.Name}::{call.Name} on a {receiver.Name}");
                ins.OpCode = OpCodes.Nop;
                ins.Operand = null;
                continue;
            }

            if (ins.Next is { } next && ResultType(ins) is { IsValueType: false } produced &&
                RequiredType(next, body) is { } required && required.FullName != produced.FullName &&
                !IsAssignable(produced, required) && IsAssignable(required, produced))
            {
                repairs.Add($"castclass {required.Name} after {Describe(ins)}");
                il.InsertAfter(ins, il.Create(OpCodes.Castclass, required));
            }
        }

        return repairs;
    }

    private static TypeReference? ResultType(Instruction ins) => ins.Operand switch
    {
        MethodReference m when ins.OpCode.FlowControl == FlowControl.Call && ins.OpCode != OpCodes.Newobj &&
                               m.ReturnType.MetadataType != MetadataType.Void && !m.ReturnType.ContainsGenericParameter => m.ReturnType,
        FieldReference f when ins.OpCode == OpCodes.Ldfld || ins.OpCode == OpCodes.Ldsfld => f.FieldType,
        _ => null,
    };

    private static TypeReference? RequiredType(Instruction consumer, MethodBody body)
    {
        var type = consumer.Operand switch
        {
            VariableDefinition v when consumer.OpCode.Code is Code.Stloc or Code.Stloc_S => v.VariableType,
            _ when consumer.OpCode.Code is >= Code.Stloc_0 and <= Code.Stloc_3 =>
                body.Variables[consumer.OpCode.Code - Code.Stloc_0].VariableType,
            FieldReference f when consumer.OpCode == OpCodes.Stfld || consumer.OpCode == OpCodes.Stsfld => f.FieldType,
            MethodReference m when consumer.OpCode.FlowControl == FlowControl.Call =>
                m.Parameters.Count > 0 ? m.Parameters[^1].ParameterType : m.HasThis ? m.DeclaringType : null,
            _ => null,
        };
        return type is null || type.IsValueType || type.ContainsGenericParameter || type.IsByReference ? null : type;
    }

    /// <summary>Whether a value of type <paramref name="from"/> may be used as <paramref name="to"/>; true when unknown</summary>
    private static bool IsAssignable(TypeReference from, TypeReference to)
    {
        if (to.FullName is "System.Object" || from.FullName == to.FullName)
            return true;
        if (from is TypeSpecification || to is TypeSpecification)
            return true; // arrays, generic instances: not needed here, do not touch

        TypeDefinition? definition;
        try
        {
            definition = from.Resolve();
        }
        catch (AssemblyResolutionException)
        {
            return true;
        }

        for (var t = definition; t != null;)
        {
            if (t.FullName == to.FullName || t.Interfaces.Any(i => i.InterfaceType.FullName == to.FullName))
                return true;
            try
            {
                t = t.BaseType?.Resolve();
            }
            catch (AssemblyResolutionException)
            {
                return true;
            }
        }

        return definition is null;
    }

    private static string Describe(Instruction ins) => ins.Operand is MemberReference m ? $"{m.DeclaringType?.Name}::{m.Name}" : ins.OpCode.Name;
}
