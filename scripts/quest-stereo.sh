#!/usr/bin/env bash
# Switch the headset's stereo rendering between Multi-Pass (each eye drawn separately) and single-pass instanced
# (multiview: both eyes in one pass, about half the draw call work) through RepoXR's SinglePassRendering setting.
# RepoXR reads it when XR starts, so the game is stopped first; start it again from the headset.
#   scripts/quest-stereo.sh multiview | multipass | status
set -euo pipefail

adb="/Applications/Unity/Hub/Editor/2022.3.62f3/PlaybackEngines/AndroidPlayer/SDK/platform-tools/adb"
cfg="/sdcard/Android/data/io.repoxr.quest/files/RepoXR/config/io.daxcess.repoxr.cfg"

case "${1:-status}" in
    multiview) value=true ;;
    multipass) value=false ;;
    status) "$adb" shell grep "^SinglePassRendering" "$cfg"; exit 0 ;;
    *) echo "usage: $0 multiview | multipass | status" >&2; exit 64 ;;
esac

"$adb" shell am force-stop io.repoxr.quest
# adb shell joins its arguments into one remote command line: quote the sed expression for the remote shell
"$adb" shell "sed -i 's/^SinglePassRendering = .*/SinglePassRendering = $value/' $cfg"
"$adb" shell grep "^SinglePassRendering" "$cfg"
