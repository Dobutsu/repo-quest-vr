#!/usr/bin/env bash
# Bake RepoXR's Harmony patches into the game assemblies.
#   scripts/bake.sh [universal|all] [windows|android]     (defaults: all, android)
# "android" also applies the Android-only platform overrides from tools/RepoXR.Standalone (no Win32 calls).
# Inputs:  work/game (link to the REPO install), external/RepoXR + patches/repoxr (scripts/prepare-repoxr.sh),
#          work/downloads/RepoXR-<ver> (release zip)
# Output:  work/baked/<target>/*.dll + bake-report.json
set -euo pipefail

root="$(cd "$(dirname "$0")/.." && pwd)"
target="${1:-all}"
platform="${2:-android}"
dotnet="${DOTNET:-$HOME/.dotnet/dotnet}"
release="$root/work/downloads/RepoXR-1.2.3/BepInEx/plugins/RepoXR"

"$root/scripts/prepare-repoxr.sh"
"$dotnet" build -c Release "$root/tools/HarmonyBaker" -v quiet -nologo >/dev/null
"$dotnet" build -c Release "$root/tools/SteamShim" -v quiet -nologo >/dev/null  # referenced by RepoXR.Standalone
"$dotnet" build -c Release "$root/tools/RepoXR.Standalone" -v quiet -nologo >/dev/null

out="$root/work/baked/$target-$platform"
rm -rf "$out"
"$dotnet" "$root/tools/HarmonyBaker/bin/Release/net8.0/HarmonyBaker.dll" \
    --game "$root/work/game/REPO_Data/Managed" \
    --mod "$root/work/repoxr-src/bin/Release/netstandard2.1/RepoXR.dll" \
    --deps "$release/RuntimeDeps" \
    --deps "$HOME/.nuget/packages/bepinex.baselib" \
    --deps "$HOME/.nuget/packages/customdiscoverstatelib" \
    --overrides "$root/tools/RepoXR.Standalone/bin/Release/netstandard2.1/RepoXR.Standalone.dll" \
    --platform "$platform" \
    --rewrite "$root/work/downloads/BepInEx5/BepInEx/core/0Harmony.dll" \
    --targets "$target" \
    --out "$out"
