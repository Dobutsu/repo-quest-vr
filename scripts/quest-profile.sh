#!/usr/bin/env bash
# Where the game's CPU time goes, from a build made with `scripts/build.sh <name> -profiling` (profileable, with
# IL2CPP symbols next to the APK). Samples the running game with simpleperf, then reports CPU share per thread and the
# hottest functions of the main thread (UnityMain) and the render thread (UnityGfxDeviceW).
#   scripts/quest-profile.sh [seconds] [apk name]      default: 20 s, repo-quest-vr
# Play a level with the headset on while it records. Output: work/profile/<time>/ (perf.data, report-*.txt).
set -euo pipefail

root="$(cd "$(dirname "$0")/.." && pwd)"
seconds="${1:-20}"
name="${2:-repo-quest-vr}"
sdk="/Applications/Unity/Hub/Editor/2022.3.62f3/PlaybackEngines/AndroidPlayer"
adb="$sdk/SDK/platform-tools/adb"
simpleperf="$sdk/NDK/simpleperf"
package="io.repoxr.quest"
out="$root/work/profile/$(date +%Y%m%d-%H%M%S)"
mkdir -p "$out"

"$adb" shell pidof "$package" >/dev/null || { echo "the game is not running on the headset" >&2; exit 1; }

# IL2CPP symbols: <apk>-<version>-v<code>-IL2CPP.symbols.zip holds libil2cpp/libunity with debug info; simpleperf
# matches them to the on-device libraries by build id
symbols="$(ls -t "$root/work/build/$name"*.symbols.zip 2>/dev/null | head -1 || true)"
[ -n "$symbols" ] || { echo "no symbols zip for $name in work/build (build with -profiling)" >&2; exit 1; }
mkdir -p "$out/symbols"
unzip -qo "$symbols" -d "$out/symbols"
# The zip names them lib*.sym.so / lib*.dbg.so; the binary cache wants the library's own name
find "$out/symbols" -name "*.so" | while read -r f; do
    base="$(basename "$f" | sed -E 's/\.(sym|dbg)\.so$/.so/')"
    if [ "$base" != "$(basename "$f")" ]; then mv "$f" "$(dirname "$f")/$base"; fi
done

echo "recording $seconds s of $package ..."
"$adb" shell setprop security.perf_harden 0 2>/dev/null || true
"$adb" shell simpleperf record --app "$package" -g --duration "$seconds" -f 1000 -o /data/local/tmp/perf.data \
    > "$out/record.log" 2>&1 || { cat "$out/record.log" >&2; exit 1; }
"$adb" pull /data/local/tmp/perf.data "$out/perf.data" >/dev/null
"$adb" shell rm -f /data/local/tmp/perf.data

cd "$out"
# The cache builder pulls the remaining system libraries from the device with adb from PATH. It loads the NDK's
# x86_64-only report library, so it runs under Rosetta with the system's universal python3.
# Note: Unity 2022.3 ships libunity stripped (its "symbols" are the same file), so engine function names in the
# reports are the nearest exported symbol and mostly wrong; per-thread and per-library shares and the game's own
# code (libil2cpp) are reliable
PATH="$sdk/SDK/platform-tools:$PATH" arch -x86_64 /usr/bin/python3 "$simpleperf/binary_cache_builder.py" \
    -i perf.data -lib "$out/symbols" > cache.log 2>&1 || true
host_simpleperf="$simpleperf/bin/darwin/x86_64/simpleperf"
report() { "$host_simpleperf" report -i perf.data --symfs binary_cache "$@" 2>/dev/null; }

# The render thread has no name in 2022.3 release builds (a "Thread-N"): the busiest thread besides UnityMain
report --sort comm,dso > report-threads.txt
render="$(grep -E "^[0-9.]+%" report-threads.txt | awk '$2 != "UnityMain" {print $2; exit}')"
report --comms UnityMain --sort dso,symbol > report-main.txt
report --comms "$render" --sort dso,symbol > report-render.txt

echo "== CPU share per thread and library"; grep -E "^[0-9.]+%" report-threads.txt | head -14 | sed -E 's|/data/app/[^ ]*/lib/arm64/||'
echo "== main thread (self)"; grep -E "^[0-9.]+%" report-main.txt | head -20 | sed -E 's|/data/app/[^ ]*/lib/arm64/||'
echo "== render thread $render (self)"; grep -E "^[0-9.]+%" report-render.txt | head -12 | sed -E 's|/data/app/[^ ]*/lib/arm64/||' 
echo "full reports: $out"
