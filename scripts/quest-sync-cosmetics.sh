#!/usr/bin/env bash
# Copy the PC game's cosmetics (MetaSave.es3: tokens, unlocks, equipped items, presets, colours) to the headset.
# The game keeps them in its data folder and syncs that through Steam Cloud; the Quest build has no Steam Cloud yet,
# so its own MetaSave.es3 starts empty. The file is the same on every platform (ES3, same AES password).
#   scripts/quest-sync-cosmetics.sh              from the CrossOver install on this Mac
#   scripts/quest-sync-cosmetics.sh <file>       from a MetaSave.es3 copied from another PC
# The game must be closed on the headset (it rewrites the file when it saves). The headset's previous file is kept
# in work/backups/.
set -euo pipefail

root="$(cd "$(dirname "$0")/.." && pwd)"
adb="/Applications/Unity/Hub/Editor/2022.3.62f3/PlaybackEngines/AndroidPlayer/SDK/platform-tools/adb"
files="/sdcard/Android/data/io.repoxr.quest/files"
src="${1:-$HOME/Library/Application Support/CrossOver/Bottles/Steam/drive_c/users/crossover/AppData/LocalLow/semiwork/Repo/MetaSave.es3}"

[ -f "$src" ] || { echo "no MetaSave.es3 at $src" >&2; exit 1; }
if "$adb" shell pidof io.repoxr.quest >/dev/null; then
    echo "the game is running on the headset: close it first" >&2
    exit 1
fi

mkdir -p "$root/work/backups"
backup="$root/work/backups/quest-MetaSave-$(date +%Y%m%d-%H%M%S).es3"
"$adb" pull "$files/MetaSave.es3" "$backup" >/dev/null 2>&1 && echo "headset's previous save: $backup" || echo "(the headset had no MetaSave.es3 yet)"

"$adb" push "$src" "$files/MetaSave.es3" >/dev/null
# Pushed files belong to the shell user; the game must be able to replace it when it saves
"$adb" shell chmod 666 "$files/MetaSave.es3"

[ "$(shasum "$src" | cut -d' ' -f1)" = "$("$adb" shell sha1sum "$files/MetaSave.es3" | cut -d' ' -f1)" ] \
    || { echo "copy mismatch" >&2; exit 1; }
echo "cosmetics copied from $src"
