#!/usr/bin/env bash
# Put the Mac's Steam login (a QR login saved by tools/SteamPhotonProbe in work/steam-session.json) on the headset,
# where the game's Steam shim picks it up at the next start (RepoXR.Standalone SteamSetup). The game can log in by
# itself too (the main menu's Steam button); this copy is for development. The headset then shares the Mac's token:
# LOG OUT on the headset revokes it for the Mac as well.
#   scripts/quest-steam-login.sh           copy the login
#   scripts/quest-steam-login.sh --remove  delete it from the headset (back to offline, singleplayer only)
# Only the account name, refresh token, SteamID and persona name are copied: the headset makes its own LoginID and
# machine id, so it never kicks the Mac's or the PC's Steam session. The refresh token is a credential: it goes to
# the game's private folder on the headset only.
set -euo pipefail

root="$(cd "$(dirname "$0")/.." && pwd)"
adb="/Applications/Unity/Hub/Editor/2022.3.62f3/PlaybackEngines/AndroidPlayer/SDK/platform-tools/adb"
target="/sdcard/Android/data/io.repoxr.quest/files/steam-session.json"

if [ "${1:-}" = "--remove" ]; then
    "$adb" shell rm -f "$target"
    echo "Steam login removed from the headset"
    exit 0
fi

session="$root/work/steam-session.json"
[ -f "$session" ] || { echo "no $session: run 'dotnet run --project tools/SteamPhotonProbe -- login' first" >&2; exit 1; }

copy="$(mktemp "$root/work/.steam-session-quest.XXXXXX")"
trap 'rm -f "$copy"' EXIT
python3 - "$session" "$copy" <<'EOF'
import json, sys
source = json.load(open(sys.argv[1]))
keep = {k: source[k] for k in ("AccountName", "RefreshToken", "SteamId", "PersonaName") if k in source}
json.dump(keep, open(sys.argv[2], "w"))
EOF

"$adb" shell mkdir -p "$(dirname "$target")"
"$adb" push "$copy" "$target" >/dev/null
echo "Steam login for $(python3 -c 'import json,sys; print(json.load(open(sys.argv[1]))["AccountName"])' "$session") copied; restart the game to use it"
