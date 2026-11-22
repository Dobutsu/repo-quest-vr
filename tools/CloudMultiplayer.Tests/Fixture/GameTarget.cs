using System.Runtime.CompilerServices;

public static class GameTarget
{
    [MethodImpl(MethodImplOptions.NoInlining)]
    public static int Compute(int value) => value;
}

// The main mod's entry points are disabled by the baker, independently of the additional patch assembly.
namespace RepoXR.Patches
{
    public static class HarmonyPatcher
    {
        public static void PatchUniversal() { }
        public static void PatchVR() { }
    }
}
