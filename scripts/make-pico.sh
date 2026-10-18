#!/usr/bin/env bash
# A Pico 4 / Pico 4 Ultra test build from a finished Quest APK, patched by ovrport (github.com/ovrport/app, GPL-3.0).
#   scripts/make-pico.sh [input.apk] [ovrport-instance]
#   (defaults: work/build/repo-quest-vr.apk, 3.3.0 — the newest instance not marked experimental)
# Output: work/pico/repo-pico-ovrp<instance>.apk (another input: work/pico/<input name>-pico-ovrp<instance>.apk)
# PICO_DEBUGGABLE=0 leaves out ovrport's patch_mark_as_debuggable (Android runs debuggable apps slower) and adds
# "-nodebug" to the name; simpleperf then needs the input built with -profiling (profileable).
# ovrport swaps libopenxr_loader.so for its own, which picks Pico's loader by ro.product.brand and maps the Touch
# bindings to bytedance/pico4(s)_controller, and adds the manifest entries Pico needs (pvr.app.type=vr). The game's
# own libraries stay untouched; minSdk is already 29 (Android 10, what Pico 4 runs).
# Signing: ovrport's own key in work/pico/ovrport-ws/signatures — keep it, so updates install over the old build.
set -euo pipefail

root="$(cd "$(dirname "$0")/.." && pwd)"
input="${1:-$root/work/build/repo-quest-vr.apk}"
input="$(cd "$(dirname "$input")" && pwd)/$(basename "$input")"   # the CLI runs from work/pico
instance="${2:-3.3.0}"
pico="$root/work/pico"
tools="$pico/tools"
ovrport_version=1.2.3
jar="$tools/overportcli-$ovrport_version-all.jar"
mkdir -p "$tools" "$pico/ovrport-ws"

# The CLI is built for Java 17; Unity's bundled OpenJDK is 11
java="${JAVA:-}"
if [ -z "$java" ]; then
    java="$(find "$tools/jre" -path '*/Contents/Home/bin/java' 2>/dev/null | head -1)"
    if [ -z "$java" ]; then
        mkdir -p "$tools/jre"
        curl -fsSL "https://api.adoptium.net/v3/binary/latest/17/ga/mac/aarch64/jre/hotspot/normal/eclipse?project=jdk" \
            | tar xz -C "$tools/jre"
        java="$(find "$tools/jre" -path '*/Contents/Home/bin/java' | head -1)"
    fi
fi
if [ ! -f "$jar" ]; then
    curl -fsSL -o "$tools/cli-jar.zip" "https://github.com/ovrport/app/releases/download/$ovrport_version/cli-jar.zip"
    unzip -o -q "$tools/cli-jar.zip" -d "$tools" && rm "$tools/cli-jar.zip"
fi

# ovrport's recommended set, minus patch_replace_icon_label: it looks the package up in Meta's store, and ours isn't there
patches="patch_copy_libraries;patch_copy_ovrplugin_vrapi;patch_fix_min_android_sdk;patch_generate_config"
patches+=";patch_remove_localized_names;patch_clean_up_frida;patch_oculus_unity;patch_oculus_unreal;patch_vr_metadata"
patches+=";patch_launcher_entry;patch_remove_uses_library;patch_fix_unreal_crash;patch_meta_xr_audio"
patches+=";patch_mark_allow_backup"
[ "${PICO_DEBUGGABLE:-1}" = 0 ] || patches+=";patch_mark_as_debuggable"

name="repo-pico"
[ "$input" = "$root/work/build/repo-quest-vr.apk" ] || name="$(basename "$input" .apk)-pico"
[ "${PICO_DEBUGGABLE:-1}" != 0 ] || name+="-nodebug"

cd "$pico"
"$java" -Xmx6g -jar "$jar" patch --input="$input" --output="$pico" --output-name="$name-ovrp{overport}.apk" \
    --workspace="$pico/ovrport-ws" --version="$instance" --patches="$patches" \
    | tr '\r' '\n' | grep -E '^(Using|Output file)' | sort -u
