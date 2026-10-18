#!/usr/bin/env bash
# RepoXR as the Quest build uses it: external/RepoXR (v1.2.3, left untouched) plus patches/repoxr/*.patch, built in
# work/repoxr-src.
#   scripts/prepare-repoxr.sh   -> work/repoxr-src/bin/Release/netstandard2.1/RepoXR.dll
# Patches (in order):
#   0001-head-bob-setting  "Head Bob" slider in RepoXR's Gameplay settings (0-100%, default 25% = upstream behaviour)
#   0002-pixel-lights-setting  "Pixel Lights" slider in Rendering (per-object pixel light limit)
#   0003-multiview-by-default  single-pass instanced stereo on (Post Processing is fixed for it, patch_packages.py)
#   0004-post-processing-setting  "Post Processing" switch (off by default: brighter, ~5 ms GPU); the main camera's
#                                 volume-less post stack is always off
#   0005-other-flashlight-shadows  other players' flashlights cast no shadows unless switched on (7-10 ms GPU with six)
#   0006-lamp-lighting  lamps lit per pixel in one pass by the Quest shaders (RepoQuestLamps.cginc), no Unity light passes
#   0007-lamp-lighting-arms-and-previews  the VR arms use the game's Hurtable shader (the bundle's copy has no lamps);
#                                         the menus' deferred player previews render forward, where the lamps are
#   0008-focus-sphere-hidden-when-faded  the focus sphere's renderer is off while it is fully faded: its GrabPass copied
#                                        the whole frame every frame (Pico 4 Ultra: 85 -> 14 FPS in the levels)
set -euo pipefail

root="$(cd "$(dirname "$0")/.." && pwd)"
src="$root/work/repoxr-src"
dotnet="${DOTNET:-$HOME/.dotnet/dotnet}"

# Fresh sources on every run; bin/ and obj/ stay for incremental builds
mkdir -p "$src"
rsync -a --delete --exclude .git --exclude bin --exclude obj "$root/external/RepoXR/" "$src/"
for patch in "$root"/patches/repoxr/*.patch; do
    patch -p1 --forward --silent -d "$src" < "$patch" || { echo "patch failed: $(basename "$patch")" >&2; exit 1; }
    echo "applied $(basename "$patch")"
done

"$dotnet" build -c Release "$src/RepoXR.csproj" -v quiet -nologo >/dev/null
echo "RepoXR built: $src/bin/Release/netstandard2.1/RepoXR.dll"
