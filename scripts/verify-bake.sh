#!/usr/bin/env bash
# ILVerify the baked assemblies and report only errors that the originals do not already have.
#   scripts/verify-bake.sh [universal|all] [windows|android]
set -euo pipefail

root="$(cd "$(dirname "$0")/.." && pwd)"
target="${1:-all}"
export DOTNET_ROOT="${DOTNET_ROOT:-$HOME/.dotnet}"
ilverify="$root/work/tools/ilverify"
[ -x "$ilverify" ] || "$DOTNET_ROOT/dotnet" tool install --tool-path "$root/work/tools" dotnet-ilverify >/dev/null

game="$root/work/game/REPO_Data/Managed"
baked="$root/work/baked/$target-${2:-windows}"
mod="$root/work/repoxr-src/bin/Release/netstandard2.1/RepoXR.dll"  # scripts/prepare-repoxr.sh
release="$root/work/downloads/RepoXR-1.2.3/BepInEx/plugins/RepoXR"
bepinex="$root/work/downloads/BepInEx5/BepInEx/core"
harmony="$(dirname "$(find "$HOME/.nuget/packages/harmonyx/2.7.0" -name 0Harmony.dll -path '*netstandard2.0*' | head -1)")"
out="$root/work/ilverify/$target-${2:-windows}"
mkdir -p "$out"

deps=(-r "$root/tools/RepoXR.Standalone/bin/Release/netstandard2.1/RepoXR.Standalone.dll" -r "$release/RuntimeDeps/*.dll" -r "$HOME/.nuget/packages/bepinex.baselib/5.4.20/lib/netstandard2.0/*.dll" -r "$harmony/*.dll")
while IFS= read -r dir; do deps+=(-r "$dir/*.dll"); done < <(find "$HOME/.nuget/packages/customdiscoverstatelib" -name '*.dll' -exec dirname {} \; | sort -u)

# Normalize to "category method" so offsets and paths do not hide identical errors
normalize() { grep '^\[IL\]' "$1" | sed -E 's/^\[IL\]: Error \[([A-Za-z]+)\]: \[[^:]+ : ([^]]+)\].*/\1 \2/' | sort -u; }

status=0
for dll in "$baked"/*.dll; do
    name="$(basename "$dll" .dll)"
    original="$game/$name.dll"
    [ "$name" = "RepoXR" ] && original="$mod"

    if [ "$name" = "0Harmony" ]; then
        # BepInEx's own HarmonyX (net35), rewritten by an Android override: verify against it with its MonoMod/Cecil
        original="$bepinex/0Harmony.dll"
        "$ilverify" "$original" -s mscorlib -r "$game/*.dll" -r "$bepinex/Mono*.dll" > "$out/$name.orig.txt" 2>&1 || true
        "$ilverify" "$dll" -s mscorlib -r "$game/*.dll" -r "$bepinex/Mono*.dll" > "$out/$name.baked.txt" 2>&1 || true
    else
        "$ilverify" "$original" -s mscorlib -r "$game/*.dll" -r "$mod" "${deps[@]}" > "$out/$name.orig.txt" 2>&1 || true
        "$ilverify" "$dll" -s mscorlib -r "$baked/*.dll" -r "$game/*.dll" "${deps[@]}" > "$out/$name.baked.txt" 2>&1 || true
    fi

    new_errors="$(comm -13 <(normalize "$out/$name.orig.txt") <(normalize "$out/$name.baked.txt"))"
    count="$(printf '%s' "$new_errors" | grep -c . || true)"
    echo "$name: $count new error kinds (per method)"
    [ -n "$new_errors" ] && { printf '%s\n' "$new_errors" | sed 's/^/    /'; status=1; }
done
exit $status
