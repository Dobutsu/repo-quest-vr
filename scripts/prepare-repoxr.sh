#!/usr/bin/env bash
# RepoXR as the Quest build uses it: external/RepoXR (v1.2.3, left untouched) plus patches/repoxr/*.patch, built in
# work/repoxr-src.
#   scripts/prepare-repoxr.sh   -> work/repoxr-src/bin/Release/netstandard2.1/RepoXR.dll
# Patches (in order):
#   0001-head-bob-setting  "Head Bob" slider in RepoXR's Gameplay settings (0-100%, default 0% = off; upstream is 25%)
#   0002-pixel-lights-setting  "Pixel Lights" slider in Rendering (per-object pixel light limit, default 1)
#   0003-multiview-by-default  single-pass instanced stereo on (Post Processing is fixed for it, patch_packages.py)
#   0004-post-processing-setting  "Post Processing" switch (off by default: brighter, ~5 ms GPU); the main camera's
#                                 volume-less post stack is always off
#   0005-other-flashlight-shadows  other players' flashlights cast no shadows unless switched on (7-10 ms GPU with six)
#   0006-lamp-lighting  lamps lit per pixel in one pass by the Quest shaders (RepoQuestLamps.cginc), no Unity light passes
#   0007-lamp-lighting-arms-and-previews  the VR arms use the game's Hurtable shader (the bundle's copy has no lamps);
#                                         the menus' deferred player previews render forward, where the lamps are
#   0008-focus-sphere-hidden-when-faded  the focus sphere's renderer is off while it is fully faded: its GrabPass copied
#                                        the whole frame every frame (Pico 4 Ultra: 85 -> 14 FPS in the levels)
#   0009-repo-v0.5.0  R.E.P.O. v0.5.0 (the Sunstone update): new overloads, renamed input keys, the text input pop-up,
#                     menus clicked through InputKey.UIClick, InputGlyphs tags, the grab toggle, vehicles and the bike,
#                     the binary Addressables catalog (catalog_0.1.bin), the hover area in world space menus, the
#                     popups scaled with a Vector2 (z 0: their text drew as solid blocks in VR)
#   0010.. see the patches' own comments; 0039-post-processing-awake-0.5.1  R.E.P.O. 0.5.1 moved PostProcessing.Start
#                                            into Awake: the lens distortion and chromatic aberration patch follows
# Built against the game in work/game (RepoXR's tester mode): the REPO package on DaXcess' feed stops at 0.4.4
set -euo pipefail

root="$(cd "$(dirname "$0")/.." && pwd)"
src="$root/work/repoxr-src"
dotnet="${DOTNET:-$HOME/.dotnet/dotnet}"

# Fresh sources on every run; bin/ and obj/ stay for incremental builds
mkdir -p "$src"
rsync -a --delete --exclude .git --exclude bin --exclude obj "$root/external/RepoXR/" "$src/"
for patch in "$root"/patches/repoxr/*.patch; do
    # -E: a patch that deletes a file leaves it empty otherwise
    patch -p1 -E --forward --silent -d "$src" < "$patch" || { echo "patch failed: $(basename "$patch")" >&2; exit 1; }
    echo "applied $(basename "$patch")"
done

# Quiet unless it fails (dotnet prints its errors on stdout): then the errors, e.g. after a game update
[ -f "$root/work/game/REPO_Data/Managed/Assembly-CSharp.dll" ] || { echo "no game at work/game (installed?)" >&2; exit 1; }
if ! out=$("$dotnet" build -c Release "$src/RepoXR.csproj" -v quiet -nologo -p:USE_TESTER=true \
    "-p:TesterGamePath=$root/work/game"); then
    grep -E " error " <<<"$out" | sed -E 's/ \[[^]]*\]$//' | sort -u >&2
    exit 1
fi
echo "RepoXR built: $src/bin/Release/netstandard2.1/RepoXR.dll"
