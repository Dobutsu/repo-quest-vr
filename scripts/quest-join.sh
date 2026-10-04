#!/usr/bin/env bash
# Join a friend's R.E.P.O. game from the running game on the headset, like "Join game" in the Steam friends list
# (until the VR menu has it). Needs a Steam login on the headset (scripts/quest-steam-login.sh).
#   scripts/quest-join.sh <friend name or SteamID>
#   scripts/quest-join.sh --list                     friends playing R.E.P.O. and their lobbies
set -euo pipefail

adb="/Applications/Unity/Hub/Editor/2022.3.62f3/PlaybackEngines/AndroidPlayer/SDK/platform-tools/adb"
files="/sdcard/Android/data/io.repoxr.quest/files"
[ $# -ge 1 ] || { echo "usage: $0 <friend name or SteamID> | --list" >&2; exit 64; }

"$adb" logcat -c
if [ "$1" = "--list" ]; then
    "$adb" shell touch "$files/steam-friends"
else
    printf '%s' "$1" | "$adb" shell "cat > $files/steam-join"
fi

# The game polls every second and asks Steam for fresh presence (4 s)
sleep 7
"$adb" logcat -d -s Unity:V | grep -E "\[RepoXR.Standalone\] Steam|\[Steam\]|Steam: " | sed -E 's/^.*Unity *: //' || echo "(no answer: is the game running with the headset on?)"
