#!/usr/bin/env bash
# A release of the Quest builder, made locally: nothing is uploaded anywhere.
#   scripts/release.sh <version> [--notes-ru <file>] [--notes-en <file>] [--update-source <url-or-folder>] [--mac-test]
# -> work/release/<version>/
#      RepoQuestBuilder-<version>-win-x64.zip   for players: RepoQuestBuilder.exe (the kit inside) + README.txt
#      latest.json, latest.json.sig             what a builder's update check reads (signed with the release key)
#      RepoQuestBuilder-<version>-osx-arm64.zip with --mac-test: QuestBuilder.Cli with the kit, to test the players'
#                                               flow and updates on this Mac (player-build, update); not for players
# The builder looks for updates in --update-source (compiled in), or in update-source.txt next to the exe: point it at
# a folder holding these files (or serve the folder over HTTP) to test updates without publishing.
# Release key: ~/.config/repoquest/release-signing-key.pem (made on the first release; back it up); its public half is
# builder/release-public-key.txt, compiled into every builder.
set -euo pipefail

root="$(cd "$(dirname "$0")/.." && pwd)"
export DOTNET_ROOT="${DOTNET_ROOT:-$HOME/.dotnet}"
dotnet="$DOTNET_ROOT/dotnet"
version="${1:?usage: scripts/release.sh <version> [--notes-ru <file>] [--notes-en <file>] [--update-source <url>] [--mac-test]}"
shift
notes_ru="" notes_en="" update_source="" mac_test=0
while [ $# -gt 0 ]; do
    case "$1" in
        --notes-ru) notes_ru="$2"; shift ;;
        --notes-en) notes_en="$2"; shift ;;
        --update-source) update_source="$2"; shift ;;
        --mac-test) mac_test=1 ;;
        *) echo "unknown option $1" >&2; exit 64 ;;
    esac
    shift
done
[[ "$version" =~ ^[0-9]+\.[0-9]+\.[0-9]+$ ]] || { echo "version must be X.Y.Z" >&2; exit 64; }
git -C "$root" diff --quiet HEAD -- builder scripts unity-tools tools patches \
    || echo "warning: uncommitted changes in builder/scripts/unity-tools/tools/patches go into this release" >&2

key="$HOME/.config/repoquest/release-signing-key.pem"
public="$root/builder/release-public-key.txt"
cli="$root/builder/QuestBuilder.Cli/bin/Release/net8.0/QuestBuilder.Cli"
"$dotnet" build -c Release -v quiet -nologo "$root/builder/QuestBuilder.Cli" | { grep -E "error|warning" || true; }
if [ ! -f "$key" ]; then
    "$cli" keygen --key "$key" --public "$public"
    echo "NEW RELEASE KEY: back up $key; commit $public" >&2
fi

out="$root/work/release/$version"
rm -rf "$out"
mkdir -p "$out"
"$root/scripts/make-kit.sh" "$version"
(cd "$root/work/kit" && zip -qrX "$out/kit.zip" .)

# The .NET tools as a self-contained folder for <rid> (QuestBuilder.Cli, not bundled: MonoMod hooks the JIT, which a
# single-file app carries inside itself), zipped for embedding (ToolLauncher.For unpacks it into the workspace)
tools_zip() {  # <rid> -> $out/tools-<rid>.zip
    "$dotnet" publish "$root/builder/QuestBuilder.Cli" -c Release -r "$1" -v quiet -nologo -p:Version="$version" \
        -p:DebugType=none -p:DebugSymbols=false -o "$out/stage-tools-$1" | { grep -E "error|warning" || true; }
    (cd "$out/stage-tools-$1" && zip -qrX "$out/tools-$1.zip" .)
}

tools_zip win-x64
stage="$out/stage-win"
"$dotnet" publish "$root/builder/QuestBuilder.App" -c Release -r win-x64 -v quiet -nologo -p:Version="$version" \
    -p:KitZip="$out/kit.zip" -p:ToolsZip="$out/tools-win-x64.zip" -p:UpdateSource="$update_source" \
    -p:DebugType=none -p:DebugSymbols=false -o "$stage/publish" | { grep -E "error|warning" || true; }
cp "$stage/publish/RepoQuestBuilder.exe" "$root/builder/release/README.txt" "$root/builder/release/THIRD-PARTY-NOTICES.txt" "$stage/"
(cd "$stage" && zip -qX "$out/RepoQuestBuilder-$version-win-x64.zip" RepoQuestBuilder.exe README.txt THIRD-PARTY-NOTICES.txt)
assets=(--asset "win-x64=$out/RepoQuestBuilder-$version-win-x64.zip")

if [ "$mac_test" = 1 ]; then
    # The players' flow without the window (QuestBuilder.Cli player-build / update), single-file like the exe, with
    # the kit and the tools inside
    tools_zip osx-arm64
    stage_mac="$out/stage-mac"
    "$dotnet" publish "$root/builder/QuestBuilder.Cli" -c Release -r osx-arm64 -v quiet -nologo -p:Version="$version" \
        -p:SingleFile=true -p:KitZip="$out/kit.zip" -p:ToolsZip="$out/tools-osx-arm64.zip" -p:UpdateSource="$update_source" \
        -p:DebugType=none -p:DebugSymbols=false -o "$stage_mac/publish" | { grep -E "error|warning" || true; }
    cp "$stage_mac/publish/QuestBuilder.Cli" "$stage_mac/"
    (cd "$stage_mac" && zip -qX "$out/RepoQuestBuilder-$version-osx-arm64.zip" QuestBuilder.Cli)
    assets+=(--asset "osx-arm64=$out/RepoQuestBuilder-$version-osx-arm64.zip")
fi

"$cli" manifest --version "$version" ${notes_ru:+--notes-ru "$notes_ru"} ${notes_en:+--notes-en "$notes_en"} \
    "${assets[@]}" --out "$out/latest.json"
"$cli" sign --key "$key" --public "$public" --file "$out/latest.json"
rm -rf "$out"/stage-* "$out/kit.zip" "$out"/tools-*.zip

ls -la "$out"
echo "release $version: $out (not published)"
