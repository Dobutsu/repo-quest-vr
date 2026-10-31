using System.Diagnostics;
using System.Reflection;
using HarmonyLib;
using MonoMod.RuntimeDetour;

namespace HarmonyBaker;

/// <summary>
/// Finds transpilers whose anchors are gone. Most of RepoXR's transpilers do not call ThrowIfInvalid: when a game
/// update removes the instruction a CodeMatcher looks for, the matcher ends past the last instruction and the
/// following Advance/Insert silently edits the end of the method instead (R.E.P.O. 0.5.0 moved
/// GameplayManager.itemUnequipAutoHold out of PhysGrabber.Update, and RepoXR's ForceHoldPatch went into the
/// grabDisableTimer countdown, which kept grabbing disabled forever).
///
/// Every CodeMatcher search that fails is recorded with the transpiler that ran it and the method being patched.
/// A search that fails after earlier ones succeeded in the same transpiler is usually a loop running out of matches
/// (Repeat, while(IsValid)) and is reported separately.
/// </summary>
internal static class MatcherWatch
{
    public sealed record Miss(string Transpiler, string Target, string Search, bool AfterSuccess);

    private static readonly List<Hook> Hooks = [];
    private static readonly List<Miss> AllMisses = [];
    private static readonly Dictionary<(string, string), int> Successes = [];

    public static IReadOnlyList<Miss> Misses => AllMisses;

    /// <summary>The method Harmony is currently building (set by <see cref="BakingPatcher"/>)</summary>
    public static MethodBase? Current { get; set; }

    private delegate CodeMatcher UseEndMatch(CodeMatcher self, bool useEnd, CodeMatch[] matches);
    private delegate CodeMatcher Match(CodeMatcher self, CodeMatch[] matches);
    private delegate CodeMatcher Search(CodeMatcher self, Func<CodeInstruction, bool> predicate);

    public static void Install()
    {
        foreach (var method in typeof(CodeMatcher).GetMethods(BindingFlags.Public | BindingFlags.Instance | BindingFlags.DeclaredOnly))
        {
            if (method.ReturnType != typeof(CodeMatcher) || !(method.Name.StartsWith("Match") || method.Name.StartsWith("Search")))
                continue;

            var types = method.GetParameters().Select(p => p.ParameterType).ToArray();
            var name = method.Name;
            Delegate? hook = types switch
            {
                [var b, var m] when b == typeof(bool) && m == typeof(CodeMatch[]) =>
                    (Func<UseEndMatch, CodeMatcher, bool, CodeMatch[], CodeMatcher>)((orig, self, useEnd, matches) =>
                        Record(orig(self, useEnd, matches), name, Describe(matches))),
                [var m] when m == typeof(CodeMatch[]) =>
                    (Func<Match, CodeMatcher, CodeMatch[], CodeMatcher>)((orig, self, matches) =>
                        Record(orig(self, matches), name, Describe(matches))),
                [var p] when p == typeof(Func<CodeInstruction, bool>) =>
                    (Func<Search, CodeMatcher, Func<CodeInstruction, bool>, CodeMatcher>)((orig, self, predicate) =>
                        Record(orig(self, predicate), name, "predicate")),
                _ => null,
            };

            if (hook is not null)
                Hooks.Add(new Hook(method, hook));
        }
    }

    private static string Describe(CodeMatch[] matches) =>
        string.Join(", ", matches.Select(m => m.opcodes.Count > 0 || m.operands.Count > 0
            ? $"{string.Join("/", m.opcodes)} {string.Join("/", m.operands)}".Trim()
            : m.name ?? "predicate"));

    private static CodeMatcher Record(CodeMatcher result, string search, string description)
    {
        var transpiler = new StackTrace().GetFrames()
            .Select(f => f.GetMethod())
            .FirstOrDefault(m => m?.DeclaringType is { } t && t.Assembly != typeof(MatcherWatch).Assembly &&
                                 t.Assembly != typeof(CodeMatcher).Assembly &&
                                 !t.Assembly.GetName().Name!.StartsWith("MonoMod", StringComparison.Ordinal) &&
                                 !t.Assembly.GetName().Name!.StartsWith("System", StringComparison.Ordinal));
        var name = transpiler is null ? "?" : $"{Outer(transpiler.DeclaringType!).FullName}::{Clean(transpiler.Name)}";
        var target = Current is null ? "?" : $"{Current.DeclaringType?.FullName}::{Current.Name}";
        var key = (name, target);

        if (result.IsValid)
            Successes[key] = Successes.GetValueOrDefault(key) + 1;
        else
            AllMisses.Add(new Miss(name, target, $"{search}({description})", Successes.GetValueOrDefault(key) > 0));

        return result;
    }

    private static Type Outer(Type type)
    {
        while (type.DeclaringType is not null && type.Name.StartsWith('<'))
            type = type.DeclaringType;
        return type;
    }

    // Local functions and lambdas: "<ForceHoldPatch>g__Foo|3_0" → "ForceHoldPatch"
    private static string Clean(string name) =>
        name.StartsWith('<') && name.IndexOf('>') is > 1 and var end ? name[1..end] : name;
}
