#!/usr/bin/env bash
# The players' (Windows) pipeline checked on this Mac: the win-x64 build of the builder's tools runs bake + project
# under CrossOver (Windows uv, Python and .NET), from the same export and kit as a Mac run, and the two Unity projects
# must come out identical. Catches Windows-only bugs (paths, line endings, encodings) before players do.
#   scripts/crossover-windows-check.sh      (needs work/export from a build and the CrossOver bottle "Steam" with REPO)
# Workspaces: C:\RQT in the bottle and work/check-mac; both are deleted at the end.
set -euo pipefail

root="$(cd "$(dirname "$0")/.." && pwd)"
export DOTNET_ROOT="${DOTNET_ROOT:-$HOME/.dotnet}"
dotnet="$DOTNET_ROOT/dotnet"
wine=/Applications/CrossOver.app/Contents/SharedSupport/CrossOver/bin/wine
drive_c="$HOME/Library/Application Support/CrossOver/Bottles/Steam/drive_c"
win_ws="$drive_c/RQT"
mac_ws="$root/work/check-mac"
tools="$root/work/check-tools-win"

[ -d "$root/work/export/REPO/ExportedProject" ] || { echo "no work/export: run scripts/build.sh first" >&2; exit 1; }
"$root/scripts/make-kit.sh" >/dev/null
"$dotnet" build -c Release -v quiet -nologo "$root/builder/QuestBuilder.Cli" | { grep -E "error|warning" || true; }

# The Cli as a self-contained Windows folder: what the players' exe unpacks and runs its tools with
rm -rf "$tools"
"$dotnet" publish "$root/builder/QuestBuilder.Cli" -c Release -r win-x64 -v quiet -nologo -o "$tools" \
    -p:DebugType=none -p:DebugSymbols=false | { grep -E "error|warning" | grep -v MonoMod || true; }

snapshot() {  # <project> -> "<sha256>  <path>" for every file, Unity's caches left out
    (cd "$1" && find . -path ./Library -prune -o -path ./Temp -prune -o -path ./Logs -prune -o -type f -print0 \
        | sort -z | xargs -0 shasum -a 256)
}

echo "== Windows (CrossOver)"
rm -rf "$win_ws"
mkdir -p "$win_ws/export"
cp -cR "$root/work/export/REPO" "$win_ws/export/REPO"
cp -R "$tools" "$win_ws/cli"
(cd "$drive_c" && WINEDEBUG=-all "$wine" --bottle Steam --wait-children "C:\\RQT\\cli\\QuestBuilder.Cli.exe" build \
    --workspace "C:\\RQT" \
    --kit "Z:${root//\//\\}\\work\\kit" \
    --game "C:\\Program Files (x86)\\Steam\\steamapps\\common\\REPO" \
    --unity "Z:\\Applications\\Unity\\Hub\\Editor\\2022.3.62f3" \
    --apk-name check --only bake,project) | tail -5

echo "== macOS"
rm -rf "$mac_ws"
mkdir -p "$mac_ws/export"
cp -cR "$root/work/export/REPO" "$mac_ws/export/REPO"
"$root/builder/QuestBuilder.Cli/bin/Release/net8.0/QuestBuilder.Cli" build --workspace "$mac_ws" --kit "$root/work/kit" \
    --game "$root/work/game" --apk-name check --only bake,project | tail -5

echo "== compare"
differences="$(diff <(snapshot "$mac_ws/unity/REPOQuest") <(snapshot "$win_ws/unity/REPOQuest") || true)"
rm -rf "$win_ws" "$mac_ws" "$tools"
if [ -n "$differences" ]; then
    echo "$differences" | head -40
    echo "the Windows project differs from the Mac one" >&2
    exit 1
fi
echo "identical"
