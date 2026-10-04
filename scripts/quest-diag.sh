#!/usr/bin/env bash
# Ask the running game on the Quest for a diagnostics snapshot (unity-tools/Runtime/QuestDiagnostics.cs) and pull it.
#   scripts/quest-diag.sh   -> work/diag/<time>/{screen.png, rt-*.png, report.txt}
# The game must be running and not paused (headset on).
set -euo pipefail

root="$(cd "$(dirname "$0")/.." && pwd)"
adb="/Applications/Unity/Hub/Editor/2022.3.62f3/PlaybackEngines/AndroidPlayer/SDK/platform-tools/adb"
files="/sdcard/Android/data/io.repoxr.quest/files"

before="$("$adb" shell ls "$files/diag" 2>/dev/null | sort | tail -1 || true)"
"$adb" shell touch "$files/diag-request"
for _ in $(seq 1 30); do
    sleep 1
    latest="$("$adb" shell ls "$files/diag" 2>/dev/null | sort | tail -1 || true)"
    if [ -n "$latest" ] && [ "$latest" != "$before" ] && "$adb" shell ls "$files/diag/$latest/report.txt" >/dev/null 2>&1; then
        mkdir -p "$root/work/diag"
        "$adb" pull "$files/diag/$latest" "$root/work/diag/" >/dev/null
        echo "$root/work/diag/$latest"
        exit 0
    fi
done
echo "no snapshot after 30 s: is the game running with the headset on?" >&2
exit 1
