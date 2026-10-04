#!/usr/bin/env bash
# The Quest builder's kit in work/kit: everything that turns a player's own copy of the game into the Quest project,
# except the game. builder/QuestBuilder.Core/Kit.cs reads it; scripts/release.sh embeds it into the builder.
#   scripts/make-kit.sh [version]      (default version: dev)
# Contents:
#   kit.json, python/requirements.lock      builder/ (pinned downloads, Unity version, Python packages)
#   scripts/, unity-tools/                  the pipeline's Python scripts and Unity tooling from this checkout
#   prebuilt/repoxr/RepoXR.dll              RepoXR v1.2.3 + patches/repoxr (scripts/prepare-repoxr.sh)
#   prebuilt/standalone/                    tools/RepoXR.Standalone
#   prebuilt/steam/                         tools/SteamShim (Facepunch.Steamworks.Win64) with SteamKit2, protobuf-net(.Core),
#                                           System.Collections.Immutable
#   prebuilt/android/libopus_egpv.so        scripts/build-opus-android.sh
#   prebuilt/repoxr-android/                RepoXR's assets for Android (scripts/build-repoxr-assets.sh, Unity; rebuilt
#                                           when its inputs change)
#   licenses/, SOURCES.md                   licenses of what the kit carries and where its sources are
# The checks at the end refuse anything that looks like game files.
set -euo pipefail

root="$(cd "$(dirname "$0")/.." && pwd)"
kit="$root/work/kit"
version="${1:-dev}"
dotnet="${DOTNET:-$HOME/.dotnet/dotnet}"

"$root/scripts/prepare-repoxr.sh" >/dev/null
"$dotnet" build -c Release -v quiet -nologo "$root/tools/SteamShim" >/dev/null
"$dotnet" build -c Release -v quiet -nologo "$root/tools/RepoXR.Standalone" >/dev/null
[ -f "$root/work/opus-android/libopus_egpv.so" ] || "$root/scripts/build-opus-android.sh"

# RepoXR's Android assets take a Unity run: only when RepoXR-Unity or our changes to it moved on
assets="$root/work/repoxr-android"
stamp="$( (git -C "$root/external/RepoXR-Unity" rev-parse HEAD
           find "$root/unity-tools/RepoXRAssets" "$root/scripts/patch_repoxr_shaders.py" "$root/scripts/build-repoxr-assets.sh" \
               -type f -exec shasum -a 256 {} \; | sed "s|$root/||" | sort) | shasum -a 256 | cut -d' ' -f1)"
if [ ! -f "$assets/repoxrassets" ] || [ "$(cat "$assets/.stamp" 2>/dev/null)" != "$stamp" ]; then
    "$root/scripts/build-repoxr-assets.sh" >/dev/null
    echo "$stamp" > "$assets/.stamp"
fi

rm -rf "$kit"
mkdir -p "$kit"/{scripts/lib,python,prebuilt/{repoxr,standalone,steam,android,repoxr-android},licenses}
python3 - "$root/builder/kit.json" "$kit/kit.json" "$version" <<'EOF'
import json, sys
manifest = json.load(open(sys.argv[1]))
manifest["version"] = sys.argv[3]
open(sys.argv[2], "w").write(json.dumps(manifest, indent=2) + "\n")
EOF
cp "$root/builder/python/requirements.lock" "$kit/python/"
# Every pipeline script; not the Mac-only diagnostics and kit tooling
for script in "$root"/scripts/*.py; do
    case "$(basename "$script")" in
        inventory.py|shader_disasm.py|patch_repoxr_shaders.py) ;;
        *) cp "$script" "$kit/scripts/" ;;
    esac
done
cp "$root"/scripts/lib/*.py "$kit/scripts/lib/"
rsync -a --exclude RepoXRAssets --exclude .DS_Store "$root/unity-tools/" "$kit/unity-tools/"

cp "$root/work/repoxr-src/bin/Release/netstandard2.1/RepoXR.dll" "$kit/prebuilt/repoxr/"
cp "$root/tools/RepoXR.Standalone/bin/Release/netstandard2.1/RepoXR.Standalone.dll" "$kit/prebuilt/standalone/"
shim="$root/tools/SteamShim/bin/Release/netstandard2.1"
for dll in Facepunch.Steamworks.Win64 SteamKit2 protobuf-net protobuf-net.Core System.Collections.Immutable; do
    cp "$shim/$dll.dll" "$kit/prebuilt/steam/"
done
cp "$root/work/opus-android/libopus_egpv.so" "$kit/prebuilt/android/"
cp -R "$assets/repoxrassets" "$assets/Addressables" "$kit/prebuilt/repoxr-android/"

cp "$root/external/RepoXR/LICENSE" "$kit/licenses/GPL-3.0.txt"
cp "$root/work/downloads/opus-1.5.2/COPYING" "$kit/licenses/opus-BSD-3-Clause.txt"
cp "$root/builder/licenses/"* "$kit/licenses/" 2>/dev/null || true
[ -f "$root/builder/SOURCES.md" ] && cp "$root/builder/SOURCES.md" "$kit/"

# Checks: text written with "\n" on every OS (Windows Python would write "\r\n"), and nothing from the game
bad_writes="$(grep -nE 'write_text\(|open\([^)]*"w' "$kit"/scripts/*.py | grep -v 'newline=' || true)"
[ -z "$bad_writes" ] || { echo "kit scripts write text without newline=\"\\n\":" >&2; echo "$bad_writes" >&2; exit 1; }
suspicious="$(find "$kit" \( -name '*.assets' -o -name '*.resS' -o -name '*.resource' -o -name 'Assembly-CSharp*' \
    -o -name 'REPO_Data' -o -name 'globalgamemanagers*' -o -name 'level[0-9]*' \) -print)"
[ -z "$suspicious" ] || { echo "game files in the kit:" >&2; echo "$suspicious" >&2; exit 1; }
bundles="$(find "$kit" -name '*.bundle' -not -path "$kit/prebuilt/repoxr-android/*" -print)"
[ -z "$bundles" ] || { echo "unexpected asset bundles in the kit:" >&2; echo "$bundles" >&2; exit 1; }

echo "kit $version: $kit ($(du -sh "$kit" | cut -f1))"
