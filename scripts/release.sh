#!/usr/bin/env bash
# A release of the Quest builder, made locally: nothing is uploaded anywhere.
#   scripts/release.sh <version> [--notes-ru <file>] [--notes-en <file>] [--update-source <url-or-folder>] [--mac-test]
#                                                    [--windows-only]
# -> work/release/<version>/
#      RepoQuestBuilder-<version>-win-x64.zip   for players: RepoQuestBuilder.exe (the kit inside) + README.txt
#      latest.json, latest.json.sig             what a builder's update check reads (signed with the release key)
#      RepoQuestBuilder-<version>-osx-arm64.zip  for players on Apple Silicon Macs: RepoQuestBuilder.app (ad-hoc signed,
#                                               not notarized: README-mac.txt explains how to open it) + README
#      (--windows-only leaves the Mac zip out of the release and latest.json)
#      RepoQuest-cli-<version>-osx-arm64.zip     with --mac-test: QuestBuilder.Cli with the kit, to test the players'
#                                               flow and updates on this Mac (player-build, update); not in latest.json
# The builder looks for updates in --update-source (compiled in), or in update-source.txt next to the exe: point it at
# a folder holding these files (or serve the folder over HTTP) to test updates without publishing.
# Release key: ~/.config/repoquest/release-signing-key.pem (made on the first release; back it up); its public half is
# builder/release-public-key.txt, compiled into every builder.
set -euo pipefail

root="$(cd "$(dirname "$0")/.." && pwd)"
export DOTNET_ROOT="${DOTNET_ROOT:-$HOME/.dotnet}"
dotnet="$DOTNET_ROOT/dotnet"
version="${1:?usage: scripts/release.sh <version> [--notes-ru <file>] [--notes-en <file>] [--update-source <url>] [--mac-test] [--windows-only]}"
shift
notes_ru="" notes_en="" update_source="" mac_test=0 windows_only=0
while [ $# -gt 0 ]; do
    case "$1" in
        --notes-ru) notes_ru="$2"; shift ;;
        --notes-en) notes_en="$2"; shift ;;
        --update-source) update_source="$2"; shift ;;
        --mac-test) mac_test=1 ;;
        --windows-only) windows_only=1 ;;
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

if [ "$windows_only" = 0 ]; then
    # The Mac window: a .app bundle around the single-file builder (the kit and the osx-arm64 tools inside, like the exe).
    # Not notarized (no Apple Developer ID): it is signed ad hoc, which Apple Silicon requires to run anything at all.
    tools_zip osx-arm64
    stage_app="$out/stage-app"
    app="$stage_app/RepoQuestBuilder.app"
    "$dotnet" publish "$root/builder/QuestBuilder.App" -c Release -r osx-arm64 -v quiet -nologo -p:Version="$version" \
        -p:KitZip="$out/kit.zip" -p:ToolsZip="$out/tools-osx-arm64.zip" -p:UpdateSource="$update_source" \
        -p:DebugType=none -p:DebugSymbols=false -o "$stage_app/publish" | { grep -E "error|warning" || true; }
    mkdir -p "$app/Contents/MacOS" "$app/Contents/Resources"
    cp "$stage_app/publish/RepoQuestBuilder" "$app/Contents/MacOS/RepoQuestBuilder"
    sips -s format png "$root/builder/QuestBuilder.App/Assets/app.ico" --out "$stage_app/icon.png" >/dev/null
    sips -s format icns "$stage_app/icon.png" --out "$app/Contents/Resources/app.icns" >/dev/null
    cat > "$app/Contents/Info.plist" <<PLIST
<?xml version="1.0" encoding="UTF-8"?>
<!DOCTYPE plist PUBLIC "-//Apple//DTD PLIST 1.0//EN" "http://www.apple.com/DTDs/PropertyList-1.0.dtd">
<plist version="1.0">
<dict>
    <key>CFBundleName</key><string>REPO Quest Builder</string>
    <key>CFBundleDisplayName</key><string>REPO Quest Builder</string>
    <key>CFBundleIdentifier</key><string>io.github.dobutsu.repoquestbuilder</string>
    <key>CFBundleExecutable</key><string>RepoQuestBuilder</string>
    <key>CFBundleIconFile</key><string>app.icns</string>
    <key>CFBundlePackageType</key><string>APPL</string>
    <key>CFBundleShortVersionString</key><string>$version</string>
    <key>CFBundleVersion</key><string>$version</string>
    <key>LSMinimumSystemVersion</key><string>12.0</string>
    <key>LSArchitecturePriority</key><array><string>arm64</string></array>
    <key>NSHighResolutionCapable</key><true/>
</dict>
</plist>
PLIST
    codesign --force --deep -s - "$app"
    codesign --verify --deep --strict "$app"
    cp "$root/builder/release/README-mac.txt" "$root/builder/release/THIRD-PARTY-NOTICES.txt" "$stage_app/"
    mkdir -p "$stage_app/zip"
    mv "$app" "$stage_app/README-mac.txt" "$stage_app/THIRD-PARTY-NOTICES.txt" "$stage_app/zip/"
    # ditto keeps the bundle's permissions and signature; zip -r would too, but ditto is what Finder's "Compress" does
    ditto -c -k "$stage_app/zip" "$out/RepoQuestBuilder-$version-osx-arm64.zip"
    assets+=(--asset "osx-arm64=$out/RepoQuestBuilder-$version-osx-arm64.zip")
fi

if [ "$mac_test" = 1 ]; then
    # The players' flow without the window (QuestBuilder.Cli player-build / update), single-file, with the kit and the
    # tools inside; for testing only, not listed in latest.json
    stage_mac="$out/stage-mac"
    "$dotnet" publish "$root/builder/QuestBuilder.Cli" -c Release -r osx-arm64 -v quiet -nologo -p:Version="$version" \
        -p:SingleFile=true -p:KitZip="$out/kit.zip" -p:ToolsZip="$out/tools-osx-arm64.zip" -p:UpdateSource="$update_source" \
        -p:DebugType=none -p:DebugSymbols=false -o "$stage_mac/publish" | { grep -E "error|warning" || true; }
    cp "$stage_mac/publish/QuestBuilder.Cli" "$stage_mac/"
    (cd "$stage_mac" && zip -qX "$out/RepoQuest-cli-$version-osx-arm64.zip" QuestBuilder.Cli)
fi

"$cli" manifest --version "$version" ${notes_ru:+--notes-ru "$notes_ru"} ${notes_en:+--notes-en "$notes_en"} \
    "${assets[@]}" --out "$out/latest.json"
"$cli" sign --key "$key" --public "$public" --file "$out/latest.json"
rm -rf "$out"/stage-* "$out/kit.zip" "$out"/tools-*.zip

ls -la "$out"
echo "release $version: $out (not published)"
